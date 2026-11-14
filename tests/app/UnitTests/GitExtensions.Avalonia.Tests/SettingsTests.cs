using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class SettingsTests
{
    private string _directory = null!;
    private string _globalConfig = null!;
    private string? _previousGlobalConfig;
    private string _previousGitCommand = null!;
    private bool _previousShowStashes;
    private BehaviorSettings _previousBehavior = null!;

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class FixedFilePicker(string? file) : IFilePicker
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult(file);
    }

    [SetUp]
    public void Isolate()
    {
        _directory = Directory.CreateTempSubdirectory("ge-settings-").FullName;
        _globalConfig = Path.Combine(_directory, "gitconfig");
        File.WriteAllText(_globalConfig, "[user]\n\tname = Global Person\n\temail = global@example.com\n");
        _previousGlobalConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _globalConfig);

        _previousGitCommand = AppSettings.GitCommandValue;
        _previousShowStashes = AppSettings.ShowStashes;
        _previousBehavior = BehaviorSettings.Current;
        BehaviorSettings.Current = new BehaviorSettings(Path.Combine(_directory, "behavior.json"));
    }

    [TearDown]
    public void Restore()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _previousGlobalConfig);
        AppSettings.GitCommandValue = _previousGitCommand;
        AppSettings.ShowStashes = _previousShowStashes;
        BehaviorSettings.Current = _previousBehavior;
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SettingsViewModel Create(string? repository = null)
        => new(() => repository, BehaviorSettings.Current);

    private string ReadGlobal(string key)
    {
        using TestRepository repository = new();
        return repository.GitOutput("config", "--global", key).Trim();
    }

    [AvaloniaTest]
    public async Task Open_loads_the_persisted_values()
    {
        AppSettings.GitCommandValue = string.Empty;
        SettingsViewModel vm = Create();
        await vm.OpenAsync();

        vm.IsOpen.Should().BeTrue();
        vm.GitPath.Should().BeEmpty();
        vm.UserName.Should().Be("Global Person");
        vm.UserEmail.Should().Be("global@example.com");
        vm.HistoryLimitText.Should().Be("2000");
        vm.ShowStashes.Should().Be(AppSettings.ShowStashes);
        vm.HasLocalIdentityOverride.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Save_persists_identity_and_behavior()
    {
        SettingsViewModel vm = Create();
        bool? historyChanged = null;
        vm.Saved += changed => historyChanged = changed;
        await vm.OpenAsync();

        vm.UserName = "New Name";
        vm.UserEmail = "new@example.com";
        vm.HistoryLimitText = "50";
        vm.ShowStashes = !vm.ShowStashes;
        bool expectedStashes = vm.ShowStashes;
        await vm.SaveAsync();

        vm.ErrorMessage.Should().BeNull();
        vm.IsOpen.Should().BeFalse();
        historyChanged.Should().BeTrue();
        ReadGlobal("user.name").Should().Be("New Name");
        ReadGlobal("user.email").Should().Be("new@example.com");
        BehaviorSettings.Current.HistoryLimit.Should().Be(50);
        new BehaviorSettings(BehaviorSettings.Current.FilePath).HistoryLimit.Should().Be(50);
        AppSettings.ShowStashes.Should().Be(expectedStashes);
    }

    [AvaloniaTest]
    public async Task Clearing_the_identity_unsets_it()
    {
        SettingsViewModel vm = Create();
        await vm.OpenAsync();
        vm.UserEmail = string.Empty;
        await vm.SaveAsync();

        vm.ErrorMessage.Should().BeNull();
        ReadGlobal("user.email").Should().BeEmpty();
        ReadGlobal("user.name").Should().Be("Global Person");
    }

    [AvaloniaTest]
    public async Task Cancel_discards_staged_edits()
    {
        SettingsViewModel vm = Create();
        await vm.OpenAsync();
        vm.UserName = "Discarded";
        vm.HistoryLimitText = "7";
        vm.CancelCommand.Execute(null);

        vm.IsOpen.Should().BeFalse();
        ReadGlobal("user.name").Should().Be("Global Person");
        BehaviorSettings.Current.HistoryLimit.Should().Be(2000);

        // Reopening shows the persisted values again.
        await vm.OpenAsync();
        vm.UserName.Should().Be("Global Person");
        vm.HistoryLimitText.Should().Be("2000");
    }

    [AvaloniaTest]
    public async Task A_bad_git_path_is_reported_and_nothing_is_saved()
    {
        string before = AppSettings.GitCommandValue;
        SettingsViewModel vm = Create();
        await vm.OpenAsync();
        vm.GitPath = Path.Combine(_directory, "no-such-git");
        vm.UserName = "Should Not Be Written";
        await vm.SaveAsync();

        vm.IsOpen.Should().BeTrue();
        vm.HasError.Should().BeTrue();
        vm.GitTestSucceeded.Should().BeFalse();
        AppSettings.GitCommandValue.Should().Be(before);
        ReadGlobal("user.name").Should().Be("Global Person");
    }

    [AvaloniaTest]
    public async Task A_program_that_is_not_git_is_rejected()
    {
        SettingsViewModel vm = Create();
        await vm.OpenAsync();
        vm.GitPath = "/bin/true";
        await vm.TestGitCommand.ExecuteAsync(null);

        vm.GitTestSucceeded.Should().BeFalse();
        vm.GitTestResult.Should().NotBeNullOrEmpty();
    }

    [AvaloniaTest]
    public async Task Test_button_reports_the_git_version_and_browse_fills_the_path()
    {
        SettingsViewModel vm = Create();
        await vm.OpenAsync();
        await vm.TestGitCommand.ExecuteAsync(null);
        vm.GitTestSucceeded.Should().BeTrue();
        vm.GitTestResult.Should().StartWith("git version");

        vm.FilePicker = new FixedFilePicker("/usr/bin/git");
        await vm.BrowseGitCommand.ExecuteAsync(null);
        vm.GitPath.Should().Be("/usr/bin/git");
    }

    [TestCase("0")]
    [TestCase("-5")]
    [TestCase("abc")]
    [TestCase("")]
    [TestCase("99999999999")]
    public async Task An_invalid_history_limit_is_rejected(string text)
    {
        SettingsViewModel vm = Create();
        await vm.OpenAsync();
        vm.HistoryLimitText = text;
        await vm.SaveAsync();

        vm.HasError.Should().BeTrue();
        vm.IsOpen.Should().BeTrue();
        BehaviorSettings.Current.HistoryLimit.Should().Be(2000);
    }

    [AvaloniaTest]
    public async Task A_repository_override_is_shown_when_it_differs()
    {
        using TestRepository repository = new();
        repository.RunGit("config", "--local", "user.email", "work@example.com");
        SettingsViewModel vm = Create(repository.Path);
        await vm.OpenAsync();

        vm.HasLocalIdentityOverride.Should().BeTrue();
        vm.LocalIdentityNote.Should().Contain("work@example.com").And.NotContain("name");
        vm.UserEmail.Should().Be("global@example.com");
    }

    [AvaloniaTest]
    public async Task The_history_limit_limits_the_loaded_commits()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());

        await viewModel.OpenAsync(repository.Path);
        viewModel.Commits.Should().HaveCount(2);

        BehaviorSettings.Current.SetHistoryLimit(1);
        await viewModel.OpenAsync(repository.Path);
        viewModel.Commits.Should().HaveCount(1);
    }

    [AvaloniaTest]
    public async Task Saving_a_changed_limit_reloads_the_open_repository()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repository.Path);

        await viewModel.Settings.OpenAsync();
        viewModel.Settings.HistoryLimitText = "1";
        await viewModel.Settings.SaveAsync();

        // The reload runs on the UI thread; wait for it to land.
        for (int i = 0; i < 100 && viewModel.Commits.Count != 1; i++)
        {
            await Task.Delay(50);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        viewModel.Commits.Should().HaveCount(1);
    }

    [AvaloniaTest]
    public async Task The_gear_button_opens_the_overlay_and_escape_closes_it()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);

        SettingsView overlay = window.FindControl<SettingsView>("SettingsOverlay")!;
        overlay.IsVisible.Should().BeFalse();

        await viewModel.Settings.OpenAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        overlay.IsVisible.Should().BeTrue();

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        viewModel.Settings.IsOpen.Should().BeFalse();
        overlay.IsVisible.Should().BeFalse();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Settings_panel_renders_in_light_and_dark()
    {
        using TestRepository repository = new();
        repository.RunGit("config", "--local", "user.email", "work@example.com");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        await viewModel.Settings.OpenAsync();
        await viewModel.Settings.TestGitCommand.ExecuteAsync(null);

        try
        {
            foreach ((ThemeVariant variant, string name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                Application.Current!.RequestedThemeVariant = variant;
                viewModel.Settings.ErrorMessage = name == "dark" ? "The git executable is not valid, nothing was saved." : null;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, $"settings-{name}.png");
                window.CaptureRenderedFrame()!.Save(file);
                TestContext.AddTestAttachment(file);
            }
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }
}
