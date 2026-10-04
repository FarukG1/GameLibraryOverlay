using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameLibrary.App;

internal sealed class NumericSlider : Grid
{
    private readonly Slider slider;
    private readonly TextBox number;
    private readonly double scale;
    private bool updating;
    public NumericSlider(Slider slider, double scale, string unit)
    {
        this.slider = slider; this.scale = scale;
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        slider.IsSnapToTickEnabled = false;
        slider.VerticalAlignment = VerticalAlignment.Center;
        slider.Margin = new Thickness(0, 0, 18, 0);
        Children.Add(slider);
        number = new TextBox { Text = Format(), ToolTip = $"{slider.Minimum * scale:0.#}–{slider.Maximum * scale:0.#} {unit}" };
        Grid.SetColumn(number, 1); Children.Add(number);
        var label = new TextBlock { Text = unit, Foreground = new SolidColorBrush(Color.FromRgb(155, 169, 186)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(label, 2); Children.Add(label);
        slider.ValueChanged += (_, _) => { if (!updating) { updating = true; number.Text = Format(); updating = false; } };
        number.TextChanged += (_, _) =>
        {
            if (updating) return;
            updating = true;
            if (TryValue(out var value)) { slider.Value = value; number.ClearValue(Control.BorderBrushProperty); }
            else number.BorderBrush = Brushes.LightSalmon;
            updating = false;
        };
        number.LostKeyboardFocus += (_, _) => { if (TryValue(out _)) number.Text = Format(); };
    }
    private string Format() => (slider.Value * scale).ToString("0.#", CultureInfo.CurrentCulture);
    private bool TryValue(out double value)
    {
        var valid = double.TryParse(number.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        value /= scale;
        return valid && double.IsFinite(value) && value >= slider.Minimum && value <= slider.Maximum;
    }
    public void Validate()
    { if (!TryValue(out var value)) throw new InvalidOperationException($"Enter a value between {slider.Minimum * scale:0.#} and {slider.Maximum * scale:0.#}."); slider.Value = value; }
    internal void EnterNumber(double value) { number.Text = value.ToString(CultureInfo.CurrentCulture); Validate(); }
}
