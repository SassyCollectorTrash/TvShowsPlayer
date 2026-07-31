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
    private DispatcherTimer? _volumeSaveTimer;
    private bool _refreshingLibrary;
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
                AppLog.ShowError(
                    "Не удалось включить канал.\n\n" + ex.Message +
                    "\n\nОткрой настройки (значок программы у часов, справа внизу — правая кнопка мыши) " +
                    "и проверь папку с сериалами.");
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
            tray.Icon = TrayIconLoader.AppIcon() ?? TrayIconLoader.FromExecutable(config.MpvPath);
            tray.IsVisible = true;
            // единственный checkable пункт = «Режим созвона»; держим ссылку, чтобы
            // и трей, и хоткей меняли одну галочку (без магической строки-заголовка).
            _callModeItem = tray.Menu?.Items.OfType<NativeMenuItem>()
                .FirstOrDefault(i => i.ToggleType == NativeMenuItemToggleType.CheckBox);
        }

        StartHotkeys(config);

        // Трей и хоткеи уже живут — теперь эфир. Если библиотеки нет, приложение
        // ОСТАЁТСЯ в трее и открывает настройки: раньше mpv с пустым плейлистом
        // сразу выходил и уносил приложение с собой, и указать папку было негде.
        var started = StartPlayback(config);
        if (started != PlaybackStart.Started)
        {
            AppLog.Write($"эфир не запущен ({started}) — открываю настройки");
            OnSettings(this, EventArgs.Empty);

            if (started == PlaybackStart.NothingToPlay)
                AppLog.ShowWarning(
                    "В этой папке пока нет серий, готовых к показу.\n\n" +
                    "Если файлы только что скачались, подожди немного — канал возьмёт их сам, " +
                    "когда они докачаются. Посмотреть, что программа нашла, можно в настройках " +
                    "на вкладке «Проверка серий».");

            return;
        }
    }

    /// <summary>Чем закончилась попытка поднять эфир.</summary>
    private enum PlaybackStart
    {
        Started,

        /// <summary>Папка с сериалами не указана или недоступна.</summary>
        NoLibrary,

        /// <summary>Папка есть, но играть нечего: пусто или всё ещё «не отстоялось».</summary>
        NothingToPlay,
    }

    /// <summary>
    /// Собрать плейлист и поднять mpv. Не запускаем пустой канал: чёрный экран без
    /// объяснений выглядит как поломка, а причина у него всегда конкретная.
    /// </summary>
    private PlaybackStart StartPlayback(AppConfig config)
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
            SettleAfter = TimeSpan.FromMinutes(config.SettleMinutes),
        });

        // Плейлиста нет вообще (первый запуск без библиотеки) — запускать нечего.
        if (build.LibraryMissing && !HasEntries(playlist))
            return PlaybackStart.NoLibrary;

        if (build.LibraryMissing)
            AppLog.Write($"библиотека недоступна ({config.CartoonsRoot}) — играем прежний плейлист");

        // Папка есть, но в эфир идти нечему: пусто или всё ещё качается.
        if (!HasEntries(playlist))
        {
            AppLog.Write($"в библиотеке нет готовых серий ({config.CartoonsRoot})");
            return PlaybackStart.NothingToPlay;
        }

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

        return PlaybackStart.Started;
    }

    /// <summary>Есть ли в плейлисте хоть одна серия (строки-комментарии не считаются).</summary>
    private static bool HasEntries(string playlistPath)
    {
        if (!File.Exists(playlistPath))
            return false;

        try
        {
            return File.ReadLines(playlistPath).Any(l => l.Length > 0 && !l.StartsWith('#'));
        }
        catch (IOException)
        {
            return false;
        }
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

            switch (StartPlayback(config))
            {
                case PlaybackStart.Started:
                    AppLog.Write("канал перезапущен");
                    break;
                case PlaybackStart.NoLibrary:
                    AppLog.ShowWarning("Папка с сериалами не выбрана или недоступна.\n\nОткрой настройки, вкладка «Сериалы», и выбери папку.");
                    break;
                case PlaybackStart.NothingToPlay:
                    AppLog.ShowWarning("В папке пока нет серий, готовых к показу.\n\nЕсли файлы только что скачались, подожди — канал возьмёт их сам.");
                    break;
            }
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
    private void StartHotkeys(AppConfig config)
    {
        if (!config.HotkeysEnabled)
        {
            AppLog.Write("горячие клавиши выключены в настройках");
            return;
        }

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
        var bindings = Hotkeys.ForMode(hotkeyMode, config.HotkeyModifiers);
        _hotkeys = new GlobalHotkeys(hwnd, bindings, OnHotkey);
        _hotkeys.Register();

        // Комбо мог занять другой программой — молчать об этом нельзя, иначе
        // «клавиши не работают» без единой подсказки.
        if (_hotkeys.RegisteredCount < bindings.Count)
            AppLog.Write($"хоткеи ({config.HotkeyModifiers}): занято другими программами " +
                         $"{bindings.Count - _hotkeys.RegisteredCount} из {bindings.Count} — смени набор в настройках");
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
            case HotkeyAction.VolumeUp: ChangeVolume(5); break;
            case HotkeyAction.VolumeDown: ChangeVolume(-5); break;
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
                AppLog.ShowWarning(
                    $"Рядом с программой не хватает файла scripts\\{name}.\n\n" +
                    "Канал будет работать, но не запомнит, на какой серии ты остановился. " +
                    "Распакуй скачанный архив целиком, а не только один файл программы.");
            else
                AppLog.Write($"скрипт {name} не найден в {bundled}");
        }
    }

    // ---- обработчики пунктов меню (App.axaml) ----
    private void OnPausePlay(object? sender, EventArgs e) => _ = _controller?.PauseAsync();

    private void OnVolumeUp(object? sender, EventArgs e) => ChangeVolume(5);

    private void OnVolumeDown(object? sender, EventArgs e) => ChangeVolume(-5);

    // Громкость должна пережить перезапуск: mpv.conf генерируется из конфига, поэтому
    // подкрученный уровень запоминаем. Пишем не на каждый шаг, а спустя паузу —
    // пользователь обычно жмёт «тише» несколько раз подряд.
    private void ChangeVolume(int delta)
    {
        _ = _controller?.VolumeAsync(delta);

        _volumeSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _volumeSaveTimer.Stop();
        _volumeSaveTimer.Tick -= OnVolumeSaveTick;
        _volumeSaveTimer.Tick += OnVolumeSaveTick;
        _volumeSaveTimer.Start();
    }

    private void OnVolumeSaveTick(object? sender, EventArgs e)
    {
        _volumeSaveTimer?.Stop();
        _ = SaveVolumeAsync();
    }

    private async Task SaveVolumeAsync()
    {
        try
        {
            if (_controller is null || string.IsNullOrEmpty(_configPath))
                return;

            var volume = (int)Math.Round(await _controller.GetVolumeAsync());
            if (volume <= 0)
                return;

            var config = AppConfig.Load(_configPath);
            if (config.Volume == volume)
                return;

            config.Volume = volume;
            config.Save(_configPath);
            AppLog.Write($"громкость запомнена: {volume}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"не удалось запомнить громкость: {ex.Message}");
        }
    }

    private void OnNextEpisode(object? sender, EventArgs e) => _ = _controller?.NextEpisodeAsync();

    private void OnNextShow(object? sender, EventArgs e) => _ = _controller?.NextShowAsync();

    private void OnResync(object? sender, EventArgs e) => _ = _controller?.ResyncAsync();

    private void OnShowNow(object? sender, EventArgs e) => _ = _controller?.ShowNowAsync();

    private void OnToggleCall(object? sender, EventArgs e) => ToggleCallMute();

    private void OnRestartChannel(object? sender, EventArgs e) => RestartChannel();

    private void OnRefreshLibrary(object? sender, EventArgs e) => _ = RefreshLibraryAsync();

    /// <summary>
    /// Обновить список сериалов ПО КОМАНДЕ пользователя и подхватить изменения «на лету»,
    /// вернувшись на ту же секунду той же серии. Сама программа за библиотекой не следит:
    /// определить «файл докачан» надёжно нельзя (торрент резервирует размер заранее,
    /// куски идут вразнобой), а тихо добавленная обрезанная серия хуже лишнего клика.
    /// </summary>
    private async Task RefreshLibraryAsync()
    {
        if (_refreshingLibrary)
            return;
        _refreshingLibrary = true;

        try
        {
            var config = AppConfig.Load(_configPath);
            var playlistPath = Path.Combine(_configDir, "channel.m3u");
            var statePath = ChannelPaths.ResolveStatePath(_configDir);

            // Где сейчас эфир — чтобы вернуться ровно сюда.
            var currentPath = _controller is null ? null : await _controller.GetCurrentPathAsync();
            var timePos = _controller is null ? 0 : await _controller.GetTimePosAsync();

            if (currentPath is not null && ShowAndRel(currentPath, config.CartoonsRoot) is { } cur)
            {
                var st = ChannelState.Load(statePath);
                st.Current = cur.Show;
                st.Shows[cur.Show] = cur.Rel;
                st.Save(statePath);
            }

            // Сканирование библиотеки — на фоновом потоке: тысячи файлов не должны
            // подвешивать интерфейс.
            var result = await Task.Run(() => ChannelBuilder.Build(new ChannelBuildOptions
            {
                Root = config.CartoonsRoot,
                PlaylistPath = playlistPath,
                StatePath = statePath,
                ExcludedShows = config.ExcludedShows,
                ShowOrder = config.ShowOrder,
                Window = config.Window,
                Step = config.Step,
                CapRotations = config.CapRotations,
                SettleAfter = TimeSpan.FromMinutes(config.SettleMinutes),
            }));

            if (result.LibraryMissing)
            {
                AppLog.Write("обновление списка: папка недоступна");
                AppLog.ShowWarning("Папка с сериалами сейчас недоступна.\n\nЕсли сериалы лежат на внешнем диске — проверь, подключён ли он.");
                return;
            }

            var skipped = result.SkippedEpisodes > 0
                ? $"\n\nПропущено файлов, в которые идёт запись прямо сейчас: {result.SkippedEpisodes}. " +
                  "Когда закачка закончится, обнови список ещё раз."
                : string.Empty;

            if (!result.Rebuilt)
            {
                AppLog.Write($"обновление списка: изменений нет (пропущено {result.SkippedEpisodes})");
                AppLog.ShowWarning("Новых серий не нашлось — список сериалов не изменился." + skipped);
                return;
            }

            AppLog.Write($"обновление списка: {result.ShowCount} сериалов, {result.PlaylistLength} серий, пропущено {result.SkippedEpisodes}");

            if (_controller is null)
                return;

            var pos = ChannelState.Load(statePath).PlaylistPos;
            await _controller.ReloadPlaylistAsync(playlistPath);
            await _controller.SetPlaylistPosAsync(pos);
            if (timePos > 1)
                await _controller.SeekAsync(timePos);   // возвращаемся на ту же секунду

            AppLog.ShowWarning(
                $"Список обновлён: в эфире {result.ShowCount} сериалов, {result.PlaylistLength} серий." + skipped);
        }
        catch (Exception ex)
        {
            AppLog.Write($"обновление библиотеки не удалось: {ex.Message}");
        }
        finally
        {
            _refreshingLibrary = false;
        }
    }

    // Полный путь → (сериал, путь относительно папки сериала) под корнем библиотеки.
    private static (string Show, string Rel)? ShowAndRel(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
            return null;

        var prefix = root.Replace('/', '\\');
        if (!prefix.EndsWith('\\'))
            prefix += "\\";

        var p = path.Replace('/', '\\');
        if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var tail = p[prefix.Length..];
        var slash = tail.IndexOf('\\');
        if (slash <= 0 || slash >= tail.Length - 1)
            return null;

        return (tail[..slash], tail[(slash + 1)..]);
    }

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
        _volumeSaveTimer?.Stop();
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
