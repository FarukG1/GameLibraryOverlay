using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using GameLibrary.Core;

namespace GameLibrary.App;

public sealed record GameCard(Game Game, GamePreferences? Preference, double Width, bool Wide = false, double CornerRadius = 12, double BorderThickness = 2)
{
    // The shared cover clip removes the outer half of the centered outline.
    public double SelectionStrokeThickness => BorderThickness * 2;
    public Rect CoverRect => new(0, 0, Width, CoverHeight);
    public double CoverHeight => Wide ? Width * 9 / 16 : Width * 1.4;
    public bool UsesHero => Wide && Game.WideCoverPath is null && Game.BackgroundPath is not null;
    public string? CoverPath => Wide ? UsesHero ? Game.BackgroundPath : Game.WideCoverPath ?? Preference?.CoverPath ?? Game.CoverPath : Preference?.CoverPath ?? Game.CoverPath;
    public string? LogoPath => UsesHero ? Game.LogoPath : null;
    public Stretch ImageStretch => Stretch.UniformToFill;
    public double LogoWidth => Width * 0.7;
    public string Initial => Game.Name.Length == 0 ? "G" : Game.Name[..1].ToUpperInvariant();
    public string Badge => Preference?.Favorite == true ? "★  FAVORITE" : Game.Source == GameSource.SteamShortcut ? "STEAM SHORTCUT" : Game.Source == GameSource.Steam ? "STEAM" : "CUSTOM";
    public string Detail => !Game.Installed ? "Unavailable / incomplete install" : GameStatistics.Minutes(Game, Preference) is { } minutes ? $"{minutes / 60:0.#} hours played" : "Ready to play";
    public string Tooltip => Game.Name + "\n" + Detail + "\nRight-click for favorites and categories";
}
public sealed record CarouselRow(string Name, List<GameCard> Cards, double Height)
{
    public string CountLabel => Cards.Count.ToString();
}

