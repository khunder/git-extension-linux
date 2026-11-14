using Avalonia;

namespace GitExtensions.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the Avalonia visual designer and by the headless tests.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
