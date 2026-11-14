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

    /// <summary>Merge commits (several parents) are drawn as squares, like in the original.</summary>
    public static readonly StyledProperty<bool> IsMergeProperty =
        AvaloniaProperty.Register<CommitGraphCell, bool>(nameof(IsMerge));

    /// <summary>The commit the current branch points at gets a ring around its node.</summary>
    public static readonly StyledProperty<bool> IsHeadProperty =
        AvaloniaProperty.Register<CommitGraphCell, bool>(nameof(IsHead));

    public const int MaxVisibleLanes = 12;
    public const double LaneWidth = 14;
    private const double NodeRadius = 4.5;
    private const double LineThickness = 1.75;

    // Mid-tone colors that stay readable on light and dark backgrounds.
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x5B, 0x9B, 0xF0),
        Color.FromRgb(0x9B, 0x4D, 0xC8),
        Color.FromRgb(0xE0, 0x44, 0x44),
        Color.FromRgb(0x9A, 0xA0, 0xA8),
        Color.FromRgb(0x2E, 0xA0, 0x6B),
        Color.FromRgb(0xE0, 0x8A, 0x2E),
        Color.FromRgb(0x14, 0xA3, 0xB8),
        Color.FromRgb(0xC2, 0x4D, 0x8F),
    ];

    private static readonly IBrush[] Brushes = Palette.Select(c => (IBrush)new ImmutableSolidColorBrush(c)).ToArray();
    private static readonly IPen[] Pens = Palette.Select(c => (IPen)new ImmutablePen(new ImmutableSolidColorBrush(c), LineThickness, lineCap: PenLineCap.Round)).ToArray();

    static CommitGraphCell()
    {
        AffectsRender<CommitGraphCell>(RowProperty, IsMergeProperty, IsHeadProperty);
        AffectsMeasure<CommitGraphCell>(LaneCountProperty);
    }

    public CommitGraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public bool IsMerge
    {
        get => GetValue(IsMergeProperty);
        set => SetValue(IsMergeProperty, value);
    }

    public bool IsHead
    {
        get => GetValue(IsHeadProperty);
        set => SetValue(IsHeadProperty, value);
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
                    Curve(context, pen, new Point(x, 0), new Point(nodeX, middle));
                    break;
                case GraphSegmentKind.Out:
                    Curve(context, pen, new Point(nodeX, middle), new Point(x, height));
                    break;
            }
        }

        IBrush fill = Brushes[row.Color % Brushes.Length];
        Point center = new(nodeX, middle);
        if (IsMerge)
        {
            context.DrawRectangle(fill, null, new Rect(nodeX - NodeRadius, middle - NodeRadius, NodeRadius * 2, NodeRadius * 2));
        }
        else
        {
            context.DrawEllipse(fill, null, center, NodeRadius, NodeRadius);
        }

        if (IsHead)
        {
            context.DrawEllipse(null, Pens[row.Color % Pens.Length], center, NodeRadius + 2.5, NodeRadius + 2.5);
        }
    }

    // Lines that change lane bend smoothly (an S-curve between the two ends); straight ones stay straight.
    private static void Curve(DrawingContext context, IPen pen, Point from, Point to)
    {
        if (from.X == to.X)
        {
            context.DrawLine(pen, from, to);
            return;
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext g = geometry.Open())
        {
            double midY = (from.Y + to.Y) / 2;
            g.BeginFigure(from, false);
            g.CubicBezierTo(new Point(from.X, midY), new Point(to.X, midY), to);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static double X(int lane) => (Math.Min(lane, MaxVisibleLanes - 1) * LaneWidth) + (LaneWidth / 2) + 2;
}
