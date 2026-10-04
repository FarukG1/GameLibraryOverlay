using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameLibrary.Core;
using GameLibrary.Infrastructure;
using Forms = System.Windows.Forms;

namespace GameLibrary.App;

public sealed class SettingsWindow : Window
{
    private readonly App app;
    private readonly Settings draft;
    private readonly TextBox steam = new(), steamUser = new(), libraries = new(), categories = new();
    private readonly ComboBox monitor = new();
    private readonly TabControl tabs = new();
    private readonly CheckBox translucent = new() { Content = "Let the desktop show through the overlay" };
    private readonly CheckBox restore = new() { Content = "Reopen the library after a game exits, when the desktop has focus" };
    private readonly CheckBox startup = new() { Content = "Start in the system tray when I sign in" };
    private readonly Slider opacity = new() { Minimum = 0.25, Maximum = 1, SmallChange = 0.01, LargeChange = 0.1 };
    private readonly Slider cardSize = new() { Minimum = 110, Maximum = 240, SmallChange = 1, LargeChange = 10 };
    private readonly Slider cornerRadius = new() { Minimum = 0, Maximum = 40, SmallChange = 1, LargeChange = 5 };
    private NumericSlider opacityEditor = null!, sizeEditor = null!, radiusEditor = null!;
    private readonly TextBox hotkey = new() { IsReadOnly = true };
    private uint hotkeyModifiers, hotkeyKey;
    private readonly ListBox customList = new() { DisplayMemberPath = "Name", MinHeight = 200 };
    private readonly TextBox name = new(), target = new(), arguments = new(), cover = new(), launcher = new(), process = new(), gameCategories = new();
    private readonly CheckBox favorite = new() { Content = "Favorite" };
    private readonly HashSet<string> editedCustom = [];
    private readonly TextBlock message = new() { Foreground = Brushes.LightSalmon, Margin = new Thickness(0, 8, 0, 10) };
    private CustomGame? selected;
    private bool loading;

