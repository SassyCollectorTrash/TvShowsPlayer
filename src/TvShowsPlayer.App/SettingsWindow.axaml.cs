using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.App;

/// <summary>
/// Окно настроек (1c). Вкладка «Плейлист» — живой медиа-менеджер: порядок сериалов
/// перетаскиванием (drag-drop), галочки «в канале», карточка «сейчас в эфире» и
/// очередь «Далее» с авто-обновлением по IPC, пересборка с сохранением играющего.
/// </summary>
public partial class SettingsWindow : Window
{
    private const string DragFormat = "localtv.show";

    private readonly AppConfig _config;
    private readonly string _configPath;
    private readonly MpvController? _controller;

    private readonly Dictionary<string, Show> _showsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ShowRow> _showRows = new();

    private ListBox _showsList = null!;
    private ListBox _queueList = null!;
    private ComboBox _audioBox = null!;
    private CheckBox _autostartBox = null!;
    private TextBlock _nowPlaying = null!;
    private CheckBox _preserveCurrent = null!;
    private TextBlock _playlistStatus = null!;
    private TextBlock _status = null!;
    private TextBlock _versionText = null!;
    private Button _updateButton = null!;
    private Button _checkUpdateButton = null!;
    private string? _releaseUrl;
    private readonly CancellationTokenSource _closing = new();

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly Version AppVersion =
        typeof(SettingsWindow).Assembly.GetName().Version ?? new Version(1, 0, 0);

    private ShowRow? _dragItem;
    private ShowRow? _draggingRow;
    private Point _dragStart;
    private DispatcherTimer? _refreshTimer;

    public SettingsWindow() : this(new AppConfig(), string.Empty, null)
    {
    }

    public SettingsWindow(AppConfig config, string configPath, MpvController? controller)
    {
        _config = config;
        _configPath = configPath;
        _controller = controller;
        AvaloniaXamlLoader.Load(this);
        DataContext = _config;

        _showsList = this.FindControl<ListBox>("ShowsList")!;
        _queueList = this.FindControl<ListBox>("QueueList")!;
        _audioBox = this.FindControl<ComboBox>("AudioDeviceBox")!;
        _nowPlaying = this.FindControl<TextBlock>("NowPlaying")!;
        _preserveCurrent = this.FindControl<CheckBox>("PreserveCurrent")!;
        _playlistStatus = this.FindControl<TextBlock>("PlaylistStatus")!;
        _status = this.FindControl<TextBlock>("StatusText")!;
        _autostartBox = this.FindControl<CheckBox>("AutostartBox")!;
        _autostartBox.IsChecked = Autostart.IsEnabled();
        _versionText = this.FindControl<TextBlock>("VersionText")!;
        _updateButton = this.FindControl<Button>("UpdateButton")!;
        _checkUpdateButton = this.FindControl<Button>("CheckUpdateButton")!;
        _versionText.Text = $"{Branding.AppName} v{AppVersion.ToString(3)}";
        Closed += (_, _) => _closing.Cancel();   // не дописываем в контролы закрытого окна

        _ = PopulateAudioDevicesAsync();
        PopulateShows();
        SetupDragDrop();
        SetupAutoRefresh();

        _ = CheckUpdatesAsync(silent: true);   // тихая проверка обновлений при открытии
    }

