using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameLibrary.App;

// Keep the active cover in the left-hand slot, including at the end of a row.
public sealed class CarouselListBox : ListBox
{
    public CarouselListBox()
    {
        SizeChanged += (_, _) => { UpdateTail(); if (SelectedIndex >= 0) AlignSelection(); };
        RequestBringIntoView += (_, e) => e.Handled = true;
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ListBoxItem container && item is GameCard card)
            container.Margin = new Thickness(0, 0, ReferenceEquals(item, Items[Items.Count - 1]) ? TailWidth(card) : 14, 0);
    }

    private double TailWidth(GameCard card) => Math.Max(14, (Viewer?.ViewportWidth ?? ActualWidth) - card.Width - 24);
    private void UpdateTail()
    {
        if (Items.Count > 0 && Items[Items.Count - 1] is GameCard card &&
            ItemContainerGenerator.ContainerFromIndex(Items.Count - 1) is ListBoxItem container)
            container.Margin = new Thickness(0, 0, TailWidth(card), 0);
    }

    public void AlignSelection()
    {
        if (SelectedItem is not GameCard card) return;
        ScrollIntoView(card);
        UpdateLayout();
        UpdateTail();
        UpdateLayout();
        // Virtualization estimates the widths of unrealized cards. Align from the
        // realized container rather than multiplying an index by an estimated width.
        if (Viewer is { } viewer && ItemContainerGenerator.ContainerFromIndex(SelectedIndex) is ListBoxItem item)
            viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + item.TranslatePoint(new Point(), viewer).X);
        UpdateLayout();
    }
    internal string Diagnostics => $"offset={Viewer?.HorizontalOffset}, extent={Viewer?.ExtentWidth}, viewport={Viewer?.ViewportWidth}, margin={(ItemContainerGenerator.ContainerFromIndex(Items.Count - 1) as ListBoxItem)?.Margin}";

    private ScrollViewer? Viewer => FindViewer(this);
    private static ScrollViewer? FindViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindViewer(child) is { } nested) return nested;
        }
        return null;
    }
}
