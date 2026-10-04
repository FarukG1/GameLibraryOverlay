using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLibrary.Core;

namespace GameLibrary.App;

public sealed class InputHints : WrapPanel
{
    public InputHints() { HorizontalAlignment = HorizontalAlignment.Right; }

    public void Configure(Settings settings)
    {
        Children.Clear();
        foreach (var (button, label) in new[] { ("a", "Play"), ("b", "Hide"), ("x", "Favorite"), ("y", "Settings") })
        {
            var group = Group($"Xbox {button.ToUpperInvariant()}: {label}");
            group.Children.Add(Icon($"xbox_button_color_{button}.png", button.ToUpperInvariant()));
            group.Children.Add(Label(label));
        }
        var search = Group("Ctrl + F: Search");
        AddKey(search, "ctrl", "Ctrl"); AddKey(search, "f", "F", true);
        search.Children.Add(Label("Search"));
        var toggle = Group(settings.HotkeyKey == 0 ? "Esc: Hide library. Global shortcut disabled." : HotkeyService.Format(settings.HotkeyModifiers, settings.HotkeyKey) + ": Show or hide library");
        if (settings.HotkeyKey == 0) AddKey(toggle, "escape", "Esc");
        else
        {
            foreach (var (mask, icon, name) in new[] { (2u, "ctrl", "Ctrl"), (1u, "alt", "Alt"), (4u, "shift_icon", "Shift"), (8u, "win", "Win") })
                if ((settings.HotkeyModifiers & mask) != 0) AddKey(toggle, icon, name, toggle.Children.Count > 0);
            var key = KeyInterop.KeyFromVirtualKey((int)settings.HotkeyKey);
            var keyName = key.ToString();
            var iconName = key >= Key.D0 && key <= Key.D9 ? keyName[1..] : key == Key.Escape ? "escape" : keyName.ToLowerInvariant();
            AddKey(toggle, iconName, keyName, toggle.Children.Count > 0);
        }
        toggle.Children.Add(Label(settings.HotkeyKey == 0 ? "Hide library" : "Show / hide library"));
    }

    private StackPanel Group(string description)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), ToolTip = description };
        AutomationProperties.SetName(group, description);
        Children.Add(group);
        return group;
    }
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), TextWrapping = TextWrapping.NoWrap };
    private static void AddKey(Panel group, string icon, string label, bool plus = false)
    {
        if (plus) group.Children.Add(Label("+"));
        group.Children.Add(Icon($"keyboard_{icon}_outline.png", label));
    }
    private static FrameworkElement Icon(string file, string label)
    {
        var uri = new Uri($"pack://application:,,,/Assets/Input/{file}");
        try
        {
            var bitmap = new BitmapImage(uri);
            bitmap.Freeze();
            var artwork = TrimPadding(bitmap);
            var image = new Image { Source = artwork, Width = 24.0 * artwork.PixelWidth / artwork.PixelHeight, Height = 24,
                Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.Uniform, ToolTip = label };
            AutomationProperties.SetName(image, label);
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            // Uncommon configurable keys still have a readable native keycap.
            return new Border { Child = Label(label), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Height = 24, Padding = new Thickness(4, 1, 5, 1), Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
        }
    }

    private static BitmapSource TrimPadding(BitmapSource source)
    {
        // The upstream PNGs share a square canvas, but wide modifiers have shorter
        // artwork inside it. Size the visible keycap, not its transparent canvas.
        var pixelsSource = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        pixelsSource.CopyPixels(pixels, stride, 0);
        var left = source.PixelWidth; var top = source.PixelHeight; var right = -1; var bottom = -1;
        for (var y = 0; y < source.PixelHeight; y++)
            for (var x = 0; x < source.PixelWidth; x++)
                if (pixels[y * stride + x * 4 + 3] != 0)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        if (right < left) return source;
        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        cropped.Freeze();
        return cropped;
    }
}
