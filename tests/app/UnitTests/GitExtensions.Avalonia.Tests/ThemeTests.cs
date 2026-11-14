using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class ThemeTests
{
    private static readonly string[] SemanticResources =
    [
        "ErrorBannerBackground", "ErrorBannerForeground", "ErrorTextForeground",
        "DiffAddedBackground", "DiffRemovedBackground", "DiffHunkBackground",
        "RefBadgeBackground", "RefBadgeForeground", "SubtleBorderBrush"
    ];

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static string TempFile(string name) => Path.Combine(IsolatedConfig.Directory!, "theme", name);

    private static void Render()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Screenshot(Window window, string name)
    {
        Render();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name);
        window.CaptureRenderedFrame()!.Save(file);
        TestContext.AddTestAttachment(file);
        TestContext.Out.WriteLine(file);
    }

    [Test]
    public void ThemeStore_round_trips_every_choice()
    {
        ThemeStore store = new(TempFile("roundtrip.json"));
        store.Load().Should().Be(ThemeChoice.System);
        foreach (ThemeChoice choice in Enum.GetValues<ThemeChoice>())
        {
            store.Save(choice);
            new ThemeStore(store.FilePath).Load().Should().Be(choice);
        }
    }

    [TestCase("{ nope")]
    [TestCase("{\"Theme\":\"Purple\"}")]
    [TestCase("{\"Theme\":42}")]
    [TestCase("null")]
    public void ThemeStore_ignores_a_corrupt_file(string content)
    {
        string file = TempFile("corrupt.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        new ThemeStore(file).Load().Should().Be(ThemeChoice.System);
    }

    [Test]
    public void ThemeStore_defaults_next_to_the_window_state_file()
        => Path.GetDirectoryName(new ThemeStore().FilePath).Should().Be(Path.GetDirectoryName(new WindowStateStore().FilePath));

    [AvaloniaTest]
    public void Applying_a_choice_changes_the_theme_variant_and_persists()
    {
        ThemeService previous = ThemeService.Current;
        try
        {
            ThemeStore store = new(TempFile("apply.json"));
            ThemeService service = new(store);
            ThemeService.Current = service;

            service.Set(ThemeChoice.Dark);
            Application.Current!.RequestedThemeVariant.Should().Be(ThemeVariant.Dark);
            Application.Current.ActualThemeVariant.Should().Be(ThemeVariant.Dark);
            store.Load().Should().Be(ThemeChoice.Dark);

            service.Set(ThemeChoice.Light);
            Application.Current.ActualThemeVariant.Should().Be(ThemeVariant.Light);

            service.Set(ThemeChoice.System);
            Application.Current.RequestedThemeVariant.Should().Be(ThemeVariant.Default);

            // A fresh service (next run) restores the stored choice.
            service.Set(ThemeChoice.Dark);
            ThemeService restored = new(store);
            restored.Choice.Should().Be(ThemeChoice.Dark);
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            restored.ApplyCurrent();
            Application.Current.ActualThemeVariant.Should().Be(ThemeVariant.Dark);
        }
        finally
        {
            ThemeService.Current = previous;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaTest]
    public void The_view_menu_checks_the_current_choice_and_applies_clicks()
    {
        ThemeService previous = ThemeService.Current;
        try
        {
            ThemeService.Current = new ThemeService(new ThemeStore(TempFile("menu.json")));
            ThemeMenuButton button = new();
            button.MenuItems[ThemeChoice.System].IsChecked.Should().BeTrue();

            ThemeService.Current.Set(ThemeChoice.Dark);
            button.MenuItems[ThemeChoice.Dark].IsChecked.Should().BeTrue();
            button.MenuItems[ThemeChoice.System].IsChecked.Should().BeFalse();
        }
        finally
        {
            ThemeService.Current = previous;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaTest]
    public void Semantic_resources_resolve_in_both_variants()
    {
        foreach (string key in SemanticResources)
        {
            Application.Current!.TryGetResource(key, ThemeVariant.Light, out object? light).Should().BeTrue(key);
            Application.Current.TryGetResource(key, ThemeVariant.Dark, out object? dark).Should().BeTrue(key);
            light.Should().BeOfType<SolidColorBrush>(key);
            dark.Should().BeOfType<SolidColorBrush>(key);
            ((SolidColorBrush)light!).Color.Should().NotBe(((SolidColorBrush)dark!).Color, $"{key} must differ between themes");
        }
    }

    [AvaloniaTest]
    public void Text_has_enough_contrast_on_the_semantic_backgrounds()
    {
        (string Background, string Foreground)[] pairs =
        [
            ("ErrorBannerBackground", "ErrorBannerForeground"),
            ("RefBadgeBackground", "RefBadgeForeground"),
        ];
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Color text = variant == ThemeVariant.Light ? Colors.Black : Colors.White;
            foreach ((string background, string foreground) in pairs)
            {
                Contrast(Resolve(background, variant), Resolve(foreground, variant)).Should().BeGreaterThan(7, $"{background}/{variant}");
            }

            foreach (string diff in new[] { "DiffAddedBackground", "DiffRemovedBackground", "DiffHunkBackground" })
            {
                Contrast(Resolve(diff, variant), text).Should().BeGreaterThan(7, $"{diff}/{variant}");
            }

            Contrast(Resolve("ErrorTextForeground", variant), variant == ThemeVariant.Light ? Colors.White : Color.Parse("#202020"))
                .Should().BeGreaterThan(4.5);
        }

        static Color Resolve(string key, ThemeVariant variant)
        {
            Application.Current!.TryGetResource(key, variant, out object? value);
            return ((SolidColorBrush)value!).Color;
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

    [AvaloniaTest]
    public async Task Diff_line_backgrounds_follow_the_theme_variant()
    {
        using TestRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "changed\nsecond line\n");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        ChangedFileViewModel file = viewModel.ChangedFiles.Single(f => f.Name == "a.txt");
        viewModel.SelectedChangedFile = file;
        await viewModel.Diff!.ShowAsync(file);
        Render();

        Color? added = FirstBackground(window, ThemeVariant.Light);
        Color? addedDark = FirstBackground(window, ThemeVariant.Dark);
        added.Should().NotBeNull();
        addedDark.Should().NotBeNull();
        added.Should().NotBe(addedDark);
        window.Close();

        static Color? FirstBackground(Window window, ThemeVariant variant)
        {
            window.RequestedThemeVariant = variant;
            Render();
            Application.Current!.TryGetResource("DiffAddedBackground", variant, out object? expected);
            Border? border = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("added"));
            border.Should().NotBeNull();
            ((SolidColorBrush)border!.Background!).Color.Should().Be(((SolidColorBrush)expected!).Color);
            return ((SolidColorBrush)border.Background!).Color;
        }
    }

    [AvaloniaTest]
    public async Task Screenshots_main_window_diff_in_both_variants()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "changed\nsecond line\n");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        ChangedFileViewModel file = viewModel.ChangedFiles.Single(f => f.Name == "a.txt");
        viewModel.SelectedChangedFile = file;
        await viewModel.Diff!.ShowAsync(file);
        viewModel.SelectedCommit = viewModel.Commits[0];
        await Task.Delay(500);

        window.RequestedThemeVariant = ThemeVariant.Dark;
        Screenshot(window, "theme-main-dark.png");
        window.RequestedThemeVariant = ThemeVariant.Light;
        Screenshot(window, "theme-main-light.png");
        window.Close();
    }

    [AvaloniaTest]
    public async Task Screenshots_start_view_and_error_banner_in_both_variants()
    {
        string root = Directory.CreateTempSubdirectory("ge-avalonia-themeshots-").FullName;
        try
        {
            List<Repository> history = [];
            foreach (string name in new[] { "gitextensions", "avalonia-playground", "dotfiles" })
            {
                string dir = Path.Combine(root, name);
                Directory.CreateDirectory(dir);
                history.Add(new Repository(dir + Path.DirectorySeparatorChar));
            }

            await RepositoryHistoryManager.Locals.SaveRecentHistoryAsync(history);
            MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
            MainWindow window = new() { DataContext = viewModel };
            window.Show();
            await viewModel.Recent.LoadAsync();

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Screenshot(window, "theme-start-dark.png");
            window.RequestedThemeVariant = ThemeVariant.Light;
            Screenshot(window, "theme-start-light.png");

            MessageDialogs.Instance.ShowError(null, "fatal: unable to access the remote repository.", "Git error");
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Screenshot(window, "theme-error-dark.png");
            window.RequestedThemeVariant = ThemeVariant.Light;
            Screenshot(window, "theme-error-light.png");
            window.Close();
        }
        finally
        {
            await RepositoryHistoryManager.Locals.SaveRecentHistoryAsync([]);
            Directory.Delete(root, recursive: true);
        }
    }
}
