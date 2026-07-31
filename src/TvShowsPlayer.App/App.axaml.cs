using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.Win32;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.App;

/// <summary>
/// Трей-приложение канала (dev-срез 1B): поднимает СВОЙ mpv (отдельный pipe,
/// оконный, dev config-dir с channel-osd.lua, но БЕЗ resume.lua — закладка живого
/// AHK-канала не трогается) и управляет им из трея с паритетом меню AHK.
/// Меню объявлено в App.axaml; иконка динамическая (из mpv.exe). Трей-flyout рисуется
/// только при наличии темы (FluentTheme в App.axaml) — без неё пункты меню без
/// шаблонов и popup всплывает «нулевого» размера.
/// </summary>
public partial class App : Application
{
    private ChannelMode _mode;

    private MpvSupervisor? _supervisor;
    private MpvController? _controller;
    private Window? _hotkeyWindow;
    private GlobalHotkeys? _hotkeys;
    private Win32Properties.CustomWndProcHookCallback? _wndProcHook;
    private NativeMenuItem? _callModeItem;
    private SettingsWindow? _settingsWindow;
    private string _configPath = string.Empty;
    private string _configDir = string.Empty;
    private Mutex? _instanceLock;
    private bool _callMuted;
    private bool _shuttingDown;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mode = ResolveMode();

            // Второй экземпляр того же режима недопустим: два канала пишут один и тот
            // же файл состояния (и мигрируют папку под собой) — прогресс просмотра
            // портится. Молча уходим, канал уже работает.
            if (!TryAcquireSingleInstance())
            {
                Environment.Exit(0);
                return;
            }

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Cleanup();

            // Сбой запуска раньше был невидим (процесс просто исчезал). Теперь —
            // понятное сообщение и запись в журнал рядом с конфигом.
            try
            {
                StartChannel();
            }
            catch (Exception ex)
            {
                AppLog.ShowError($"Не удалось запустить канал.\n\n{ex.Message}\n\nПроверь «Настройки → Пути»: папку с мультфильмами и mpv.exe.");
                Shutdown();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Один канал на режим (dev и prod не мешают друг другу — имена разные).
    private bool TryAcquireSingleInstance()
    {
        try
        {
            _instanceLock = new Mutex(initiallyOwned: true, $@"Local\{Branding.AppName}-{_mode}", out var isNew);
            return isNew;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return true;   // не смогли проверить — не мешаем запуску
        }
    }

    private void StartChannel()
    {
        var appDir = AppContext.BaseDirectory;
        var isProd = _mode == ChannelMode.Production;

        // Dev: dev-config рядом с exe (оконный, свой pipe, без resume) — не трогает живой канал.
        // Prod: %LOCALAPPDATA%\LocalTV — вне папки приложения, чтобы обновление (замена
        // папки/распаковка новой версии) НЕ затронуло настройки и прогресс просмотра.
        string configDir;
        if (isProd)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))   // без %LOCALAPPDATA% не кладём конфиг в CWD
                localAppData = appDir;

            LegacyConfigMigration.Run(localAppData);   // разовый перенос со старого имени (Jetix → LocalTV)
            Autostart.MigrateLegacyName();             // и автозапуск-ключ реестра

