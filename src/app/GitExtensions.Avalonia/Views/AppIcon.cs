using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  One of the original application's 16x16 icons (see <see cref="IconCache"/>), picked by file name without extension
///  and re-tinted automatically when the theme variant changes.
/// </summary>
public sealed class AppIcon : Control
{
    public static readonly StyledProperty<string?> IconNameProperty = AvaloniaProperty.Register<AppIcon, string?>(nameof(IconName));

    static AppIcon()
    {
        AffectsRender<AppIcon>(IconNameProperty);
        AffectsMeasure<AppIcon>(IconNameProperty);
    }

    public AppIcon()
    {
        Width = 16;
        Height = 16;
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        bool dark = Services.ThemePalettes.IsDark(ActualThemeVariant);
        var bitmap = IconCache.Get(IconName, dark);
        if (bitmap is null)
        {
            return;
        }

        context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(0, 0, Width, Height));
    }
}
