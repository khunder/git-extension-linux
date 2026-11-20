using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
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
    private bool _previousDontConfirmAmend;
    private ThemeService _previousTheme = null!;
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
        _previousDontConfirmAmend = AppSettings.DontConfirmAmend.Value;
        _previousTheme = ThemeService.Current;
        ThemeService.Current = new ThemeService(new ThemeStore(Path.Combine(_directory, "theme.json")));
        _previousBehavior = BehaviorSettings.Current;
        BehaviorSettings.Current = new BehaviorSettings(Path.Combine(_directory, "behavior.json"));
    }

    [TearDown]
    public void Restore()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _previousGlobalConfig);
        AppSettings.GitCommandValue = _previousGitCommand;
        AppSettings.ShowStashes = _previousShowStashes;
        AppSettings.DontConfirmAmend.Value = _previousDontConfirmAmend;
        AppSettings.SaveSettings();
        ThemeService.Current = _previousTheme;
        BehaviorSettings.Current = _previousBehavior;
        try
        {
            TestFiles.DeleteTree(_directory);
        }
        catch (IOException)
        {
        }
    }

    private SettingsViewModel Create(string? repository = null)
        => new(() => repository, BehaviorSettings.Current);

    private async Task<SettingsViewModel> OpenAsync(string? page = null, string? repository = null)
    {
        SettingsViewModel vm = Create(repository);
        await vm.OpenAsync(page);
        return vm;
    }

    private static T Row<T>(SettingsViewModel vm, string pageId, string labelStart)
        where T : SettingRow
        => vm.FindPage(pageId)!.Rows.OfType<T>().First(r => r.Label.StartsWith(labelStart, StringComparison.Ordinal));

    private string ReadGlobal(string key)
    {
        using TestRepository repository = new();
        return repository.GitOutput("config", "--global", key).Trim();
    }

    [AvaloniaTest]
    public async Task The_tree_has_the_pages_of_the_original_dialog()
    {
        SettingsViewModel vm = await OpenAsync();

        vm.IsOpen.Should().BeTrue();
        vm.SelectedPage!.Id.Should().Be(SettingsPageIds.Checklist);
        vm.WindowTitle.Should().Be("Settings - Checklist");
        vm.Pages.Select(p => p.Title).Should().Equal("Git Extensions Next", "Git", "Plugins");
        vm.Pages[0].Children.Select(p => p.Title).Should().Equal(
            "General", "Appearance", "Revision links", "Build server integration", "Scripts", "Hotkeys", "Shell extension", "Advanced", "Detailed", "SSH");
        vm.FindPage(SettingsPageIds.Appearance)!.Children.Select(p => p.Title).Should().Equal("Sorting", "Colors", "Fonts", "Console style");
        vm.FindPage(SettingsPageIds.Detailed)!.Children.Select(p => p.Title).Should().Equal("Browse repository", "Commit dialog", "Diff viewer", "Blame viewer");
        vm.Pages[1].Children.Select(p => p.Title).Should().Equal("Paths", "Config", "Advanced");
    }

    [AvaloniaTest]
    public async Task The_checklist_reports_git_and_the_identity_without_shell_extension_lines()
    {
        SettingsViewModel vm = await OpenAsync();
        ChecklistViewModel checklist = vm.SelectedPage!.Checklist!;

        checklist.Items.Should().Contain(i => i.Ok && i.Text.StartsWith("Git ", StringComparison.Ordinal) && i.Text.EndsWith("is found on your computer.", StringComparison.Ordinal));
        checklist.Items.Should().Contain(i => i.Ok && i.Text == "A username and an email address are configured.");
        checklist.Items.Should().NotContain(i => i.Text.Contains("hell extension", StringComparison.OrdinalIgnoreCase));
        checklist.Items.Should().Contain(i => i.Text.StartsWith("The configured language is", StringComparison.Ordinal));
    }

    [AvaloniaTest]
    public async Task A_missing_identity_is_red_and_offers_the_config_page()
    {
        File.WriteAllText(_globalConfig, string.Empty);
        SettingsViewModel vm = await OpenAsync();

        ChecklistItem item = vm.SelectedPage!.Checklist!.Items.Single(i => i.Text.Contains("username", StringComparison.Ordinal));
        item.Ok.Should().BeFalse();
        item.FixPageId.Should().Be(SettingsPageIds.GitConfig);
        await vm.GoToPageCommand.ExecuteAsync(item.FixPageId);
        vm.SelectedPage!.Id.Should().Be(SettingsPageIds.GitConfig);
        vm.WindowTitle.Should().Be("Settings - Config");
    }

    [AvaloniaTest]
    public async Task The_config_page_edits_the_global_identity_and_clearing_unsets_it()
    {
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.GitConfig);
        TextRow name = Row<TextRow>(vm, SettingsPageIds.GitConfig, "User name");
        TextRow email = Row<TextRow>(vm, SettingsPageIds.GitConfig, "User email");
        name.Value.Should().Be("Global Person");
        email.Value.Should().Be("global@example.com");

        name.Value = "New Name";
        email.Value = string.Empty;
        await vm.ApplyCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().BeNull();
        ReadGlobal("user.name").Should().Be("New Name");
        ReadGlobal("user.email").Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Local_source_writes_the_repository_config_and_effective_is_read_only()
    {
        using TestRepository repository = new();
        repository.RunGit("config", "--local", "user.email", "work@example.com");
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.GitConfig, repository.Path);
        vm.HasRepository.Should().BeTrue();

        vm.IsEffectiveSource = true;
        await vm.WhenIdleAsync();
        TextRow email = Row<TextRow>(vm, SettingsPageIds.GitConfig, "User email");
        email.Value.Should().Be("work@example.com");
        email.IsEnabled.Should().BeFalse();

        vm.IsLocalSource = true;
        await vm.WhenIdleAsync();
        email.Value.Should().Be("work@example.com");
        email.IsEnabled.Should().BeTrue();
        email.Value = "local2@example.com";
        TextRow editor = Row<TextRow>(vm, SettingsPageIds.GitConfig, "Editor");
        editor.Value = "vim";
        await vm.ApplyCommand.ExecuteAsync(null);

        repository.GitOutput("config", "--local", "user.email").Trim().Should().Be("local2@example.com");
        repository.GitOutput("config", "--local", "core.editor").Trim().Should().Be("vim");
        ReadGlobal("user.email").Should().Be("global@example.com");

        vm.IsGlobalSource = true;
        await vm.WhenIdleAsync();
        email.Value.Should().Be("global@example.com");
    }

    [AvaloniaTest]
    public async Task The_advanced_git_page_sets_and_unsets_keys()
    {
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.GitAdvanced);
        ChoiceRow prune = Row<ChoiceRow>(vm, SettingsPageIds.GitAdvanced, "Fetch: prune");
        prune.Value.Should().Be("(not set)");
        prune.Value = "true";
        await vm.ApplyCommand.ExecuteAsync(null);
        ReadGlobal("fetch.prune").Should().Be("true");

        prune.Value = "(not set)";
        await vm.ApplyCommand.ExecuteAsync(null);
        ReadGlobal("fetch.prune").Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Switching_pages_saves_the_page_that_was_left()
    {
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.Confirmations);
        BoolRow amend = Row<BoolRow>(vm, SettingsPageIds.Confirmations, "Amending");
        bool before = amend.Value;
        amend.Value = !before;

        await vm.GoToPageCommand.ExecuteAsync(SettingsPageIds.General);

        AppSettings.DontConfirmAmend.Value.Should().Be(before);
    }

    [AvaloniaTest]
    public async Task Cancel_closes_without_reverting_saved_pages_but_keeps_unsaved_edits_out()
    {
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.GitConfig);
        TextRow name = Row<TextRow>(vm, SettingsPageIds.GitConfig, "User name");
        name.Value = "Applied";
        await vm.ApplyCommand.ExecuteAsync(null);
        name.Value = "Not applied";
        vm.CancelCommand.Execute(null);

        vm.IsOpen.Should().BeFalse();
        ReadGlobal("user.name").Should().Be("Applied");
    }

    [AvaloniaTest]
    public async Task Ok_saves_and_closes()
    {
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.GitConfig);
        Row<TextRow>(vm, SettingsPageIds.GitConfig, "User name").Value = "Via Ok";
        await vm.OkCommand.ExecuteAsync(null);

        vm.IsOpen.Should().BeFalse();
        ReadGlobal("user.name").Should().Be("Via Ok");
    }

    [AvaloniaTest]
    public async Task The_general_page_persists_history_options_and_reports_the_change()
    {
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.General);
        bool? historyChanged = null;
        vm.Saved += changed => historyChanged = changed;

        IntRow limit = Row<IntRow>(vm, SettingsPageIds.General, "Maximum number of commits");
        BoolRow stashes = Row<BoolRow>(vm, SettingsPageIds.General, "Show stashes");
        limit.Value.Should().Be(2000);
        limit.Value = 50;
        stashes.Value = !stashes.Value;
        bool expectedStashes = stashes.Value;
        await vm.ApplyCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().BeNull();
        historyChanged.Should().BeTrue();
        BehaviorSettings.Current.HistoryLimit.Should().Be(50);
        new BehaviorSettings(BehaviorSettings.Current.FilePath).HistoryLimit.Should().Be(50);
        AppSettings.ShowStashes.Should().Be(expectedStashes);
    }

    [TestCase(0)]
    [TestCase(-5)]
    [TestCase(99999999)]
    public async Task An_out_of_range_history_limit_is_refused(int value)
    {
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.General);
        Row<IntRow>(vm, SettingsPageIds.General, "Maximum number of commits").Value = value;
        await vm.ApplyCommand.ExecuteAsync(null);

        vm.HasError.Should().BeTrue();
        BehaviorSettings.Current.HistoryLimit.Should().Be(2000);
        await vm.OkCommand.ExecuteAsync(null);
        vm.IsOpen.Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task A_bad_git_path_is_refused_and_not_saved()
    {
        string before = AppSettings.GitCommandValue;
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.GitPaths);
        TextRow git = Row<TextRow>(vm, SettingsPageIds.GitPaths, "Command used to run git");
        git.Value = Path.Combine(_directory, "no-such-git");
        await vm.OkCommand.ExecuteAsync(null);

        vm.IsOpen.Should().BeTrue();
        vm.HasError.Should().BeTrue();
        AppSettings.GitCommandValue.Should().Be(before);
        git.Value.Should().Be(before);
    }

    [AvaloniaTest]
    public async Task The_git_test_button_reports_the_version_and_rejects_other_programs()
    {
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.GitPaths);
        ActionRow test = vm.FindPage(SettingsPageIds.GitPaths)!.Rows.OfType<ActionRow>().Single();
        await vm.RunActionCommand.ExecuteAsync(test);
        test.ResultOk.Should().BeTrue();
        test.Result.Should().StartWith("git version");

        vm.FilePicker = new FixedFilePicker("/bin/true");
        TextRow git = Row<TextRow>(vm, SettingsPageIds.GitPaths, "Command used to run git");
        await vm.BrowseCommand.ExecuteAsync(git);
        git.Value.Should().Be("/bin/true");
        await vm.RunActionCommand.ExecuteAsync(test);
        test.ResultOk.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task The_theme_row_applies_immediately()
    {
        SettingsViewModel vm = await OpenAsync(SettingsPageIds.Colors);
        ChoiceRow theme = Row<ChoiceRow>(vm, SettingsPageIds.Colors, "Application theme");
        theme.Value = "Dark";
        ThemeService.Current.Choice.Should().Be(ThemeChoice.Dark);
        theme.Value = "System";
    }

    [AvaloniaTest]
    public async Task Unsupported_features_show_a_notice_and_no_controls()
    {
        SettingsViewModel vm = await OpenAsync();
        foreach (string id in new[] { SettingsPageIds.Scripts, SettingsPageIds.Hotkeys, SettingsPageIds.BuildServer, SettingsPageIds.RevisionLinks, SettingsPageIds.Plugins, SettingsPageIds.ShellExtension })
        {
            vm.FindPage(id)!.Rows.Should().ContainSingle().Which.Should().BeOfType<NoticeRow>();
        }

        vm.FindPage(SettingsPageIds.Scripts)!.Rows[0].Label.Should().StartWith("Not available yet");
    }

    [AvaloniaTest]
    public async Task Search_filters_the_tree_and_highlights_matching_labels()
    {
        SettingsViewModel vm = await OpenAsync();
        vm.SearchText = "blame";

        vm.FindPage(SettingsPageIds.BlameViewer)!.IsVisible.Should().BeTrue();
        vm.FindPage(SettingsPageIds.Detailed)!.IsVisible.Should().BeTrue();
        vm.FindPage(SettingsPageIds.Ssh)!.IsVisible.Should().BeFalse();
        vm.SelectedPage!.Id.Should().NotBe(SettingsPageIds.Checklist);

        vm.SearchText = "stash count";
        SettingsPageViewModel general = vm.FindPage(SettingsPageIds.General)!;
        general.IsVisible.Should().BeTrue();
        general.Rows.Where(r => r.IsMatch).Select(r => r.Label).Should().Equal("Show stash count");
        vm.SelectedPage!.Id.Should().Be(SettingsPageIds.General);

        vm.SearchText = string.Empty;
        vm.FindPage(SettingsPageIds.Ssh)!.IsVisible.Should().BeTrue();
        general.Rows.Any(r => r.IsMatch).Should().BeFalse();
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

        await viewModel.Settings.OpenAsync(SettingsPageIds.General);
        Row<IntRow>(viewModel.Settings, SettingsPageIds.General, "Maximum number of commits").Value = 1;
        await viewModel.Settings.ApplyCommand.ExecuteAsync(null);

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

        DialogWindow.Open.Should().BeEmpty();

        await viewModel.Settings.OpenAsync();
        ShellTestSupport.Settle();
        DialogWindow dialog = DialogWindow.Open.Should().ContainSingle().Which;
        dialog.Title.Should().Be("Settings");
        dialog.CanResize.Should().BeTrue();
        dialog.Width.Should().Be(980);
        dialog.Height.Should().Be(720);
        dialog.Find<Button>("OkButton").IsDefault.Should().BeTrue();
        dialog.Find<Button>("CancelButton").Should().NotBeNull();
        dialog.Find<Button>("ApplyButton").Should().NotBeNull();

        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        ShellTestSupport.Settle();
        viewModel.Settings.IsOpen.Should().BeFalse();
        DialogWindow.Open.Should().BeEmpty();
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_dashboard_and_the_menu_open_the_settings()
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Button button = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "SettingsButton");
        button.Command!.Execute(null);
        await viewModel.Settings.WhenIdleAsync();
        viewModel.Settings.IsOpen.Should().BeTrue();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Pages_render_in_light_and_dark()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1200, Height = 860 };
        window.Show();
        await viewModel.OpenAsync(repository.Path);

        try
        {
            foreach ((ThemeVariant variant, string name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                Application.Current!.RequestedThemeVariant = variant;
                foreach (string page in new[] { SettingsPageIds.Checklist, SettingsPageIds.General, SettingsPageIds.Confirmations, SettingsPageIds.GitConfig, SettingsPageIds.Ssh, SettingsPageIds.Scripts, SettingsPageIds.Colors })
                {
                    await viewModel.Settings.OpenAsync(page);
                    if (page == SettingsPageIds.GitConfig)
                    {
                        viewModel.Settings.IsLocalSource = true;
                        await viewModel.Settings.WhenIdleAsync();
                    }

                    if (page == SettingsPageIds.General)
                    {
                        viewModel.Settings.SearchText = "stash";
                    }

                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
                    Directory.CreateDirectory(directory);
                    string file = Path.Combine(directory, $"settings-{page}-{name}.png");
                    DialogWindow.Open.Should().ContainSingle().Which.CaptureRenderedFrame()!.Save(file);
                    TestContext.AddTestAttachment(file);
                }
            }
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }
}