    private void OnAutostartToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box)
            Autostart.Set(box.IsChecked == true);
    }

    // ---- проверка обновлений ----
    private async void OnCheckUpdates(object? sender, RoutedEventArgs e) =>
        await CheckUpdatesAsync(silent: false);

    private void OnOpenReleases(object? sender, RoutedEventArgs e)
    {
        // ссылка приходит из ответа GitHub — открываем только https, иначе свою страницу
        var url = Uri.TryCreate(_releaseUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
            ? _releaseUrl!
            : Branding.ReleasesUrl;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // браузер не открылся — не критично
        }
    }

    private async Task CheckUpdatesAsync(bool silent)
    {
        try
        {
            if (!silent)
            {
                _status.Text = "Проверяю обновления…";
                _checkUpdateButton.IsEnabled = false;   // без гонки повторных кликов
            }

            var info = await UpdateChecker.FetchLatestAsync(
                Http, Branding.RepoOwner, Branding.RepoName, _closing.Token);

            if (_closing.IsCancellationRequested)
                return;   // окно закрыли, пока шёл запрос

            if (info is null)
            {
                if (!silent)
                    _status.Text = "Не удалось проверить обновления";
                return;
            }

            if (UpdateChecker.HasUpdate(AppVersion, info))
            {
                _releaseUrl = info.ReleaseUrl ?? Branding.ReleasesUrl;
                _updateButton.Content = $"Скачать обновление v{info.Version.ToString(3)}";
                _updateButton.IsVisible = true;
                _status.Text = $"Доступна новая версия v{info.Version.ToString(3)}";
            }
            else
            {
                _updateButton.IsVisible = false;
                if (!silent)
                    _status.Text = "Установлена последняя версия";
            }
        }
        catch
        {
            if (!silent)
                _status.Text = "Не удалось проверить обновления";
        }
        finally
        {
            _checkUpdateButton.IsEnabled = true;
        }
    }

    // ---- сериалы: порядок (drag-drop / ↑↓) + «в канале» ----
    private void PopulateShows()
    {
        var scanned = ScanShows();
        _showsByName.Clear();
        foreach (var s in scanned)
            _showsByName[s.Name] = s;

        var ordered = ShowOrdering.Apply(scanned, _config.ShowOrder);
        var excluded = new HashSet<string>(_config.ExcludedShows, StringComparer.OrdinalIgnoreCase);

        _showRows.Clear();
        foreach (var s in ordered)
            _showRows.Add(new ShowRow { Name = s.Name, IsIncluded = !excluded.Contains(s.Name) });

        _showsList.ItemsSource = _showRows;
    }

    private IReadOnlyList<Show> ScanShows()
    {
        try
        {
            return ShowScanner.Scan(_config.CartoonsRoot);
        }
        catch
        {
            return Array.Empty<Show>();
        }
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e) => Move(-1);

    private void OnMoveDown(object? sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        var i = _showsList.SelectedIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _showRows.Count)
            return;

        _showRows.Move(i, j);
        _showsList.SelectedIndex = j;
    }

    private void SetupDragDrop()
    {
        _showsList.AddHandler(PointerPressedEvent, ShowsPointerPressed, RoutingStrategies.Tunnel);
        _showsList.AddHandler(PointerMovedEvent, ShowsPointerMoved, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(_showsList, true);
        _showsList.AddHandler(DragDrop.DragOverEvent, ShowsDragOver);
        _showsList.AddHandler(DragDrop.DropEvent, ShowsDrop);
    }

    private void ShowsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragItem = (e.Source as Control)?.DataContext as ShowRow;
        _dragStart = e.GetPosition(_showsList);
    }

    private async void ShowsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null)
            return;
        if (!e.GetCurrentPoint(_showsList).Properties.IsLeftButtonPressed)
        {
            _dragItem = null;
            return;
        }

        var p = e.GetPosition(_showsList);
        if (Math.Abs(p.X - _dragStart.X) < 4 && Math.Abs(p.Y - _dragStart.Y) < 4)
            return;

        var row = _dragItem;
        _dragItem = null;
        _draggingRow = row;
        _showsList.SelectedItem = row;

        var data = new DataObject();
        data.Set(DragFormat, row);
        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        finally
        {
            _draggingRow = null;
        }
    }

    // Живая перестановка: пока тянем, элемент переезжает под курсор — список
    // расступается в реальном времени. На Drop уже всё на месте.
    private void ShowsDragOver(object? sender, DragEventArgs e)
    {
        if (_draggingRow is null || !e.Data.Contains(DragFormat))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;

        var target = (e.Source as Control)?.DataContext as ShowRow;
        if (target is null || ReferenceEquals(target, _draggingRow))
            return;

        var from = _showRows.IndexOf(_draggingRow);
        var to = _showRows.IndexOf(target);
        if (from >= 0 && to >= 0 && from != to)
        {
            _showRows.Move(from, to);
            _showsList.SelectedItem = _draggingRow;
        }
    }

    private void ShowsDrop(object? sender, DragEventArgs e)
    {
        _draggingRow = null;   // уже переставлено в DragOver
    }

    private void ApplyShowConfig()
    {
        _config.ShowOrder = _showRows.Select(r => r.Name).ToList();
        _config.ExcludedShows = _showRows.Where(r => !r.IsIncluded).Select(r => r.Name).ToList();
    }

    // ---- живая панель «Сейчас в эфире» + «Далее» ----
    private void SetupAutoRefresh()
    {
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => _ = RefreshNowAndNext();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer?.Stop();
        _ = RefreshNowAndNext();
    }

    private async Task RefreshNowAndNext()
    {
        // IPC-граница + async void выше: ошибка не должна ронять приложение.
        try
        {
            if (_controller is null)
            {
                _nowPlaying.Text = "(нет связи с mpv)";
                return;
            }

            var pos = await _controller.GetPlaylistPosAsync();
            var path = await _controller.GetCurrentPathAsync();
            var now = ShowAndRel(path);
            _nowPlaying.Text = now is { } v ? $"{v.Show} · {Path.GetFileName(path)}" : "—";

            var entries = ReadDevPlaylist();
            var queue = new List<QueueItem>();
            for (var i = pos + 1; i < entries.Count && queue.Count < 16; i++)
            {
                var q = ShowAndRel(entries[i]);
                var label = q is { } qq ? $"{qq.Show} · {Path.GetFileName(entries[i])}" : Path.GetFileName(entries[i]);
                queue.Add(new QueueItem { Index = i, Label = label ?? string.Empty });
            }

            _queueList.ItemsSource = queue;
        }
        catch
        {
            // молча — фоновый опрос
        }
    }

    private void OnRefreshNow(object? sender, RoutedEventArgs e) => _ = RefreshNowAndNext();

    private async void OnQueueJump(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_controller is null || _queueList.SelectedItem is not QueueItem item)
                return;

            await _controller.SetPlaylistPosAsync(item.Index);
            await RefreshNowAndNext();
        }
        catch (Exception ex)
        {
            _playlistStatus.Text = $"Ошибка перехода: {ex.Message}";
        }
    }

    // ---- пересборка (с сохранением играющего сериала) ----
    private async void OnRebuild(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Пересборка меняет канал — значит и конфиг должен лечь на диск, иначе
            // следующий запуск соберёт по старому порядку и канал «прыгнет».
            ApplyShowConfig();
            if (!string.IsNullOrEmpty(_configPath))
                _config.Save(_configPath);

            var dir = Path.GetDirectoryName(_configPath) ?? AppContext.BaseDirectory;
            var playlistPath = Path.Combine(dir, "channel.m3u");
            var statePath = ChannelPaths.ResolveStatePath(dir);

            if (_preserveCurrent.IsChecked == true && _controller is not null)
            {
                var sr = ShowAndRel(await _controller.GetCurrentPathAsync());
                if (sr is { } v)
                {
                    var st = ChannelState.Load(statePath);
                    st.Current = v.Show;
                    st.Shows[v.Show] = v.Rel;
                    st.Save(statePath);
                }
            }

            var result = ChannelBuilder.Build(new ChannelBuildOptions
            {
                Root = _config.CartoonsRoot,
                PlaylistPath = playlistPath,
                StatePath = statePath,
                ExcludedShows = _config.ExcludedShows,
                ShowOrder = _config.ShowOrder,
                Window = _config.Window,
                Step = _config.Step,
                CapRotations = _config.CapRotations,
                Force = true,
            });

            var pos = ChannelState.Load(statePath).PlaylistPos;
            if (_controller is not null)
            {
                await _controller.ReloadPlaylistAsync(playlistPath);
                await _controller.SetPlaylistPosAsync(pos);
            }

            _playlistStatus.Text = $"Пересобрано: {result.ShowCount} сериалов, {result.PlaylistLength} записей · указатель → {pos}.";
            await RefreshNowAndNext();
        }
        catch (Exception ex)
        {
            _playlistStatus.Text = $"Ошибка пересборки: {ex.Message}";
        }
    }

    // Плейлист канала — тысячи строк; фоновое обновление идёт раз в 2 секунды,
    // поэтому перечитываем файл только когда он реально изменился.
    private List<string> _playlistCache = new();
    private DateTime _playlistStamp = DateTime.MinValue;

    private List<string> ReadDevPlaylist()
    {
        var dir = Path.GetDirectoryName(_configPath) ?? AppContext.BaseDirectory;
        var m3u = Path.Combine(dir, "channel.m3u");
        if (!File.Exists(m3u))
            return new List<string>();

        var stamp = File.GetLastWriteTimeUtc(m3u);
        if (stamp == _playlistStamp)
            return _playlistCache;

        _playlistCache = File.ReadAllLines(m3u)
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
        _playlistStamp = stamp;

        return _playlistCache;
    }

    // Полный путь → (сериал, rel относительно папки сериала) под корнем библиотеки.
    private (string Show, string Rel)? ShowAndRel(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var root = _config.CartoonsRoot.Replace('/', '\\');
        if (!root.EndsWith('\\'))
            root += "\\";

        var p = path.Replace('/', '\\');
        if (!p.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;

        var tail = p[root.Length..];
        var slash = tail.IndexOf('\\');
        if (slash <= 0 || slash >= tail.Length - 1)
            return null;

        return (tail[..slash], tail[(slash + 1)..]);
    }

    // ---- аудио-устройства ----
    // Список устройств добывается запуском mpv — делаем это в фоне, иначе окно
    // настроек «подвисает» на открытии, пока mpv перечисляет звуковые выходы.
    private async Task PopulateAudioDevicesAsync()
    {
        var mpvPath = _config.MpvPath;
        var devices = await Task.Run(() => QueryAudioDevices(mpvPath));
        if (_closing.IsCancellationRequested)
            return;

        _audioBox.ItemsSource = devices;
        _audioBox.SelectedItem = devices.FirstOrDefault(d => d.Id == _config.AudioDevice)
                                 ?? devices.FirstOrDefault(d => d.Id == "auto");
    }

    private static IReadOnlyList<AudioDevice> QueryAudioDevices(string mpvPath)
    {
        try
        {
            var psi = new ProcessStartInfo(mpvPath, "--audio-device=help")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return Array.Empty<AudioDevice>();

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return AudioDevices.Parse(output);
        }
        catch
        {
            return Array.Empty<AudioDevice>();
        }
    }

    private void OnAudioDeviceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: AudioDevice device })
            _config.AudioDevice = device.Id;
    }

    // ---- пикеры путей ----
    private async void OnBrowseMpv(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выбери mpv.exe",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Программа") { Patterns = new[] { "*.exe" } } },
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            SetText("MpvPathBox", path);
    }

    private async void OnBrowseCartoons(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выбери папку с мультфильмами",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            _config.CartoonsRoot = path;
            SetText("CartoonsRootBox", path);
            PopulateShows();
        }
    }

    private void SetText(string controlName, string text)
    {
        if (this.FindControl<TextBox>(controlName) is { } box)
            box.Text = text;
    }

    // ---- сохранить ----
    private void OnSave(object? sender, RoutedEventArgs e)
    {
        ApplyShowConfig();
        if (!string.IsNullOrEmpty(_configPath))
            _config.Save(_configPath);

        _status.Text = $"Сохранено · {DateTime.Now:HH:mm:ss}.";
    }
}

/// <summary>Строка списка сериалов: имя, порядок (позиция) и «в канале».</summary>
public sealed class ShowRow
{
    public string Name { get; init; } = string.Empty;
    public bool IsIncluded { get; set; } = true;
}

/// <summary>Элемент очереди «Далее»: индекс в плейлисте и подпись.</summary>
public sealed class QueueItem
{
    public int Index { get; init; }
    public string Label { get; init; } = string.Empty;
}
