using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class CloneInitTests
{
    private string _root = null!;

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    [SetUp]
    public void CreateRoot() => _root = Directory.CreateTempSubdirectory("ge-avalonia-clone-").FullName;

    [TearDown]
    public void DeleteRoot()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ---- Pure helpers ----

    [TestCase("https://github.com/owner/repo.git", "repo")]
    [TestCase("https://github.com/owner/repo", "repo")]
    [TestCase("https://github.com/owner/repo.git/", "repo")]
    [TestCase("https://github.com/owner/repo///", "repo")]
    [TestCase("ssh://git@host:2222/owner/repo.git", "repo")]
    [TestCase("git@github.com:owner/repo.git", "repo")]
    [TestCase("host:owner/repo.git", "repo")]
    [TestCase("host:repo.git", "repo")]
    [TestCase("file:///tmp/data/repo.git", "repo")]
    [TestCase("/srv/git/repo.git", "repo")]
    [TestCase("/srv/git/repo", "repo")]
    [TestCase(@"C:\Users\me\src\repo.git", "repo")]
    [TestCase(@"C:\Users\me\src\repo\", "repo")]
    [TestCase(@"\\server\share\repo.git", "repo")]
    [TestCase("  https://host/a/b.git  ", "b")]
    [TestCase("https://host/a/b.git?ref=x", "b")]
    [TestCase("my.repo.git", "my.repo")]
    [TestCase(".git", ".git")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase("..", "")]
    public void Target_name_is_derived_from_the_url(string url, string expected)
        => CloneTargetNameParser.Parse(url).Should().Be(expected);

    [Test]
    public void Validator_reports_each_problem_inline()
    {
        string existing = Path.Combine(_root, "full");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "x.txt"), "x");
        string file = Path.Combine(_root, "afile");
        File.WriteAllText(file, "x");

        CloneOptionsValidator.Validate("https://h/r.git", _root, "r", null, null).IsValid.Should().BeTrue();
        CloneOptionsValidator.Validate("https://h/r.git", _root, "r", "5", "feature/x").IsValid.Should().BeTrue();
        CloneOptionsValidator.Validate("https://h/r.git", Path.Combine(_root, "not-yet"), "r", "", "").IsValid.Should().BeTrue();

        CloneOptionsValidator.Validate("", _root, "r", null, null).Url.Should().NotBeNull();
        CloneOptionsValidator.Validate("  ", _root, "r", null, null).Url.Should().NotBeNull();
        CloneOptionsValidator.Validate("--upload-pack=x", _root, "r", null, null).Url.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", "", "r", null, null).ParentFolder.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", "relative/dir", "r", null, null).ParentFolder.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", file, "r", null, null).ParentFolder.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "", null, null).TargetName.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "a/b", null, null).TargetName.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "..", null, null).TargetName.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "full", null, null).TargetName.Should().Contain("not empty");
        CloneOptionsValidator.Validate("u", _root, "afile", null, null).TargetName.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "r", "0", null).Depth.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "r", "-3", null).Depth.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "r", "abc", null).Depth.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "r", "1.5", null).Depth.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "r", "99999999999", null).Depth.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "r", null, "-x").Branch.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "r", null, "a b").Branch.Should().NotBeNull();
        CloneOptionsValidator.Validate("u", _root, "r", null, "a..b").Branch.Should().NotBeNull();

        // An existing but empty target folder is fine.
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        CloneOptionsValidator.Validate("u", _root, "empty", null, null).TargetName.Should().BeNull();
    }

    // ---- View-model behavior ----

    private CloneViewModel CreateClone(List<string> opened, Func<string, IExecutable>? factory = null, List<string>? log = null)
        => new(
            new NoFolderPicker(),
            path =>
            {
                opened.Add(path);
                return Task.CompletedTask;
            },
            executableFactory: factory ?? LocalGit,
            appendLog: log is null ? null : chunk =>
            {
                lock (log)
                {
                    log.Add(chunk);
                }
            });

    // Newer git refuses file-based submodule transports unless allowed.
    private static IExecutable LocalGit(string directory)
        => new Executable(() => "git", directory, "-c protocol.file.allow=always");

    private string CreateOrigin(bool withSubmodule = false)
    {
        string origin = Path.Combine(_root, "origin.git");
        RemoteScenario.Run(_root, "init", "--quiet", "--bare", "--initial-branch=main", origin);
        string work = Path.Combine(_root, "work");
        RemoteScenario.Run(_root, "clone", "--quiet", origin, work);
        RemoteScenario.Run(work, "config", "user.name", "T");
        RemoteScenario.Run(work, "config", "user.email", "t@example.com");
        RemoteScenario.Run(work, "checkout", "-q", "-B", "main");
        RemoteScenario.Commit(work, "a.txt", "one", "first");
        RemoteScenario.Commit(work, "a.txt", "two", "second");
        RemoteScenario.Commit(work, "a.txt", "three", "third");
        RemoteScenario.Run(work, "push", "--quiet", "origin", "main");
        RemoteScenario.Run(work, "checkout", "-q", "-b", "feature");
        RemoteScenario.Commit(work, "f.txt", "feature", "feature work");
        RemoteScenario.Run(work, "push", "--quiet", "origin", "feature");
        RemoteScenario.Run(work, "checkout", "-q", "main");

        if (withSubmodule)
        {
            string lib = Path.Combine(_root, "lib");
            Directory.CreateDirectory(lib);
            RemoteScenario.Run(lib, "init", "--quiet", "--initial-branch=main");
            RemoteScenario.Run(lib, "config", "user.name", "T");
            RemoteScenario.Run(lib, "config", "user.email", "t@example.com");
            RemoteScenario.Commit(lib, "lib.txt", "lib", "lib commit");
            RemoteScenario.Run(work, "-c", "protocol.file.allow=always", "submodule", "add", "--quiet", lib, "libs/lib");
            RemoteScenario.Run(work, "commit", "--quiet", "-m", "Add submodule");
            RemoteScenario.Run(work, "push", "--quiet", "origin", "main");
        }

        return origin;
    }

    [AvaloniaTest]
    public async Task Clone_of_a_local_repository_opens_the_result_and_streams_output()
    {
        string origin = CreateOrigin();
        List<string> opened = [];
        List<string> log = [];
        CloneViewModel clone = CreateClone(opened, log: log);
        string parent = Path.Combine(_root, "dest");
        clone.Open(parent);
        clone.Url = origin;
        clone.TargetName.Should().Be("origin");

        await clone.CloneCommand.ExecuteAsync(null);

        clone.ErrorMessage.Should().BeNull();
        clone.IsOpen.Should().BeFalse();
        opened.Should().Equal(Path.Combine(parent, "origin"));
        File.ReadAllText(Path.Combine(parent, "origin", "a.txt")).Should().Be("three");
        string.Concat(log).Should().Contain("origin");
    }

    [AvaloniaTest]
    public async Task Editing_the_name_stops_it_following_the_url()
    {
        CloneViewModel clone = CreateClone([]);
        clone.Open(_root);
        clone.Url = "https://h/a/one.git";
        clone.TargetName.Should().Be("one");
        clone.TargetName = "mine";
        clone.Url = "https://h/a/two.git";
        clone.TargetName.Should().Be("mine");
        clone.TargetName = string.Empty;
        clone.TargetName.Should().Be("two");
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task Clone_honors_branch_and_depth()
    {
        string origin = CreateOrigin();
        List<string> opened = [];
        CloneViewModel clone = CreateClone(opened);
        clone.Open(_root);
        clone.Url = new Uri(origin).AbsoluteUri;
        clone.TargetName = "shallow";
        clone.Branch = "feature";
        clone.Depth = "1";

        await clone.CloneCommand.ExecuteAsync(null);

        clone.ErrorMessage.Should().BeNull();
        string target = Path.Combine(_root, "shallow");
        opened.Should().Equal(target);
        RemoteScenario.Run(target, "rev-parse", "--abbrev-ref", "HEAD").Should().Be("feature");
        RemoteScenario.Run(target, "rev-list", "--count", "HEAD").Should().Be("1");
        File.Exists(Path.Combine(target, "f.txt")).Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Clone_initializes_submodules_recursively_when_asked()
    {
        string origin = CreateOrigin(withSubmodule: true);
        List<string> opened = [];
        CloneViewModel clone = CreateClone(opened);
        clone.Open(_root);
        clone.Url = origin;
        clone.TargetName = "with-sub";
        await clone.CloneCommand.ExecuteAsync(null);
        clone.ErrorMessage.Should().BeNull();
        File.Exists(Path.Combine(_root, "with-sub", "libs", "lib", "lib.txt")).Should().BeFalse();

        clone.Open(_root);
        clone.Url = origin;
        clone.TargetName = "recursive";
        clone.RecurseSubmodules = true;
        await clone.CloneCommand.ExecuteAsync(null);
        clone.ErrorMessage.Should().BeNull();
        File.ReadAllText(Path.Combine(_root, "recursive", "libs", "lib", "lib.txt")).Should().Be("lib");
    }

    [AvaloniaTest]
    public async Task Clone_into_a_non_empty_folder_is_refused_and_nothing_is_deleted()
    {
        string origin = CreateOrigin();
        string target = Path.Combine(_root, "taken");
        Directory.CreateDirectory(target);
        string precious = Path.Combine(target, "precious.txt");
        File.WriteAllText(precious, "keep");
        int calls = 0;
        CloneViewModel clone = CreateClone([], dir =>
        {
            calls++;
            return LocalGit(dir);
        });
        clone.Open(_root);
        clone.Url = origin;
        clone.TargetName = "taken";

        await clone.CloneCommand.ExecuteAsync(null);

        calls.Should().Be(0);
        clone.NameError.Should().Contain("not empty");
        clone.IsRunning.Should().BeFalse();
        File.ReadAllText(precious).Should().Be("keep");
    }

    [AvaloniaTest]
    public async Task Failed_clone_reports_git_message_and_removes_only_its_own_folder()
    {
        List<string> opened = [];
        CloneViewModel clone = CreateClone(opened);
        string parent = Path.Combine(_root, "newparent");
        clone.Open(parent);
        clone.Url = Path.Combine(_root, "does-not-exist.git");

        await clone.CloneCommand.ExecuteAsync(null);

        opened.Should().BeEmpty();
        clone.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        clone.IsOpen.Should().BeTrue();
        Directory.Exists(Path.Combine(parent, "does-not-exist")).Should().BeFalse();
        clone.IsRunning.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Invalid_input_shows_inline_errors_without_running_git()
    {
        int calls = 0;
        CloneViewModel clone = CreateClone([], dir =>
        {
            calls++;
            return LocalGit(dir);
        });
        clone.Open(string.Empty);
        await clone.CloneCommand.ExecuteAsync(null);
        clone.HasUrlError.Should().BeTrue();
        clone.HasParentError.Should().BeTrue();
        clone.HasNameError.Should().BeTrue();

        clone.Url = "https://h/x.git";
        clone.UrlError.Should().BeNull("errors update as the input is fixed");
        clone.ParentFolder = _root;
        clone.Depth = "zero";
        await clone.CloneCommand.ExecuteAsync(null);
        clone.HasDepthError.Should().BeTrue();
        calls.Should().Be(0);
    }

    private string WriteSlowGit()
    {
        string script = Path.Combine(_root, "slowgit.sh");
        File.WriteAllText(
            script,
            "#!/bin/sh\n" +
            "for last; do :; done\n" +
            "mkdir -p \"$last\"\n" +
            "touch \"$last/partial.pack\"\n" +
            "echo 'Receiving objects: 1% (1/100)' >&2\n" +
            "sleep 120\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return script;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(end, "the condition should become true in time");
            await Task.Delay(20);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTest]
    public async Task Cancelling_removes_the_folder_this_clone_created_but_not_the_parent()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Uses a shell script as a stand-in for a slow git.");
        }

        string script = WriteSlowGit();
        List<string> opened = [];
        CloneViewModel clone = CreateClone(opened, dir => new Executable(script, dir));
        string parent = Path.Combine(_root, "dest");
        Directory.CreateDirectory(parent);
        File.WriteAllText(Path.Combine(parent, "sibling.txt"), "keep");
        string target = Path.Combine(parent, "slow");
        clone.Open(parent);
        clone.Url = "https://example.invalid/slow.git";

        Task running = clone.CloneCommand.ExecuteAsync(null);
        await WaitForAsync(() => File.Exists(Path.Combine(target, "partial.pack")) && clone.ProgressText.Contains("Receiving"));
        clone.IsRunning.Should().BeTrue();
        clone.CancelCommand.Execute(null);
        await running;

        clone.IsRunning.Should().BeFalse();
        clone.ErrorMessage.Should().Contain("cancelled");
        opened.Should().BeEmpty();
        Directory.Exists(target).Should().BeFalse();
        File.ReadAllText(Path.Combine(parent, "sibling.txt")).Should().Be("keep");
    }

    [AvaloniaTest]
    public async Task Cancelling_into_an_existing_empty_folder_keeps_that_folder()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Uses a shell script as a stand-in for a slow git.");
        }

        string script = WriteSlowGit();
        CloneViewModel clone = CreateClone([], dir => new Executable(script, dir));
        string target = Path.Combine(_root, "pre-existing");
        Directory.CreateDirectory(target);
        clone.Open(_root);
        clone.Url = "https://example.invalid/pre-existing.git";

        Task running = clone.CloneCommand.ExecuteAsync(null);
        await WaitForAsync(() => File.Exists(Path.Combine(target, "partial.pack")));
        clone.CancelCommand.Execute(null);
        await running;

        Directory.Exists(target).Should().BeTrue("the folder existed before the clone");
        Directory.EnumerateFileSystemEntries(target).Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Main_window_clone_opens_the_repository_and_records_it_as_recent()
    {
        string origin = CreateOrigin();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        viewModel.Recent.RequestClone.Should().NotBeNull();
        viewModel.Recent.CloneCommand.Execute(null);
        viewModel.Clone.IsOpen.Should().BeTrue();
        viewModel.Clone.ParentFolder = Path.Combine(_root, "clones");
        viewModel.Clone.Url = origin;

        await viewModel.Clone.CloneCommand.ExecuteAsync(null);

        viewModel.Clone.IsOpen.Should().BeFalse();
        viewModel.HasOpenedRepository.Should().BeTrue();
        viewModel.RepositoryPath.TrimEnd('/', '\\').Should().Be(Path.Combine(_root, "clones", "origin"));
        viewModel.Commits.Should().HaveCount(3);
        viewModel.Remote.OutputText.Should().Contain("git clone");
        await viewModel.Recent.LoadAsync();
        viewModel.Recent.Items.Select(i => i.Path.TrimEnd('/', '\\')).Should().Contain(Path.Combine(_root, "clones", "origin"));
    }

    // ---- Init ----

    private static IExecutable GitWithIdentity(string directory)
        => new Executable(() => "git", directory, "-c user.name=Tester -c user.email=tester@example.com");

    [AvaloniaTest]
    public async Task Init_creates_the_folder_with_the_chosen_branch_and_opens_it()
    {
        List<string> opened = [];
        InitViewModel init = new(new NoFolderPicker(), path =>
        {
            opened.Add(path);
            return Task.CompletedTask;
        }, GitWithIdentity);
        string folder = Path.Combine(_root, "a", "b", "fresh");
        init.Open(folder);
        init.InitialBranch = "trunk";

        await init.InitCommand.ExecuteAsync(null);

        init.ErrorMessage.Should().BeNull();
        init.IsOpen.Should().BeFalse();
        opened.Should().Equal(folder);
        RemoteScenario.Run(folder, "symbolic-ref", "--short", "HEAD").Should().Be("trunk");
        File.Exists(Path.Combine(folder, "README.md")).Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Init_can_create_an_initial_commit_with_a_readme()
    {
        InitViewModel init = new(new NoFolderPicker(), _ => Task.CompletedTask, GitWithIdentity);
        string folder = Path.Combine(_root, "withreadme");
        init.Open(folder);
        init.InitialBranch = "main";
        init.CreateReadmeCommit = true;

        await init.InitCommand.ExecuteAsync(null);

        init.ErrorMessage.Should().BeNull();
        RemoteScenario.Run(folder, "log", "--format=%s").Should().Be("Initial commit");
        RemoteScenario.Run(folder, "ls-files").Should().Be("README.md");
        File.ReadAllText(Path.Combine(folder, "README.md")).Should().StartWith("# withreadme");
    }

    [AvaloniaTest]
    public async Task Init_rejects_invalid_branch_names_and_existing_repositories()
    {
        int opened = 0;
        InitViewModel init = new(new NoFolderPicker(), _ =>
        {
            opened++;
            return Task.CompletedTask;
        }, GitWithIdentity);
        string folder = Path.Combine(_root, "invalid");
        init.Open(folder);
        init.InitialBranch = "bad name~1";

        await init.InitCommand.ExecuteAsync(null);

        init.BranchError.Should().NotBeNull();
        init.IsOpen.Should().BeTrue();
        opened.Should().Be(0);
        Directory.Exists(Path.Combine(folder, ".git")).Should().BeFalse();

        init.InitialBranch = "good/name";
        await init.InitCommand.ExecuteAsync(null);
        opened.Should().Be(1);

        init.Open(folder);
        await init.InitCommand.ExecuteAsync(null);
        init.FolderError.Should().Contain("already contains");
        init.Open(string.Empty);
        await init.InitCommand.ExecuteAsync(null);
        init.HasFolderError.Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Init_reports_a_missing_identity_clearly_but_keeps_the_repository()
    {
        string empty = Path.Combine(_root, "empty-config");
        File.WriteAllText(empty, string.Empty);
        string? previousGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        string? previousSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", empty);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        try
        {
            List<string> errors = [];
            List<string> opened = [];
            InitViewModel init = new(
                new NoFolderPicker(),
                path =>
                {
                    opened.Add(path);
                    return Task.CompletedTask;
                },
                dir => new Executable(() => "git", dir, "-c user.useConfigOnly=true"),
                reportError: errors.Add);
            string folder = Path.Combine(_root, "noidentity");
            init.Open(folder);
            init.CreateReadmeCommit = true;

            await init.InitCommand.ExecuteAsync(null);

            errors.Should().ContainSingle().Which.Should().Contain("does not know who you are").And.Contain("user.name");
            opened.Should().Equal(folder);
            Directory.Exists(Path.Combine(folder, ".git")).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobal);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", previousSystem);
        }
    }

    // ---- Rendering ----

    [AvaloniaTest]
    public async Task Dashboard_offers_clone_and_init_and_dialogs_render_in_light_and_dark()
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.Recent.LoadAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Avalonia.Controls.Button cloneButton = window.Find<Avalonia.Controls.Button>("CloneButton");
        cloneButton.IsVisible.Should().BeTrue();
        window.FindControl<CloneView>("CloneOverlay")!.IsVisible.Should().BeFalse();

        try
        {
            viewModel.ShowCloneCommand.Execute(null);
            viewModel.Clone.ParentFolder = "/home/me/repos";
            viewModel.Clone.Url = "git@github.com:owner/some-project.git";
            viewModel.Clone.Depth = "abc";
            viewModel.Clone.Branch = "main";
            viewModel.Clone.RecurseSubmodules = true;
            viewModel.Clone.CloneCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.FindControl<CloneView>("CloneOverlay")!.IsVisible.Should().BeTrue();
            Capture(window, "clone");

            viewModel.Clone.CancelCommand.Execute(null);
            viewModel.ShowInitCommand.Execute(null);
            viewModel.Init.Folder = "/home/me/repos/new-project";
            viewModel.Init.InitialBranch = "main";
            viewModel.Init.CreateReadmeCommit = true;
            viewModel.Init.ErrorMessage = "git init failed: example error text";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.FindControl<InitView>("InitOverlay")!.IsVisible.Should().BeTrue();
            window.FindControl<CloneView>("CloneOverlay")!.IsVisible.Should().BeFalse();
            Capture(window, "init");
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }

    private static void Capture(MainWindow window, string name)
    {
        foreach ((ThemeVariant variant, string suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            Application.Current!.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, $"{name}-{suffix}.png");
            window.CaptureRenderedFrame()!.Save(file);
            TestContext.AddTestAttachment(file);
        }
    }
}