            // Если перенос папки не удался (напр. занята) — работаем со старой, а имя
            // файла состояния приводим к каноничному ИМЕННО в ней: resume.lua пишет
            // только каноничное имя, иначе прогресс не будет виден и будет затёрт.
            configDir = ChannelPaths.ResolveConfigDir(localAppData);
            LegacyConfigMigration.RenameStateFile(configDir);
        }
        else
        {
            configDir = Path.Combine(appDir, "dev-config");
        }
        AppLog.UseDirectory(configDir);
        AppLog.Write($"старт: режим={_mode}, config-dir={configDir}");

        _configPath = Path.Combine(configDir, "appconfig.json");
        var config = AppConfig.Load(_configPath);
        ResolveMpvPath(config, appDir);   // бандл-mpv, если путь в конфиге не существует

        ProvisionScripts(configDir);

        // Prod-каналу нужен mpv.conf (луп, alang, dynaudnorm, внешняя озвучка, fs-screen,
        // аудио-устройство). Dev — без него: изоляция звука/лупа от живого канала.
        if (isProd)
            File.WriteAllText(Path.Combine(configDir, "mpv.conf"), MpvConfig.Generate(config));

        _configDir = configDir;

        // Иконка трея берётся из mpv.exe (динамическая) → ставим после загрузки XAML,
        // затем делаем видимой, чтобы нативная иконка создалась уже с картинкой.
        var icons = TrayIcon.GetIcons(this);
        if (icons is { Count: > 0 })
        {
            var tray = icons[0];
            tray.Icon = TrayIconLoader.FromExecutable(config.MpvPath);
            tray.IsVisible = true;
            // единственный checkable пункт = «Режим созвона»; держим ссылку, чтобы
            // и трей, и хоткей меняли одну галочку (без магической строки-заголовка).
            _callModeItem = tray.Menu?.Items.OfType<NativeMenuItem>()
                .FirstOrDefault(i => i.ToggleType == NativeMenuItemToggleType.CheckBox);
        }

        StartHotkeys();

        // Трей и хоткеи уже живут — теперь эфир. Если библиотеки нет, приложение
        // ОСТАЁТСЯ в трее и открывает настройки: раньше mpv с пустым плейлистом
        // сразу выходил и уносил приложение с собой, и указать папку было негде.
        if (!StartPlayback(config))
        {
            AppLog.Write("библиотека не указана или недоступна — эфир не запущен, открываю настройки");
            OnSettings(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Собрать плейлист и поднять mpv. <c>false</c> — библиотеки нет (папка не задана
    /// или недоступна), эфир не запускаем.
    /// </summary>
    private bool StartPlayback(AppConfig config)
    {
        var isProd = _mode == ChannelMode.Production;
        var playlist = Path.Combine(_configDir, "channel.m3u");

        var build = ChannelBuilder.Build(new ChannelBuildOptions
        {
            Root = config.CartoonsRoot,
            PlaylistPath = playlist,
            StatePath = ChannelPaths.ResolveStatePath(_configDir),
            ExcludedShows = config.ExcludedShows,
            ShowOrder = config.ShowOrder,
            Window = config.Window,
            Step = config.Step,
            CapRotations = config.CapRotations,
        });

        // Плейлиста нет вообще (первый запуск без библиотеки) — запускать нечего.
        if (build.LibraryMissing && !File.Exists(playlist))
            return false;

        if (build.LibraryMissing)
            AppLog.Write($"библиотека недоступна ({config.CartoonsRoot}) — играем прежний плейлист");

        var pipeName = isProd ? Branding.PipeName : Branding.PipeNameDev;
        var options = new MpvLaunchOptions
        {
            ConfigDir = _configDir,
            Playlist = playlist,
            PipePath = $@"\\.\pipe\{pipeName}",
            ChannelOsdRoot = config.CartoonsRoot,   // имя сериала для next-show / «сейчас»
            ChannelName = config.ChannelName,       // имя канала на заставке
            SplashSeconds = config.SplashSeconds,
            BumperSeconds = config.BumperSeconds,
            PlashkaSeconds = config.PlashkaSeconds,
            ClockEnabled = config.ClockEnabled,
            RetroTheme = config.RetroTheme,
            Fullscreen = isProd,
        };

        _supervisor = new MpvSupervisor(config.MpvPath, MpvLaunchArgs.Build(options));
        _supervisor.Exited += OnMpvExited;
        _supervisor.Start();

        if (isProd)   // канал-«киоск»: убрать окно mpv из панели задач / Alt-Tab
        {
            var supervisor = _supervisor;
            _ = Task.Run(() => WindowStyler.HideFromTaskbar(supervisor.WaitForWindowHandle(10000)));
        }

        _controller = new MpvController(pipeName);
        _ = ConnectControllerAsync(_controller);   // фоновое подключение к pipe (с ретраем)

        return true;
    }

    /// <summary>
    /// Перезапустить эфир с текущими настройками — так применяются аудио-устройство,
    /// экран и экранная графика (раньше для этого требовалось закрывать приложение).
    /// </summary>
    private void RestartChannel()
    {
        try
        {
            var config = AppConfig.Load(_configPath);
            ResolveMpvPath(config, AppContext.BaseDirectory);

            if (_mode == ChannelMode.Production)
                File.WriteAllText(Path.Combine(_configDir, "mpv.conf"), MpvConfig.Generate(config));

            // Окно настроек держит ссылку на старое соединение — закрываем, чтобы
            // «Сейчас в эфире» не показывало мёртвый канал.
            _settingsWindow?.Close();

            _controller?.Dispose();
            _controller = null;
            _supervisor?.Dispose();   // гасит свой mpv без события Exited
            _supervisor = null;

            _callMuted = false;
            if (_callModeItem is not null)
                _callModeItem.IsChecked = false;

            if (!StartPlayback(config))
                AppLog.ShowWarning("Папка с мультфильмами не указана или недоступна.\n\nОткрой «Настройки → Пути» и выбери папку.");
            else
                AppLog.Write("канал перезапущен из трея");
        }
        catch (Exception ex)
        {
            AppLog.ShowError($"Не удалось перезапустить канал.\n\n{ex.Message}");
        }
    }

    // Подключение к mpv фоном: провал больше не теряется молча (иначе трей и хоткеи
    // просто ничего не делают, и понять причину нельзя).
    private static async Task ConnectControllerAsync(MpvController controller)
    {
        try
        {
            await controller.ConnectAsync();
            AppLog.Write(controller.IsConnected
                ? "IPC: подключились к mpv"
                : "IPC: подключиться не удалось — трей и хоткеи работать не будут");
        }
        catch (Exception ex)
        {
            AppLog.Write($"IPC: подключиться не удалось ({ex.Message}) — трей и хоткеи работать не будут");
        }
    }

    // Скрытое окно (1×1, off-screen) — даёт HWND для RegisterHotKey и приёма WM_HOTKEY.
    // dev-режим = Ctrl+Alt+SHIFT+клавиша, чтобы не конфликтовать с хоткеями живого
    // AHK-канала (у него те же комбо без Shift).
    private void StartHotkeys()
    {
        _hotkeyWindow = new Window
        {
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-32000, -32000),
            Title = $"{Branding.AppName} hotkeys",
        };
        _hotkeyWindow.Show();

        var hwnd = _hotkeyWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
            return;

        _wndProcHook = OnWndProc;
        Win32Properties.AddWndProcHookCallback(_hotkeyWindow, _wndProcHook);

        // Dev = Ctrl+Alt+Shift+… (сосуществование с живым AHK), Prod = боевые комбо.
        var hotkeyMode = _mode == ChannelMode.Production ? HotkeyMode.Production : HotkeyMode.Dev;
        var bindings = Hotkeys.ForMode(hotkeyMode);
        _hotkeys = new GlobalHotkeys(hwnd, bindings, OnHotkey);
        _hotkeys.Register();

        // Комбо мог занять другой программой — молчать об этом нельзя, иначе
        // «клавиши не работают» без единой подсказки.
        if (_hotkeys.RegisteredCount < bindings.Count)
            AppLog.Write($"хоткеи: занято другими программами {bindings.Count - _hotkeys.RegisteredCount} из {bindings.Count}");
    }

    private IntPtr OnWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _hotkeys?.HandleMessage(msg, wParam);
        return IntPtr.Zero;
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.Pause: _ = _controller?.PauseAsync(); break;
            case HotkeyAction.VolumeUp: _ = _controller?.VolumeAsync(5); break;
            case HotkeyAction.VolumeDown: _ = _controller?.VolumeAsync(-5); break;
            case HotkeyAction.NextEpisode: _ = _controller?.NextEpisodeAsync(); break;
            case HotkeyAction.ToggleMute: ToggleCallMute(); break;
            case HotkeyAction.Resync: _ = _controller?.ResyncAsync(); break;
            case HotkeyAction.ShowNow: _ = _controller?.ShowNowAsync(); break;
        }
    }

    // Режим: env LOCALTV_MODE перекрывает; иначе Debug→Dev, Release→Prod. Гарантия —
    // локальные Debug-запуски по умолчанию Dev и не трогают живой AHK-канал.
    private static ChannelMode ResolveMode()
    {
        var env = Environment.GetEnvironmentVariable("LOCALTV_MODE");
        if (string.Equals(env, "prod", StringComparison.OrdinalIgnoreCase))
            return ChannelMode.Production;
        if (string.Equals(env, "dev", StringComparison.OrdinalIgnoreCase))
            return ChannelMode.Dev;

#if DEBUG
        return ChannelMode.Dev;
#else
        return ChannelMode.Production;
#endif
    }

    // Если путь к mpv из конфига не существует, но рядом есть бандл-mpv — берём его
    // (свежий получатель работает без ручной настройки).
    private static void ResolveMpvPath(AppConfig config, string appDir)
    {
        if (File.Exists(config.MpvPath))
            return;

        var bundled = Path.Combine(appDir, "mpv", "mpv.exe");
        if (File.Exists(bundled))
            config.MpvPath = bundled;
    }

    // Провизионим Lua из бандла приложения (<appdir>/scripts) в config-dir по режиму:
    // Dev → channel-osd.lua; Prod → + resume.lua (resume работает).
    private void ProvisionScripts(string configDir)
    {
        var scriptsDir = Path.Combine(configDir, "scripts");
        Directory.CreateDirectory(scriptsDir);
        var bundled = Path.Combine(AppContext.BaseDirectory, "scripts");

        foreach (var name in ChannelScripts.ForMode(_mode))
        {
            var source = Path.Combine(bundled, name);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(scriptsDir, name), overwrite: true);
                continue;
            }

            // Без resume.lua канал молча перестаёт запоминать место просмотра —
            // это как раз тот случай, о котором нужно сказать вслух.
            if (name == ChannelScripts.Resume)
                AppLog.ShowWarning($"Рядом с программой нет файла scripts\\{name}.\n\nКанал будет работать, но НЕ запомнит, на какой серии ты остановился. Распакуй архив целиком.");
            else
                AppLog.Write($"скрипт {name} не найден в {bundled}");
        }
    }

    // ---- обработчики пунктов меню (App.axaml) ----
    private void OnPausePlay(object? sender, EventArgs e) => _ = _controller?.PauseAsync();

    private void OnVolumeUp(object? sender, EventArgs e) => _ = _controller?.VolumeAsync(5);

    private void OnVolumeDown(object? sender, EventArgs e) => _ = _controller?.VolumeAsync(-5);

    private void OnNextEpisode(object? sender, EventArgs e) => _ = _controller?.NextEpisodeAsync();

    private void OnNextShow(object? sender, EventArgs e) => _ = _controller?.NextShowAsync();

    private void OnResync(object? sender, EventArgs e) => _ = _controller?.ResyncAsync();

    private void OnShowNow(object? sender, EventArgs e) => _ = _controller?.ShowNowAsync();

    private void OnToggleCall(object? sender, EventArgs e) => ToggleCallMute();

    private void OnRestartChannel(object? sender, EventArgs e) => RestartChannel();

    // Общий тумблер «Режим созвона» для пункта трея И хоткея — одно состояние,
    // одна галочка (CallModeItem объявлен в App.axaml), чтобы клавиша и меню совпадали.
    private void ToggleCallMute()
    {
        _callMuted = !_callMuted;
        if (_callModeItem is not null)
            _callModeItem.IsChecked = _callMuted;
        _ = _controller?.SetMuteAsync(_callMuted);
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        // Окно грузит свежую копию конфига из файла (правки изолированы до «Сохранить»).
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
                AppConfig.Load(_configPath), _configPath, _controller, RestartChannel);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnQuit(object? sender, EventArgs e) => _ = QuitAsync();

    private async Task QuitAsync()
    {
        try
        {
            if (_controller is not null)
                await _controller.QuitAsync();   // вежливо просим mpv закрыться
        }
        catch (Exception ex)
        {
            AppLog.Write($"выход: mpv не ответил ({ex.Message}) — закрываемся принудительно");
        }

        Shutdown();   // выход обязан сработать, даже если IPC отвалился
    }

    private void OnMpvExited(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(Shutdown);   // mpv закрылся → закрываем приложение

    private void Shutdown()
    {
        if (_shuttingDown)
            return;
        _shuttingDown = true;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void Cleanup()
    {
        _hotkeys?.Dispose();   // снимает RegisterHotKey
        if (_wndProcHook is not null && _hotkeyWindow is not null)
            Win32Properties.RemoveWndProcHookCallback(_hotkeyWindow, _wndProcHook);
        _hotkeyWindow?.Close();
        _controller?.Dispose();
        _supervisor?.Dispose();   // гасит свой mpv, если ещё жив

        if (_instanceLock is not null)
        {
            try { _instanceLock.ReleaseMutex(); } catch (ApplicationException) { }
            _instanceLock.Dispose();
        }
    }
}