    public SettingsWindow(App app)
    {
        this.app = app;
        draft = app.Store.Clone(app.Settings);
        Title = "Game Library · Settings";
        Background = new SolidColorBrush(Color.FromRgb(16, 21, 29));
        Foreground = new SolidColorBrush(Color.FromRgb(237, 241, 247));
        Width = 960; Height = 760; MinWidth = 740; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(25) };
        Content = root;
        var title = new TextBlock { Text = "Make it your library.", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 20) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        var save = Button("Save settings", async () => await SaveAsync()); save.Background = new SolidColorBrush(Color.FromRgb(204, 245, 120)); save.Foreground = Brushes.Black;
        buttons.Children.Add(save); buttons.Children.Add(Button("Close", Close)); footer.Children.Add(message);
        root.Children.Add(tabs);
        Tab(tabs, "Library", LibraryPanel());
        Tab(tabs, "Appearance", AppearancePanel());
        Tab(tabs, "Custom games", CustomPanel());
        Tab(tabs, "Categories", CategoriesPanel());
        Tab(tabs, "Shortcuts", ShortcutsPanel());
        Tab(tabs, "About", AboutPanel());
    }
    private StackPanel LibraryPanel()
    {
        var panel = Panel();
        Help(panel, "Steam games and non-Steam games added to Steam are read locally. Leave the installation folder blank to detect it automatically. Steam writes shortcut changes to disk when it saves its library; if an added game is missing, exit Steam normally, reopen it, and rescan here.");
        steam.Text = draft.SteamDirectory ?? "";
        Field(panel, "Steam installation folder", Browse(steam, true));
        steamUser.Text = draft.SteamUserId ?? "";
        Field(panel, "Steam userdata account folder (optional; blank selects the most recent account)", steamUser);
        libraries.Text = string.Join(Environment.NewLine, draft.ExtraLibraries);
        libraries.AcceptsReturn = true; libraries.Height = 110; libraries.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Field(panel, "Additional library roots — one per line", libraries);
        Help(panel, "Games on disconnected drives remain cached as unavailable. Use Rescan Steam after reconnecting a drive.");
        startup.IsChecked = TrayService.StartsWithWindows; panel.Children.Add(startup);
        restore.IsChecked = draft.RestoreAfterExit; panel.Children.Add(restore);
        return panel;
    }
    private StackPanel AppearancePanel()
    {
        var panel = Panel();
        monitor.Items.Add(new ComboBoxItem { Content = "Primary monitor (automatic)", Tag = "" });
        foreach (var screen in Forms.Screen.AllScreens)
            monitor.Items.Add(new ComboBoxItem { Content = $"{screen.DeviceName} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · Primary" : ""), Tag = screen.DeviceName });
        monitor.SelectedIndex = 0;
        foreach (ComboBoxItem item in monitor.Items) if ((string)item.Tag == draft.MonitorDevice) monitor.SelectedItem = item;
        Field(panel, "Gaming monitor", monitor);
        translucent.IsChecked = draft.Translucent; panel.Children.Add(translucent);
        opacity.Value = draft.BackgroundOpacity; opacityEditor = new NumericSlider(opacity, 100, "%"); Field(panel, "Background opacity", opacityEditor);
        cardSize.Value = draft.CardWidth; sizeEditor = new NumericSlider(cardSize, 1, "px"); Field(panel, "Cover size", sizeEditor);
        cornerRadius.Value = draft.CoverCornerRadius; radiusEditor = new NumericSlider(cornerRadius, 1, "px"); Field(panel, "Cover corner radius (0 = square)", radiusEditor);
        Help(panel, "Sliders move smoothly. You can also type exact values. The library leaves the taskbar available. If Wallpaper Engine still pauses, add an application rule for GameLibrary.exe under its Settings → Performance → Application rules. Use an app-focused condition with Keep running so the rule does not override pausing during games while this tray app is running.");
        return panel;
    }
    private StackPanel CategoriesPanel()
    {
        var panel = Panel();
        Help(panel, "One category per line. Their order here controls their order in the library. Right-click a game cover to assign categories.");
        categories.Text = string.Join(Environment.NewLine, draft.Categories); categories.AcceptsReturn = true; categories.Height = 280;
        categories.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Field(panel, "Custom categories", categories);
        return panel;
    }
    private StackPanel ShortcutsPanel()
    {
        var panel = Panel();
        hotkeyModifiers = draft.HotkeyModifiers; hotkeyKey = draft.HotkeyKey; UpdateHotkeyLabel();
        hotkey.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Back) { hotkeyKey = 0; hotkeyModifiers = 0; UpdateHotkeyLabel(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var modifiers = Keyboard.Modifiers;
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) { message.Text = "Include Ctrl, Alt, or Windows in the global shortcut."; return; }
            hotkeyModifiers = (uint)(((modifiers & ModifierKeys.Alt) != 0 ? 1 : 0) | ((modifiers & ModifierKeys.Control) != 0 ? 2 : 0) | ((modifiers & ModifierKeys.Shift) != 0 ? 4 : 0) | ((modifiers & ModifierKeys.Windows) != 0 ? 8 : 0));
            hotkeyKey = (uint)KeyInterop.VirtualKeyFromKey(key); message.Text = ""; UpdateHotkeyLabel();
        };
        Field(panel, "Click here and press a shortcut to toggle the library", hotkey);
        Help(panel, "Backspace disables the global shortcut. Inside the library: Ctrl+F searches, Esc closes, arrows navigate a carousel, and Enter launches the selected game. Shift+mouse wheel scrolls a carousel horizontally.");
        return panel;
    }
    private void UpdateHotkeyLabel()
    {
        hotkey.Text = HotkeyService.Format(hotkeyModifiers, hotkeyKey);
    }
    private Grid CustomPanel()
    {
        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        grid.Children.Add(left);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        actions.Children.Add(Button("Add", () => { var game = new CustomGame { Name = "New game" }; draft.CustomGames.Add(game); RefreshCustomList(); customList.SelectedItem = game; }));
        actions.Children.Add(Button("Remove", () => { if (selected is null) return; draft.CustomGames.Remove(selected); editedCustom.Remove(selected.Id); selected = null; RefreshCustomList(); LoadCustom(); }));
        DockPanel.SetDock(actions, Dock.Top); left.Children.Add(actions); left.Children.Add(customList);
        customList.SelectionChanged += (_, _) => LoadCustom();
        RefreshCustomList();
        var editor = Panel();
        var scroll = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); grid.Children.Add(scroll);
        Field(editor, "Name", name); Field(editor, "Executable or launch URI", Browse(target)); Field(editor, "Arguments (executable launches)", arguments);
        Field(editor, "Cover image", Browse(cover, false, true));
        Field(editor, "Optional launcher executable — started first", Browse(launcher));
        Field(editor, "Game process executable — optional detection override", Browse(process));
        Field(editor, "Categories — comma-separated names", gameCategories); editor.Children.Add(favorite);
        editor.Children.Add(Button("Keep game changes", () => { try { SaveCustom(); message.Text = "Game changes kept. Save settings to apply."; } catch (Exception ex) { message.Text = ex.Message; } }));
        Help(editor, "For Epic, Battle.net, or similar launchers, use their launch URI when possible. If the launcher starts a different executable, select that actual game executable as the process override. URI-only games can launch without process tracking.");
        return grid;
    }
    private void RefreshCustomList() { customList.ItemsSource = null; customList.ItemsSource = draft.CustomGames; }
    private void LoadCustom()
    {
        if (loading) return;
        if (selected is not null && draft.CustomGames.Contains(selected)) CaptureCustom();
        selected = customList.SelectedItem as CustomGame;
        name.Text = selected?.Name ?? ""; target.Text = selected?.Target ?? ""; arguments.Text = selected?.Arguments ?? "";
        cover.Text = selected?.CoverPath ?? ""; launcher.Text = selected?.LauncherExecutable ?? ""; process.Text = selected?.ProcessExecutable ?? "";
        var preferences = selected is null ? null : draft.Games.GetValueOrDefault(selected.Id);
        favorite.IsChecked = preferences?.Favorite == true; gameCategories.Text = string.Join(", ", preferences?.Categories ?? []);
    }
    private void CaptureCustom()
    {
        if (selected is null) return;
        selected.Name = name.Text.Trim(); selected.Target = target.Text.Trim(); selected.Arguments = arguments.Text;
        selected.CoverPath = NullIfEmpty(cover.Text); selected.LauncherExecutable = NullIfEmpty(launcher.Text); selected.ProcessExecutable = NullIfEmpty(process.Text);
        draft.For(selected.Id).Favorite = favorite.IsChecked == true;
        draft.For(selected.Id).Categories = gameCategories.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
        editedCustom.Add(selected.Id);
    }
    private void SaveCustom()
    {
        if (selected is null) return;
        var updated = new CustomGame { Id = selected.Id, Name = name.Text.Trim(), Target = target.Text.Trim(), Arguments = arguments.Text,
            CoverPath = NullIfEmpty(cover.Text), LauncherExecutable = NullIfEmpty(launcher.Text), ProcessExecutable = NullIfEmpty(process.Text) };
        if (CustomGameProvider.Validate(updated) is { } error) throw new InvalidOperationException(error);
        var assigned = gameCategories.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
        var defined = Lines(categories.Text);
        if (assigned.Any(c => !defined.Contains(c))) throw new InvalidOperationException("Add the category in the Categories tab first.");
        selected.Name = updated.Name; selected.Target = updated.Target; selected.Arguments = updated.Arguments; selected.CoverPath = updated.CoverPath;
        selected.LauncherExecutable = updated.LauncherExecutable; selected.ProcessExecutable = updated.ProcessExecutable;
        draft.For(selected.Id).Favorite = favorite.IsChecked == true; draft.For(selected.Id).Categories = assigned; editedCustom.Add(selected.Id);
        loading = true;
        var keep = selected; RefreshCustomList(); customList.SelectedItem = keep; loading = false;
    }
    private StackPanel AboutPanel()
    {
        var panel = Panel();
        Help(panel, "Game Library · .NET 10 / WPF\n\nYour library stays on this PC. Steam metadata is read locally; no Steam login, Web API key, or Millennium integration is needed. No telemetry or artwork downloads.");
        Help(panel, "Recent activity and playtime use local Steam metadata when available, plus sessions tracked by this app. Steam totals can be incomplete or delayed. Launcher and anti-cheat handoffs can require a process override.");
        Field(panel, "Settings, artwork, logs, and rebuildable cache", new TextBox { Text = app.Store.Root, IsReadOnly = true });
        Help(panel, "Start with Windows follows this executable’s location. If you move the app, turn that option off and on again.\n\nClosing the overlay leaves the tray app running. Use Exit in the tray menu to quit.");
        return panel;
    }
    internal async Task SaveAsync()
    {
        try
        {
            opacityEditor.Validate(); sizeEditor.Validate(); radiusEditor.Validate();
            SaveCustom();
            draft.SteamDirectory = NullIfEmpty(steam.Text); draft.SteamUserId = NullIfEmpty(steamUser.Text);
            if (draft.SteamDirectory is not null && !Directory.Exists(draft.SteamDirectory)) throw new InvalidOperationException("The Steam installation folder does not exist.");
            if (draft.SteamUserId is not null && !uint.TryParse(draft.SteamUserId, out _)) throw new InvalidOperationException("Steam userdata folder must be numeric.");
            draft.ExtraLibraries = Lines(libraries.Text);
            if (draft.ExtraLibraries.Any(p => !Path.IsPathFullyQualified(p))) throw new InvalidOperationException("Library roots must be absolute paths.");
            draft.Categories = Lines(categories.Text);
            draft.MonitorDevice = NullIfEmpty((string?)((ComboBoxItem?)monitor.SelectedItem)?.Tag);
            draft.Translucent = translucent.IsChecked == true; draft.BackgroundOpacity = opacity.Value; draft.CardWidth = cardSize.Value;
            draft.CoverCornerRadius = cornerRadius.Value;
            draft.RestoreAfterExit = restore.IsChecked == true; draft.HotkeyKey = hotkeyKey; draft.HotkeyModifiers = hotkeyModifiers;
            foreach (var game in draft.CustomGames)
            {
                if (CustomGameProvider.Validate(game) is { } error) throw new InvalidOperationException(game.Name + ": " + error);
                if (!string.IsNullOrWhiteSpace(game.CoverPath)) game.CoverPath = ImportCover(game.CoverPath);
            }
            IsEnabled = false;
            await app.ApplySettingsAsync(draft, startup.IsChecked == true, editedCustom);
            IsEnabled = true;
            editedCustom.Clear();
            message.Foreground = new SolidColorBrush(Color.FromRgb(204, 245, 120));
            message.Text = "Settings saved.";
            Activate();
        }
        catch (Exception ex) { message.Foreground = Brushes.LightSalmon; message.Text = ex.Message; IsEnabled = true; }
    }
    internal void ExerciseAppearanceInputs()
    {
        tabs.SelectedIndex = 1;
        opacityEditor.EnterNumber(73.5);
        sizeEditor.EnterNumber(160.5);
        radiusEditor.EnterNumber(18.5);
    }
    private string ImportCover(string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp") throw new InvalidOperationException("Use PNG, JPG, or BMP cover images.");
        if (new FileInfo(source).Length > 24 * 1024 * 1024) throw new InvalidOperationException("Cover images must be smaller than 24 MB.");
        using var stream = File.OpenRead(source);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        var directory = Path.Combine(app.Store.Root, "covers"); Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, hash + extension);
        if (!File.Exists(destination)) File.Copy(source, destination);
        return destination;
    }
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static List<string> Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static StackPanel Panel() => new() { Margin = new Thickness(18) };
    private static Button Button(string text, Action action) { var button = new Button { Content = text }; button.Click += (_, _) => action(); return button; }
    private static void Field(Panel panel, string label, UIElement control)
    { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 14, 0, 7), Foreground = new SolidColorBrush(Color.FromRgb(174, 189, 207)), FontSize = 12 }); panel.Children.Add(control); }
    private static void Help(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 14), Foreground = new SolidColorBrush(Color.FromRgb(155, 169, 186)), FontSize = 13, TextWrapping = TextWrapping.Wrap });
    private static void Tab(TabControl tabs, string title, UIElement content) => tabs.Items.Add(new TabItem { Header = title, Content = content is Grid ? content : new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
    private DockPanel Browse(TextBox box, bool folder = false, bool image = false)
    {
        var panel = new DockPanel();
        var browse = Button("…", () =>
        {
            if (folder)
            {
                var picker = new Microsoft.Win32.OpenFolderDialog(); if (picker.ShowDialog(this) == true) box.Text = picker.FolderName;
            }
            else
            {
                var picker = new Microsoft.Win32.OpenFileDialog { Filter = image ? "Cover images|*.png;*.jpg;*.jpeg;*.bmp" : "Executables|*.exe" };
                if (picker.ShowDialog(this) == true) box.Text = picker.FileName;
            }
        });
        browse.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); panel.Children.Add(browse); panel.Children.Add(box); return panel;
    }
}
