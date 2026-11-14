using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

/// <summary>Saves dark (and light, for comparison) screenshots of the main surfaces so the palette can be reviewed.</summary>
[TestFixture]
public class DarkThemeRenderTests
{
    private static void Save(Window window, string name)
    {
        ShellTestSupport.Settle();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name);
        window.CaptureRenderedFrame()!.Save(file);
        TestContext.AddTestAttachment(file);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(20);
            ShellTestSupport.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task Main_window_menus_and_overlays(string theme)
    {
        ThemeVariant variant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        repository.AddRichHistory();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "changed\nsecond line\n");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1280, Height = 760, RequestedThemeVariant = variant };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        viewModel.SelectedCommit = viewModel.Commits[1];
        await WaitAsync(() => viewModel.CommitDetails.HasCommit);
        ShellTestSupport.Settle();
        Save(window, $"dark-review-main-{theme}.png");

        viewModel.RevisionTabs.SelectedTabIndex = 1;
        ShellTestSupport.Settle();
        await Task.Delay(300);
        Save(window, $"dark-review-diff-{theme}.png");
        viewModel.RevisionTabs.SelectedTabIndex = 2;
        ShellTestSupport.Settle();
        await Task.Delay(300);
        Save(window, $"dark-review-filetree-{theme}.png");
        viewModel.RevisionTabs.SelectedTabIndex = 0;
        ShellTestSupport.Settle();

        MenuItem start = window.Find<MenuItem>("StartMenu");
        start.IsSubMenuOpen = true;
        Save(window, $"dark-review-menu-{theme}.png");
        start.IsSubMenuOpen = false;

        viewModel.OpenSettingsCommand.Execute(null);
        Save(window, $"dark-review-settings-{theme}.png");
        window.Close();
    }

    [AvaloniaTest]
    public void Dark_text_meets_WCAG_AA_on_every_layer()
    {
        string[] layers = ["SystemControlBackgroundAltHighBrush", "SidebarBackground", "DialogBackground", "MenuBarBackground", "TextControlBackground", "ButtonBackground", "MenuFlyoutPresenterBackground"];
        string[] texts = ["SystemControlForegroundBaseHighBrush", "SystemControlForegroundBaseMediumHighBrush", "AccentTextForeground", "SystemControlHyperlinkTextBrush", "ErrorTextForeground", "SuccessTextForeground"];
        foreach (string layer in layers)
        {
            foreach (string text in texts)
            {
                Contrast(Resolve(layer), Resolve(text)).Should().BeGreaterThan(4.5, $"{text} on {layer}");
            }
        }

        // Row selection and menu hover keep white text readable.
        foreach (string selected in new[] { "ListBoxItemBackgroundSelected", "MenuFlyoutItemBackgroundPointerOver" })
        {
            Contrast(Resolve(selected), Colors.White).Should().BeGreaterThan(4.5, selected);
        }

        static Color Resolve(string key)
        {
            Application.Current!.TryGetResource(key, ThemeVariant.Dark, out object? value).Should().BeTrue(key);
            return ((ISolidColorBrush)value!).Color;
        }

        static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        static double Luminance(Color c)
        {
            static double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        }
    }
}