public partial class OverlayWindow : Window
{
    private readonly App app;
    private readonly DispatcherTimer searchDelay;
    private bool favoritesOnly;
    private bool userNavigated;
    private readonly GamepadService gamepad;
    private List<CarouselRow> rows = [];
    private int rowIndex, cardIndex;
    public int RealizedCards => Descendants(this).OfType<Button>().Count(b => b.DataContext is GameCard);
    internal bool GamepadRunning => gamepad.IsRunning;
    internal string? SelectedGameId => SelectedCard?.Game.Id;
    internal bool GameCardHasFocus => Keyboard.FocusedElement is ListBoxItem { DataContext: GameCard };
    internal bool FirstGameSelected => rowIndex == 0 && cardIndex == 0 && SelectedCard is not null;
    internal async Task VerifyCarouselAnchorsAsync()
    {
        var savedRow = rowIndex; var savedCard = cardIndex;
        for (var r = 0; r < rows.Count; r++)
        {
            double? anchor = null;
            foreach (var index in new[] { 0, rows[r].Cards.Count / 2, rows[r].Cards.Count - 1 }.Distinct())
            {
                rowIndex = r; cardIndex = index; FocusSelected();
                await Task.Delay(60);
                var list = Descendants(Rows).OfType<CarouselListBox>().First(l => ReferenceEquals(l.DataContext, rows[r]));
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
                var x = item.TranslatePoint(new Point(), list).X;
                var artwork = Descendants(item).OfType<Grid>().First(g => g.Name == "CoverArtwork");
                var coverBounds = artwork.TransformToAncestor(list).TransformBounds(new Rect(artwork.RenderSize));
                if (coverBounds.Left < 4 || coverBounds.Right > list.ActualWidth - 4)
                    throw new InvalidOperationException($"Selected cover is clipped: {coverBounds}, viewport={list.ActualWidth}.");
                if (anchor is { } expected && Math.Abs(x - expected) > 2)
                    throw new InvalidOperationException($"Carousel anchor moved: row={r}, index={index}, x={x}, expected={expected}, {list.Diagnostics}.");
                anchor ??= x;
            }
        }
        rowIndex = savedRow; cardIndex = savedCard; FocusSelected();
    }
    internal bool ScrollbarsMatchInput => Descendants(this).OfType<System.Windows.Controls.Primitives.ScrollBar>()
        .All(bar => bar.Opacity == (bar.IsMouseOver && !InputMode.Current.IsGamepad ? 1 : 0) &&
            (bar.Template.FindName("PART_Track", bar) is not FrameworkElement track ||
            (bar.Orientation == Orientation.Vertical ? track.ActualWidth : track.ActualHeight) <= 6));
    internal string ScrollbarDiagnostics => string.Join("; ", Descendants(this).OfType<System.Windows.Controls.Primitives.ScrollBar>()
        .Select(bar => $"{bar.Orientation}: {bar.ActualWidth}x{bar.ActualHeight}, opacity={bar.Opacity}, hover={bar.IsMouseOver}, gamepad={InputMode.Current.IsGamepad}"));
    private GameCard? SelectedCard => rows.Count > rowIndex && rows[rowIndex].Cards.Count > cardIndex ? rows[rowIndex].Cards[cardIndex] : null;
    public OverlayWindow(App app)
    {
        this.app = app;
        AllowsTransparency = app.Settings.Translucent;
        InitializeComponent();
        // The system resource follows Windows accent changes automatically. Scope
        // a manual override to this window when the user opts out.
        if (!app.Settings.UseWindowsAccentColor)
        {
            var selectionBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(app.Settings.SelectionColor));
            selectionBrush.Freeze();
            Resources[SystemColors.AccentColorBrushKey] = selectionBrush;
        }
        ShortcutHint.Configure(app.Settings);
        SizeChanged += (_, _) => ShortcutHint.MaxWidth = Math.Max(200, ActualWidth - 120);
        gamepad = new GamepadService(action => { InputMode.Current.UseGamepad(); HandleNavigation(action); });
        var alpha = app.Settings.Translucent ? (byte)(app.Settings.BackgroundOpacity * 255) : (byte)255;
        Background = new SolidColorBrush(Color.FromArgb(alpha, 12, 17, 24));
        searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); Refresh(); };
        SourceInitialized += (_, _) =>
        {
            NativeWindow.Place(this, app.Settings.MonitorDevice);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        };
        Activated += (_, _) => { gamepad.Start(); if (!Search.IsKeyboardFocusWithin) QueueFocus(); };
        Deactivated += (_, _) => gamepad.Stop();
        ContentRendered += (_, _) => QueueFocus();
        app.LibraryChanged += Refresh;
        Closed += (_, _) => { app.LibraryChanged -= Refresh; searchDelay.Stop(); gamepad.Dispose(); Rows.ItemsSource = null; };
        Refresh();
    }
    private void Refresh()
    {
        var term = Search.Text.Trim();
        var cards = app.Games.Where(g => string.IsNullOrEmpty(term) || g.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .Select(g => new GameCard(g, app.Settings.Games.GetValueOrDefault(g.Id), app.Settings.CardWidth, CornerRadius: app.Settings.CoverCornerRadius, BorderThickness: app.Settings.SelectionBorderThickness)).ToList();
        if (favoritesOnly) cards = cards.Where(c => c.Preference?.Favorite == true).ToList();
        var previousId = userNavigated ? SelectedCard?.Game.Id : null;
        var previousRow = userNavigated ? rows.ElementAtOrDefault(rowIndex)?.Name : null;
        rows = [];
        void Add(string name, IEnumerable<GameCard> values, bool wide = false)
        {
            var list = values.Select(c => wide ? c with { Width = app.Settings.CardWidth * 2, Wide = true } : c).ToList();
            if (list.Count > 0) rows.Add(new(name, list, list[0].CoverHeight + 117));
        }
        if (term.Length > 0) Add("Search results", cards);
        else if (favoritesOnly) Add("Favorites", cards);
        else
        {
            Add("Recently Played", cards.Where(c => GameStatistics.LastPlayed(c.Game, c.Preference) is not null).OrderByDescending(c => GameStatistics.LastPlayed(c.Game, c.Preference)).Take(24), wide: true);
            Add("Favorites", cards.Where(c => c.Preference?.Favorite == true));
            Add("Installed", cards.Where(c => c.Game.Installed));
            Add("Most Played", cards.Where(c => GameStatistics.Minutes(c.Game, c.Preference) > 0).OrderByDescending(c => GameStatistics.Minutes(c.Game, c.Preference)).Take(48));
            foreach (var category in app.Settings.Categories) Add(category, cards.Where(c => c.Preference?.Categories.Contains(category) == true));
            Add("Unavailable", cards.Where(c => !c.Game.Installed));
        }
        rowIndex = Math.Max(0, rows.FindIndex(r => r.Name == previousRow));
        cardIndex = rows.Count > 0 ? Math.Max(0, rows[rowIndex].Cards.FindIndex(c => c.Game.Id == previousId)) : 0;
        Rows.ItemsSource = rows;
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Summary.Text = $"{app.Games.Count} titles · Steam & your games · One place to play";
        StatusText.Text = app.Status;
        if (IsActive && !Search.IsKeyboardFocusWithin) QueueFocus();
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (searchDelay is null) return; userNavigated = false; searchDelay.Stop(); searchDelay.Start(); }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (SearchPanel.IsVisible) DismissSearch(); else Close(); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { ShowSearch(); e.Handled = true; }
        else if (e.Key == Key.Enter && Search.IsKeyboardFocusWithin) { searchDelay.Stop(); Refresh(); FocusSelected(); e.Handled = true; }
        else if (Keyboard.FocusedElement is not TextBox)
        {
            LibraryAction? action = e.Key switch { Key.Left => LibraryAction.Left, Key.Right => LibraryAction.Right,
                Key.Up => LibraryAction.Up, Key.Down => LibraryAction.Down, Key.Enter => LibraryAction.Launch, _ => null };
            if (action is { } value) { e.Handled = true; HandleNavigation(value); }
        }
    }
    private void SettingsClick(object sender, RoutedEventArgs e) => app.ShowSettings();
    internal void ShowSearch() { SearchPanel.Visibility = Visibility.Visible; Search.Focus(); Search.SelectAll(); }
    internal void DismissSearch()
    {
        Search.Clear(); searchDelay.Stop(); SearchPanel.Visibility = Visibility.Collapsed;
        Refresh(); FocusSelected();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => { if (IsActive) FocusSelected(); });
    }
    internal bool SearchIsOpen => SearchPanel.IsVisible;
    public void SetFavoritesOnly(bool value) { userNavigated = false; favoritesOnly = value; Refresh(); }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void AllClick(object sender, RoutedEventArgs e) => SetFavoritesOnly(false);
    private void FavoritesClick(object sender, RoutedEventArgs e) => SetFavoritesOnly(true);
    private async void GameClick(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is GameCard card) await app.LaunchAsync(card.Game); }
    private void GameContextMenu(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button { DataContext: GameCard card } button) return;
        var menu = new ContextMenu();
        var favorite = new MenuItem { Header = card.Preference?.Favorite == true ? "Remove favorite" : "Add to favorites" };
        favorite.Click += (_, _) => app.ToggleFavorite(card.Game);
        menu.Items.Add(favorite);
        foreach (var category in app.Settings.Categories)
        {
            var item = new MenuItem { Header = category, IsCheckable = true, IsChecked = card.Preference?.Categories.Contains(category) == true };
            item.Click += (_, _) => app.ToggleCategory(card.Game, category);
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
    }
    private void CarouselLoaded(object sender, RoutedEventArgs e)
    { if (rowIndex == 0 && cardIndex == 0 && IsActive && !Search.IsKeyboardFocusWithin) QueueFocus(); }
    private void QueueFocus() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (IsActive && !Search.IsKeyboardFocusWithin) FocusSelected(); });
    private void FocusSelected()
    {
        if (SelectedCard is not { } card) return;
        var row = rows[rowIndex];
        Rows.ScrollIntoView(row); Rows.UpdateLayout();
        foreach (var list in Descendants(Rows).OfType<ListBox>())
        {
            if (!ReferenceEquals(list.DataContext, row)) { list.SelectedIndex = -1; continue; }
            list.SelectedIndex = cardIndex;
            if (list is CarouselListBox carousel) carousel.AlignSelection();
            if (list.ItemContainerGenerator.ContainerFromIndex(cardIndex) is ListBoxItem item) Keyboard.Focus(item);
        }
    }
    internal async void HandleNavigation(LibraryAction action)
    {
        if (action == LibraryAction.Close) { if (SearchPanel.IsVisible) DismissSearch(); else Close(); return; }
        if (action == LibraryAction.Settings) { app.ShowSettings(); return; }
        if (SelectedCard is not { } selected) return;
        userNavigated = true;
        if (action == LibraryAction.Launch) { await app.LaunchAsync(selected.Game); return; }
        if (action == LibraryAction.Favorite) { app.ToggleFavorite(selected.Game); return; }
        if (action == LibraryAction.Up) rowIndex = Math.Max(0, rowIndex - 1);
        if (action == LibraryAction.Down) rowIndex = Math.Min(rows.Count - 1, rowIndex + 1);
        if (action == LibraryAction.Left) cardIndex--;
        if (action == LibraryAction.Right) cardIndex++;
        cardIndex = Math.Clamp(cardIndex, 0, rows[rowIndex].Cards.Count - 1);
        FocusSelected();
    }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    { if (msg == 0x001a) Dispatcher.BeginInvoke(() => NativeWindow.Place(this, app.Settings.MonitorDevice)); return IntPtr.Zero; }
    private void CarouselWheel(object sender, MouseWheelEventArgs e)
    {
        // Vertical wheel scrolls rows; Shift+wheel moves within the focused carousel.
        var viewer = Descendants((DependencyObject)sender).OfType<ScrollViewer>().FirstOrDefault();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        { if (e.Delta < 0) viewer?.LineRight(); else viewer?.LineLeft(); }
        else
        { var outer = Descendants(Rows).OfType<ScrollViewer>().FirstOrDefault(); if (e.Delta < 0) outer?.LineDown(); else outer?.LineUp(); }
        e.Handled = true;
    }
    private async void CarouselKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && sender is ListBox { SelectedItem: GameCard card }) { e.Handled = true; await app.LaunchAsync(card.Game); } }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
