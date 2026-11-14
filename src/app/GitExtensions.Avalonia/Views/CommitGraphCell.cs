using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GitExtensions.Desktop.History;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  Draws the graph part of one history row: the lane lines that cross the row and the commit node.
///  It draws only its own <see cref="Row"/>, so it works inside a virtualized list.
/// </summary>
public sealed class CommitGraphCell : Control
{
    public static readonly StyledProperty<CommitGraphRow?> RowProperty =
        AvaloniaProperty.Register<CommitGraphCell, CommitGraphRow?>(nameof(Row));

    /// <summary>
    ///  Number of lanes the column reserves (same for every row so the subjects line up); capped by <see cref="MaxVisibleLanes"/>.
    /// </summary>
    public static readonly StyledProperty<int> LaneCountProperty =
        AvaloniaProperty.Register<CommitGraphCell, int>(nameof(LaneCount), 1);

    public const int MaxVisibleLanes = 12;
    public const double LaneWidth = 14;
    private const double NodeRadius = 4.5;
    private const double LineThickness = 1.75;

    // Mid-tone colors that stay readable on light and dark backgrounds.
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x3B, 0x8E, 0xEA),
        Color.FromRgb(0xE0, 0x7B, 0x39),
        Color.FromRgb(0x2E, 0xA0, 0x6B),
        Color.FromRgb(0xC2, 0x4D, 0xB3),
        Color.FromRgb(0xD1, 0xA3, 0x1F),
        Color.FromRgb(0x14, 0xA3, 0xB8),
        Color.FromRgb(0xE0, 0x4F, 0x5F),
        Color.FromRgb(0x8E, 0x6B, 0xE0),
    ];

    private static readonly IBrush[] Brushes = Palette.Select(c => (IBrush)new ImmutableSolidColorBrush(c)).ToArray();
    private static readonly IPen[] Pens = Palette.Select(c => (IPen)new ImmutablePen(new ImmutableSolidColorBrush(c), LineThickness, lineCap: PenLineCap.Round)).ToArray();

    static CommitGraphCell()
    {
        AffectsRender<CommitGraphCell>(RowProperty);
        AffectsMeasure<CommitGraphCell>(LaneCountProperty);
    }

    public CommitGraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public int LaneCount
    {
        get => GetValue(LaneCountProperty);
        set => SetValue(LaneCountProperty, value);
    }

    public static IBrush BrushFor(int color) => Brushes[color % Brushes.Length];

    public static double WidthFor(int laneCount) => (Math.Clamp(laneCount, 1, MaxVisibleLanes) * LaneWidth) + 4;

    protected override Size MeasureOverride(Size availableSize) => new(WidthFor(LaneCount), 0);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Row is not { } row)
        {
            return;
        }

        double height = Bounds.Height;
        double middle = height / 2;
        double nodeX = X(row.Lane);

        foreach (GraphSegment segment in row.Segments)
        {
            IPen pen = Pens[segment.Color % Pens.Length];
            double x = X(segment.Lane);
            switch (segment.Kind)
            {
                case GraphSegmentKind.Pass:
                    context.DrawLine(pen, new Point(x, 0), new Point(x, height));
                    break;
                case GraphSegmentKind.In:
                    context.DrawLine(pen, new Point(x, 0), new Point(nodeX, middle));
                    break;
                case GraphSegmentKind.Out:
                    context.DrawLine(pen, new Point(nodeX, middle), new Point(x, height));
                    break;
            }
        }

        context.DrawEllipse(Brushes[row.Color % Brushes.Length], null, new Point(nodeX, middle), NodeRadius, NodeRadius);
    }

    private static double X(int lane) => (Math.Min(lane, MaxVisibleLanes - 1) * LaneWidth) + (LaneWidth / 2) + 2;
}
