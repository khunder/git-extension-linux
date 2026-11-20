using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  The icon the operating system shows for a file type (Windows shell icons, like Explorer and the original application).
///  Other platforms have no such lookup here; callers fall back to the generic file icon.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SystemIcons
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeNormal = 0x80;

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The icon of files with <paramref name="extension"/> (".md"; empty for none), or null when it cannot be read.</summary>
    public static Bitmap? ForExtension(string extension)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(extension, out Bitmap? bitmap))
            {
                bitmap = Read(extension);
                Cache[extension] = bitmap;
            }

            return bitmap;
        }
    }

    private static Bitmap? Read(string extension)
    {
        SHFILEINFO info = default;

        // The file does not have to exist: the attributes flag makes the shell answer for the file type alone.
        IntPtr result = SHGetFileInfo("file" + extension, FileAttributeNormal, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), ShgfiIcon | ShgfiUseFileAttributes);
        if (result == IntPtr.Zero || info.iconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return ToBitmap(info.iconHandle);
        }
        finally
        {
            _ = DestroyIcon(info.iconHandle);
        }
    }

    private static WriteableBitmap? ToBitmap(IntPtr icon)
    {
        if (!GetIconInfo(icon, out ICONINFO iconInfo))
        {
            return null;
        }

        try
        {
            if (iconInfo.colorBitmap == IntPtr.Zero || GetObject(iconInfo.colorBitmap, Marshal.SizeOf<BITMAP>(), out BITMAP bitmap) == 0 || bitmap.widthPixels <= 0 || bitmap.heightPixels <= 0)
            {
                return null;
            }

            int width = bitmap.widthPixels;
            int height = bitmap.heightPixels;
            byte[] pixels = ReadPixels(iconInfo.colorBitmap, width, height);
            if (pixels.Length == 0)
            {
                return null;
            }

            // Icons without an alpha channel keep their transparency in the mask bitmap.
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            if (!hasAlpha)
            {
                byte[] mask = iconInfo.maskBitmap == IntPtr.Zero ? [] : ReadPixels(iconInfo.maskBitmap, width, height);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    bool transparent = mask.Length == pixels.Length && mask[i] != 0;
                    pixels[i + 3] = transparent ? (byte)0 : (byte)255;
                }
            }

            WriteableBitmap result = new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using ILockedFramebuffer frame = result.Lock();
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(pixels, y * width * 4, frame.Address + (y * frame.RowBytes), width * 4);
            }

            return result;
        }
        finally
        {
            if (iconInfo.colorBitmap != IntPtr.Zero)
            {
                _ = DeleteObject(iconInfo.colorBitmap);
            }

            if (iconInfo.maskBitmap != IntPtr.Zero)
            {
                _ = DeleteObject(iconInfo.maskBitmap);
            }
        }
    }

    private static byte[] ReadPixels(IntPtr bitmap, int width, int height)
    {
        BITMAPINFO info = new()
        {
            structSize = (uint)Marshal.SizeOf<BITMAPINFO>() - 4,
            imageWidth = width,
            imageHeight = -height,
            planeCount = 1,
            bitCount = 32,
        };
        byte[] pixels = new byte[width * height * 4];
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, 0) == 0 ? [] : pixels;
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref SHFILEINFO info, uint infoSize, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFO info, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr iconHandle;
        public int iconIndex;
        public uint attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string displayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string typeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool isIcon;
        public uint hotspotX;
        public uint hotspotY;
        public IntPtr maskBitmap;
        public IntPtr colorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int kind;
        public int widthPixels;
        public int heightPixels;
        public int widthBytes;
        public ushort planes;
        public ushort bitsPerPixel;
        public IntPtr bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public uint structSize;
        public int imageWidth;
        public int imageHeight;
        public ushort planeCount;
        public ushort bitCount;
        public uint compression;
        public uint imageSize;
        public int xPixelsPerMeter;
        public int yPixelsPerMeter;
        public uint colorsUsed;
        public uint colorsImportant;
        public uint colors;
    }
}
