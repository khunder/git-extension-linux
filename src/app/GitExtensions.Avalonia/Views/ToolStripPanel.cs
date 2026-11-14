using Avalonia;
using Avalonia.Controls;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  A left-aligned horizontal strip like the original WinForms tool strip: the items sit next to each other and, when the
///  window is too narrow, only the item marked with the "shrink" class (the working directory) gives way.
/// </summary>
public sealed class ToolStripPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<ToolStripPanel, double>(nameof(Spacing), 1);

    static ToolStripPanel() => AffectsMeasure<ToolStripPanel>(SpacingProperty);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private static bool IsShrinkable(Control c) => c.Classes.Contains("shrink");

    protected override Size MeasureOverride(Size availableSize)
    {
        double fixedWidth = 0;
        double height = 0;
        int visible = 0;
        foreach (Control child in Children.Where(c => c.IsVisible))
        {
            visible++;
            if (IsShrinkable(child))
            {
                continue;
            }

            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            fixedWidth += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double spacing = Spacing * Math.Max(0, visible - 1);
        double remaining = Math.Max(0, availableSize.Width - fixedWidth - spacing);
        double width = fixedWidth + spacing;
        foreach (Control child in Children.Where(c => c.IsVisible && IsShrinkable(c)))
        {
            child.Measure(new Size(remaining, availableSize.Height));
            width += child.DesiredSize.Width;
            remaining = Math.Max(0, remaining - child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (Control child in Children.Where(c => c.IsVisible))
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }

        return finalSize;
    }
}
