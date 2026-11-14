using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Loads the icons copied from the original WinForms application (<c>GitUI/Resources/Icons</c>, GPL-2.0 like the rest
///  of the repository) from the embedded <c>Assets/Icons</c> and adapts their lightness for the dark theme the way
///  the original's <c>AdaptLightness</c> does: lightness is mapped from [0,1] onto [text, window] colours, i.e. dark
///  strokes become light while hues are kept.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<(string Name, bool Dark), Bitmap?> Cache = [];

    /// <summary>The icons the original adapts to the theme explicitly (see <c>RepoObjectsTree</c> and <c>FormBrowse</c>).</summary>
    private static readonly HashSet<string> AlwaysAdapted = ["Branch", "EyeClosed", "EyeOpened", "RemoteEnableAndFetch", "RecoverLostObjects"];

    /// <summary>
    ///  Other icons are only adapted when they are mostly dark (they would vanish on a dark background);
    ///  mid-tone and light icons read well on both themes and keep their original look.
    /// </summary>
    private const double DarkIconLumaThreshold = 110;

    public static Bitmap? Get(string? name, bool dark)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        lock (Cache)
        {
            if (!Cache.TryGetValue((name, dark), out Bitmap? bitmap))
            {
                bitmap = Load(name, dark);
                Cache[(name, dark)] = bitmap;
            }

            return bitmap;
        }
    }

    private static Bitmap? Load(string name, bool dark)
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri($"avares://{typeof(IconCache).Assembly.GetName().Name}/Assets/Icons/{name}.png"));
            if (!dark)
            {
                return new Bitmap(stream);
            }

            WriteableBitmap decoded = WriteableBitmap.Decode(stream);
            return AlwaysAdapted.Contains(name) || MeanLuma(decoded) < DarkIconLumaThreshold ? Adapt(decoded) : decoded;
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The alpha-weighted mean of the channels, 0..255.</summary>
    public static double MeanLuma(Bitmap source)
    {
        using MemoryStream copy = new();
        source.Save(copy);
        copy.Position = 0;
        return MeanLuma(WriteableBitmap.Decode(copy));
    }

    private static double MeanLuma(WriteableBitmap bitmap)
    {
        using ILockedFramebuffer frame = bitmap.Lock();
        byte[] row = new byte[frame.RowBytes];
        double sum = 0;
        double weight = 0;
        bool premul = frame.Format == PixelFormats.Bgra8888 || frame.Format == PixelFormats.Rgba8888;
        for (int y = 0; y < frame.Size.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(frame.Address + (y * frame.RowBytes), row, 0, row.Length);
            for (int x = 0; x < frame.Size.Width; x++)
            {
                int i = x * 4;
                double a = row[i + 3];
                if (a == 0)
                {
                    continue;
                }

                // With premultiplied alpha the channels already carry the weight; otherwise apply it.
                double channels = (row[i] + row[i + 1] + row[i + 2]) / 3.0;
                sum += premul ? channels : channels * a / 255.0;
                weight += a / 255.0;
            }
        }

        return weight == 0 ? 255 : sum / weight;
    }

    private static WriteableBitmap Adapt(WriteableBitmap bitmap)
    {
        using ILockedFramebuffer frame = bitmap.Lock();

        // Both Bgra8888 and Rgba8888 are 4 bytes per pixel and the transformation treats the channels alike.
        // The alpha is premultiplied for the Skia default (Bgra8888 premul): un-premultiply around the change.
        bool premul = frame.Format == PixelFormats.Bgra8888 || frame.Format == PixelFormats.Rgba8888;
        int redAt = frame.Format == PixelFormats.Rgba8888 ? 0 : 2;
        byte[] row = new byte[frame.RowBytes];
        for (int y = 0; y < frame.Size.Height; y++)
        {
            IntPtr line = frame.Address + (y * frame.RowBytes);
            System.Runtime.InteropServices.Marshal.Copy(line, row, 0, row.Length);
            for (int x = 0; x < frame.Size.Width; x++)
            {
                int i = x * 4;
                byte a = row[i + 3];
                if (a == 0)
                {
                    continue;
                }

                double factor = premul ? 255.0 / a : 1.0;
                double r = Math.Min(1.0, row[i + redAt] * factor / 255.0);
                double g = Math.Min(1.0, row[i + 1] * factor / 255.0);
                double b = Math.Min(1.0, row[i + (2 - redAt)] * factor / 255.0);
                (r, g, b) = AdaptLightness(r, g, b);
                double back = premul ? a / 255.0 : 1.0;
                row[i + redAt] = (byte)Math.Round(r * back * 255);
                row[i + 1] = (byte)Math.Round(g * back * 255);
                row[i + (2 - redAt)] = (byte)Math.Round(b * back * 255);
            }

            System.Runtime.InteropServices.Marshal.Copy(row, 0, line, row.Length);
        }

        return bitmap;
    }

    /// <summary>Maps the HSL lightness l to 0.88 - 0.78 * l (near-black becomes light grey, white becomes dark), keeping hue and saturation.</summary>
    private static (double R, double G, double B) AdaptLightness(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        double d = max - min;
        double s = d == 0 ? 0 : d / (1 - Math.Abs((2 * l) - 1));
        double h = 0;
        if (d != 0)
        {
            h = max == r ? ((g - b) / d) % 6 : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
            h *= 60;
            if (h < 0)
            {
                h += 360;
            }
        }

        // Near black has no perceivable hue: fade the saturation out as the original does.
        if (l <= 0.1)
        {
            s *= l / 0.1;
        }

        double newL = 0.88 + (l * (0.12 - 0.88));
        double c = (1 - Math.Abs((2 * newL) - 1)) * s;
        double hp = h / 60;
        double xx = c * (1 - Math.Abs((hp % 2) - 1));
        (double r1, double g1, double b1) = hp switch
        {
            < 1 => (c, xx, 0.0),
            < 2 => (xx, c, 0.0),
            < 3 => (0.0, c, xx),
            < 4 => (0.0, xx, c),
            < 5 => (xx, 0.0, c),
            _ => (c, 0.0, xx),
        };
        double m = newL - (c / 2);
        return (r1 + m, g1 + m, b1 + m);
    }
}
