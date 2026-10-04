using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using GameLibrary.Core;
using GameLibrary.Infrastructure;

namespace GameLibrary.App;

public partial class App : Application
{
    private SingleInstance? instance;
    private TrayService? tray;
    private HotkeyService? hotkey;
    private LibraryWatcher? watcher;
    private GameProcessMonitor? processMonitor;
    private OverlayWindow? overlay;
    private SettingsWindow? settingsWindow;
    private readonly SemaphoreSlim scanLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private bool shuttingDown;
    private bool ready;
    private bool smokeTest;
    public SettingsService Store { get; private set; } = null!;
    public Settings Settings { get; private set; } = new();
    public IReadOnlyList<Game> Games { get; private set; } = [];
    public string Status { get; private set; } = "Reading local library…";
    public event Action? LibraryChanged;
    public static App CurrentApp => (App)Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        smokeTest = e.Args.Contains("--smoke-test");
        try
        {
            instance = new SingleInstance();
            if (!instance.IsFirst) { await instance.NotifyAsync(e.Args.Contains("--exit") ? (byte)2 : (byte)1); Shutdown(); return; }
            if (e.Args.Contains("--exit")) { Shutdown(); return; }
            InputMode.Initialize();
            var dataIndex = Array.IndexOf(e.Args, "--data-dir");
            var root = dataIndex >= 0 && dataIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[dataIndex + 1]) : null;
            Store = new SettingsService(root);
            DispatcherUnhandledException += (_, args) =>
            {
                Store.Log(args.Exception.ToString());
                if (smokeTest) { args.Handled = true; Shutdown(1); return; }
                // Do not silently continue after an unexpected application-level failure.
                MessageBox.Show("Game Library encountered an error. Details were written to app.log.\n" + args.Exception.Message, "Game Library");
            };
            tray = new TrayService(ShowLibrary, ShowSettings, () => _ = RescanAsync(), Shutdown, Notify, favorite => { ShowLibrary(); overlay?.SetFavoritesOnly(favorite); });
            _ = instance.ListenAsync(() => Dispatcher.BeginInvoke(ShowLibrary), () => Dispatcher.BeginInvoke(() => Shutdown()));
            Settings = await Task.Run(Store.LoadSettings);
            Games = (await Task.Run(Store.LoadCache)).Games;
            processMonitor = new GameProcessMonitor(Store.Log);
            processMonitor.Changed += update => Dispatcher.BeginInvoke(() => SessionChanged(update));
            watcher = new LibraryWatcher(() => Dispatcher.BeginInvoke(() => _ = RescanAsync()));
            hotkey = new HotkeyService();
            hotkey.Pressed += ToggleLibrary;
            if (!hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey)) Notify("Library shortcut is in use. Choose another in Settings.");
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            ready = true;
            if (Store.RecoveryMessage is { } message) Notify(message);
            if (!e.Args.Contains("--background")) ShowLibrary();
            await RescanAsync();
            if (e.Args.Contains("--smoke-test")) await SmokeTestAsync(e.Args.Contains("--stress"));
            else if (e.Args.Contains("--idle-diagnostics")) await IdleDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            Store?.Log(ex.ToString());
            if (!smokeTest) MessageBox.Show("Game Library could not start.\n" + ex.Message, "Game Library", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public void ShowLibrary()
    {
        if (!ready || shuttingDown) return;
        if (overlay is null)
        {
            overlay = new OverlayWindow(this) { ShowActivated = settingsWindow?.IsActive != true };
            overlay.Closed += (sender, _) => { if (MainWindow == sender) MainWindow = null; overlay = null; CoverCache.Shared.Trim(16 * 1024 * 1024); };
            overlay.Show();
        }
        else { overlay.Show(); overlay.Activate(); }
    }
    public void HideLibrary() => overlay?.Close();
    private void ToggleLibrary() { if (overlay is null) ShowLibrary(); else HideLibrary(); }
    public void ShowSettings()
    {
        if (!ready || shuttingDown) return;
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(this);
            settingsWindow.Closed += (sender, _) => { if (MainWindow == sender) MainWindow = null; settingsWindow = null; };
            settingsWindow.Show();
        }
        else settingsWindow.Activate();
    }
    private void DisplayChanged(object? sender, EventArgs args) => Dispatcher.BeginInvoke(() => { if (overlay is not null) NativeWindow.Place(overlay, Settings.MonitorDevice); });

    public async Task RescanAsync()
    {
        if (!ready || shuttingDown) return;
        await scanLock.WaitAsync();
        try
        {
            if (shuttingDown) return;
            Status = "Scanning Steam metadata…";
            LibraryChanged?.Invoke();
            var snapshot = Store.Clone(Settings);
            var result = await new SteamProvider().ScanAsync(snapshot, lifetime.Token);
            var custom = await new CustomGameProvider().ScanAsync(Settings, lifetime.Token);
            var found = result.Games.ToDictionary(g => g.Id);
            foreach (var previous in Games.Where(g => g.Source is GameSource.Steam or GameSource.SteamShortcut && !found.ContainsKey(g.Id)))
                if (previous.LibraryDirectory is { } library && result.UnavailableLibraries.Contains(library, StringComparer.OrdinalIgnoreCase))
                    found[previous.Id] = previous with { Installed = false };
            Games = found.Values.Concat(custom.Games).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var cached = Games.ToArray();
            await Task.Run(() => Store.SaveCache(cached));
            watcher?.Reset(result.WatchDirectories);
            Status = result.Warnings.Count == 0 ? $"{Games.Count(g => g.Installed)} installed · Local library up to date" : result.Warnings[0];
            foreach (var warning in result.Warnings) Store.Log(warning);
            LibraryChanged?.Invoke();
        }
        catch (OperationCanceledException) when (shuttingDown) { }
        catch (Exception ex) { Store.Log(ex.ToString()); Status = "Scan failed: " + ex.Message; LibraryChanged?.Invoke(); }
        finally { scanLock.Release(); }
    }

    public async Task LaunchAsync(Game game)
    {
        try
        {
            if (processMonitor is null || processMonitor.IsActive) { Notify("Another launch or game session is already being tracked."); return; }
            await processMonitor.LaunchAsync(game);
        }
        catch (Exception ex) { Notify(ex.Message); }
    }
    private void SessionChanged(SessionUpdate update)
    {
        if (shuttingDown) return;
        Status = update.Message;
        switch (update.State)
        {
            case SessionState.Starting: HideLibrary(); break;
            case SessionState.Running:
                Settings.For(update.Game.Id).LastPlayed = DateTimeOffset.UtcNow;
                SavePreferences();
                HideLibrary();
                break;
            case SessionState.Exited:
                GameStatistics.RecordSession(update.Game, Settings.For(update.Game.Id), update.Minutes, DateTimeOffset.UtcNow);
                SavePreferences();
                if (Settings.RestoreAfterExit && NativeWindow.DesktopHasFocus()) ShowLibrary();
                else if (Settings.RestoreAfterExit) Notify(update.Message + ". Open Library from the tray or shortcut.");
                break;
            case SessionState.Unknown:
            case SessionState.Failed:
                Notify(update.Message);
                if (NativeWindow.DesktopHasFocus()) ShowLibrary();
                break;
        }
        LibraryChanged?.Invoke();
    }
    public void ToggleFavorite(Game game) { var preference = Settings.For(game.Id); preference.Favorite = !preference.Favorite; SavePreferences(); }
    public void ToggleCategory(Game game, string category)
    {
        var values = Settings.For(game.Id).Categories;
        if (!values.Remove(category)) values.Add(category);
        SavePreferences();
    }
    private void SavePreferences()
    {
        try { Store.SaveSettings(Settings); LibraryChanged?.Invoke(); }
        catch (Exception ex) { Store.Log(ex.ToString()); Notify("Could not save preferences: " + ex.Message); }
    }
    public async Task ApplySettingsAsync(Settings value, bool startup, IReadOnlySet<string>? editedCustom = null)
    {
        value = Store.Clone(value); // Keep the still-open settings editor isolated from live settings.
        // Preserve favorites/session changes that occurred while the settings editor was open.
        var preferences = Store.Clone(Settings).Games;
        foreach (var id in editedCustom ?? new HashSet<string>())
        {
            if (!value.Games.TryGetValue(id, out var edited)) continue;
            if (!preferences.TryGetValue(id, out var current)) preferences[id] = current = new();
            current.Favorite = edited.Favorite;
            current.Categories = edited.Categories;
        }
        foreach (var preference in preferences.Values) preference.Categories.RemoveAll(c => !value.Categories.Contains(c));
        value.Games = preferences;
        if (!hotkey!.Register(value.HotkeyModifiers, value.HotkeyKey))
        {
            hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);
            throw new InvalidOperationException("That shortcut is already in use. Choose another shortcut.");
        }
        try
        {
            if (startup != TrayService.StartsWithWindows) TrayService.SetStartup(startup);
            Store.SaveSettings(value);
        }
        catch { hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey); throw; }
        Settings = value;
        tray?.RefreshStartup();
        var visible = overlay is not null;
        HideLibrary();
        if (visible) ShowLibrary();
        await RescanAsync();
    }
    public void Notify(string message) { Status = message; tray?.Notify(message); LibraryChanged?.Invoke(); }

    private async Task SmokeTestAsync(bool stress)
    {
        if (stress)
        {
            Games = Enumerable.Range(1, 5000).Select(i => new Game { Id = "test:" + i, Name = $"Test game {i:0000}", Installed = true, Source = GameSource.Custom }).ToArray();
            LibraryChanged?.Invoke();
        }
        ShowLibrary();
        var secondInstanceExited = false;
        if (Environment.ProcessPath is { } executable)
        {
            using var second = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, Arguments = "--background" });
            if (second is not null)
            {
                await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                secondInstanceExited = second.ExitCode == 0;
            }
        }
        await Task.Delay(1500);
        overlay?.Activate();
        await Task.Delay(100);
        overlay?.UpdateLayout();
        var testedGameCount = Games.Count;
        var firstSelection = overlay?.SelectedGameId;
        var firstHasFocus = overlay?.GameCardHasFocus == true && overlay.FirstGameSelected;
        var withinWorkArea = overlay is not null && NativeWindow.FitsWorkArea(overlay);
        if (!withinWorkArea) throw new InvalidOperationException("Smoke test: overlay covers the taskbar work area.");
        if (Games.Count > 0 && !firstHasFocus) throw new InvalidOperationException("Smoke test: first game was not focused.");
        overlay?.HandleNavigation(LibraryAction.Right);
        var navigationMoved = Games.Count < 2 || overlay?.SelectedGameId != firstSelection;
        overlay?.HandleNavigation(LibraryAction.Left);
        if (!navigationMoved || overlay?.SelectedGameId != firstSelection) throw new InvalidOperationException("Smoke test: game navigation failed.");
        if (overlay is not null) await overlay.VerifyCarouselAnchorsAsync();
        InputMode.Current.UseGamepad();
        await Task.Delay(60);
        var controllerScrollbarsHidden = overlay?.ScrollbarsMatchInput == true;
        InputMode.Current.UseMouse();
        await Task.Delay(60);
        var scrollbarHoverOnly = overlay?.ScrollbarsMatchInput == true;
        if (!controllerScrollbarsHidden || !scrollbarHoverOnly) throw new InvalidOperationException("Smoke test: scrollbar input visibility or thickness is incorrect. " + overlay?.ScrollbarDiagnostics);
        var cardCount = overlay?.RealizedCards ?? 0;
        if (Games.Count > 0 && cardCount == 0) throw new InvalidOperationException("Smoke test: no game cards were realized.");
        if (stress && cardCount > 100) throw new InvalidOperationException("Smoke test: carousel virtualization is not bounded.");
        if (!secondInstanceExited) throw new InvalidOperationException("Smoke test: second-instance forwarding failed.");
        if (overlay is not null)
        {
            if (overlay.SearchIsOpen) throw new InvalidOperationException("Smoke test: search should be hidden initially.");
            overlay.ShowSearch();
            if (!overlay.SearchIsOpen || System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
                throw new InvalidOperationException("Smoke test: on-demand search did not receive focus.");
            overlay.DismissSearch();
            await Task.Delay(100);
            if (overlay.SearchIsOpen || (Games.Count > 0 && !overlay.GameCardHasFocus))
                throw new InvalidOperationException($"Smoke test: dismissing search did not restore the library. active={overlay.IsActive}, search={overlay.SearchIsOpen}, focus={System.Windows.Input.Keyboard.FocusedElement}, selected={overlay.SelectedGameId}, cards={overlay.RealizedCards}");
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)overlay.ActualWidth, (int)overlay.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(overlay);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(Store.Root, "overlay.png")); encoder.Save(file);
        }
        ShowSettings();
        await Task.Delay(600);
        var controllerPausedForSettings = overlay?.GamepadRunning != true;
        if (!controllerPausedForSettings) throw new InvalidOperationException("Smoke test: controller input was active behind Settings.");
        var editor = settingsWindow;
        editor?.ExerciseAppearanceInputs();
        if (editor is not null) await editor.SaveAsync();
        var numericValuesSaved = Math.Abs(Settings.BackgroundOpacity - 0.735) < 0.00001 && Math.Abs(Settings.CardWidth - 160.5) < 0.00001 && Math.Abs(Settings.CoverCornerRadius - 18.5) < 0.00001 && Math.Abs(Store.LoadSettings().SelectionBorderThickness - 3.5) < 0.00001;
        if (!numericValuesSaved) throw new InvalidOperationException("Smoke test: manually entered appearance values were not saved.");
        var selectionColorSaved = !Settings.UseWindowsAccentColor && Store.LoadSettings().SelectionColor == "#4080F0" &&
            overlay?.FindResource(System.Windows.SystemColors.AccentColorBrushKey) is System.Windows.Media.SolidColorBrush selectionBrush && selectionBrush.Color.ToString() == "#FF4080F0";
        if (!selectionColorSaved) throw new InvalidOperationException("Smoke test: custom selection color was not saved or applied.");
        var settingsStayedOpen = ReferenceEquals(editor, settingsWindow) && settingsWindow?.IsVisible == true;
        if (!settingsStayedOpen) throw new InvalidOperationException("Smoke test: saving closed Settings.");
        if (settingsWindow is not null)
        {
            settingsWindow.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)settingsWindow.ActualWidth, (int)settingsWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(settingsWindow);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(Store.Root, "settings.png")); encoder.Save(file);
        }
        settingsWindow?.Close();
        HideLibrary();
        await Task.Delay(400);
        File.WriteAllText(Path.Combine(Store.Root, "smoke-result.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            games = testedGameCount, realizedCards = cardCount, overlayClosed = overlay is null, secondInstanceExited,
            withinWorkArea, firstGameSelected = firstHasFocus, navigationMoved, controllerPausedForSettings, settingsStayedOpen, numericValuesSaved,
            controllerScrollbarsHidden, scrollbarHoverOnly, selectionColorSaved,
            workingSetBytes = Process.GetCurrentProcess().WorkingSet64, status = Status
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Shutdown();
    }
    private async Task IdleDiagnosticsAsync()
    {
        await Task.Delay(3000);
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(5000);
        process.Refresh();
        File.WriteAllText(Path.Combine(Store.Root, "idle-result.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            games = Games.Count, overlayCreated = overlay is not null,
            workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64,
            cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds, sampleMilliseconds = elapsed.Elapsed.TotalMilliseconds
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        shuttingDown = true;
        lifetime.Cancel();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        settingsWindow?.Close(); overlay?.Close(); watcher?.Dispose(); processMonitor?.Dispose();
        hotkey?.Dispose(); tray?.Dispose(); instance?.Dispose();
        base.OnExit(e);
    }
}
