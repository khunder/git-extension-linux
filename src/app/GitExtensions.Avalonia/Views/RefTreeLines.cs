using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using Avalonia.VisualTree;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The indent of a row of the branches tree, painted like the original's tree view: one column per level, the dotted lines
///  that link a node to its siblings and to its parent, and the small expander arrow of the node (clicking it expands or
///  collapses the node). Part of the row template, so every position is fixed: a column is <see cref="Indent"/> wide and a
///  child's column sits under its parent's icon.
/// </summary>
public sealed class RefTreeLines : Control, ICustomHitTest
{
    /// <summary>The width of one level; the icon of a node starts at the column of its children.</summary>
    public const double Indent = 18;

    private const double ArrowGap = 5;

    private static readonly IPen LightArrow = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), 1.2);
    private static readonly IPen DarkArrow = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), 1.2);

    private TreeViewItem? _item;

    public RefTreeLines()
    {
        Cursor = new Cursor(StandardCursorType.Arrow);
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
    }

    private int Level => _item?.Level ?? 0;

    private bool HasChildren => _item is { ItemCount: > 0 };

    /// <summary>The whole indent takes clicks (only the arrow column reacts), so the row is not selected through the arrow.</summary>
    public bool HitTest(Point point) => true;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _item = TemplatedParent as TreeViewItem ?? this.FindAncestorOfType<TreeViewItem>();
        if (_item is not null)
        {
            _item.PropertyChanged += OnItemPropertyChanged;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_item is not null)
        {
            _item.PropertyChanged -= OnItemPropertyChanged;
            _item = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize) => new((Level + 1) * Indent, 0);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_item is not null && HasChildren && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.GetPosition(this).X >= Level * Indent)
        {
            _item.IsExpanded = !_item.IsExpanded;
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_item is null)
        {
            return;
        }

        int level = Level;
        double height = Bounds.Height;
        double middle = Math.Floor(height / 2);
        DotPainter dots = new(context, this);

        // The chain of items from the top level down to this one, to know which ancestors have later siblings.
        List<TreeViewItem> chain = [.. _item.GetVisualAncestors().OfType<TreeViewItem>().Reverse(), _item];

        for (int j = 0; j < level && j < chain.Count - 1; j++)
        {
            if (!IsLast(chain[j]))
            {
                dots.Vertical(Column(j), 0, height);
            }
        }

        double column = Column(level);
        double gap = HasChildren ? ArrowGap : 0;
        if (level > 0 || !IsFirst(_item))
        {
            dots.Vertical(column, 0, middle - gap);
        }

        if (!IsLast(_item))
        {
            dots.Vertical(column, middle + gap, height);
        }

        dots.Horizontal(middle, column + gap, Bounds.Width - 1);

        if (HasChildren)
        {
            DrawArrow(context, column, middle + 0.5, _item.IsExpanded);
        }
    }

    private static double Column(int level) => (level * Indent) + (Indent / 2);

    private static IEnumerable<object> SiblingsOf(TreeViewItem item)
    {
        ItemsControl? owner = item.FindAncestorOfType<TreeViewItem>() as ItemsControl ?? item.FindAncestorOfType<TreeView>();
        return owner?.ItemsSource?.Cast<object>() ?? [];
    }

    private static bool IsLast(TreeViewItem item) => SiblingsOf(item).LastOrDefault() is not { } last || ReferenceEquals(last, item.DataContext);

    private static bool IsFirst(TreeViewItem item) => SiblingsOf(item).FirstOrDefault() is not { } first || ReferenceEquals(first, item.DataContext);

    // A small open chevron like the original: pointing right when collapsed, down when expanded.
    private void DrawArrow(DrawingContext context, double x, double y, bool expanded)
    {
        IPen pen = GitExtensions.Desktop.Services.ThemePalettes.IsDark(ActualThemeVariant) ? DarkArrow : LightArrow;
        StreamGeometry geometry = new();
        using (StreamGeometryContext g = geometry.Open())
        {
            if (expanded)
            {
                g.BeginFigure(new Point(x - 3.5, y - 1.75), false);
                g.LineTo(new Point(x, y + 1.75));
                g.LineTo(new Point(x + 3.5, y - 1.75));
            }
            else
            {
                g.BeginFigure(new Point(x - 1.75, y - 3.5), false);
                g.LineTo(new Point(x + 1.75, y));
                g.LineTo(new Point(x - 1.75, y + 3.5));
            }

            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void OnItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TreeViewItem.IsExpandedProperty || e.Property == ItemsControl.ItemCountProperty)
        {
            InvalidateVisual();
        }
        else if (e.Property == TreeViewItem.LevelProperty)
        {
            InvalidateMeasure();
        }
    }
}

/// <summary>
///  Draws dotted lines the way the classic tree view does: one screen pixel on, one off, on the pixel grid of the screen
///  (not of the layout units, which are fractional at 125% or 150% scaling). Positions are snapped in window coordinates,
///  so a line that runs through many rows lands on the same pixel column in every row and its dots stay in step.
/// </summary>
internal readonly struct DotPainter
{
    private static readonly IBrush LightDotsBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
    private static readonly IBrush DarkDotsBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));

    private readonly DrawingContext _context;
    private readonly IBrush _brush;
    private readonly double _scale;
    private readonly double _originX;
    private readonly double _originY;

    public DotPainter(DrawingContext context, Visual owner)
    {
        _context = context;
        TopLevel? top = TopLevel.GetTopLevel(owner);
        _scale = top?.RenderScaling ?? 1;
        Point origin = top is not null && owner.TranslatePoint(default, top) is { } point ? point : default;
        _originX = origin.X;
        _originY = origin.Y;
        _brush = GitExtensions.Desktop.Services.ThemePalettes.IsDark(owner.ActualThemeVariant) ? DarkDotsBrush : LightDotsBrush;
    }

    /// <summary>A vertical dotted line at <paramref name="x"/> from <paramref name="top"/> to <paramref name="bottom"/> (local layout units).</summary>
    public void Vertical(double x, double top, double bottom)
    {
        double pixel = 1 / _scale;
        double localX = (Math.Floor((x + _originX) * _scale) / _scale) - _originX;
        long first = (long)Math.Ceiling((top + _originY) * _scale);
        long last = (long)Math.Ceiling((bottom + _originY) * _scale);
        for (long row = first; row < last; row++)
        {
            if ((row & 1) == 0)
            {
                _context.FillRectangle(_brush, new Rect(localX, (row / _scale) - _originY, pixel, pixel));
            }
        }
    }

    /// <summary>A horizontal dotted line at <paramref name="y"/> from <paramref name="left"/> to <paramref name="right"/> (local layout units).</summary>
    public void Horizontal(double y, double left, double right)
    {
        double pixel = 1 / _scale;
        double localY = (Math.Floor((y + _originY) * _scale) / _scale) - _originY;
        long first = (long)Math.Ceiling((left + _originX) * _scale);
        long last = (long)Math.Floor((right + _originX) * _scale);
        for (long column = first; column <= last; column++)
        {
            if ((column & 1) == 0)
            {
                _context.FillRectangle(_brush, new Rect((column / _scale) - _originX, localY, pixel, pixel));
            }
        }
    }
}
