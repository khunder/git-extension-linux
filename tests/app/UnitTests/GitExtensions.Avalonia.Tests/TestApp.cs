using Avalonia;
using Avalonia.Headless;
using GitExtensions.Desktop;
using GitExtensionsDesktopTests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace GitExtensionsDesktopTests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
