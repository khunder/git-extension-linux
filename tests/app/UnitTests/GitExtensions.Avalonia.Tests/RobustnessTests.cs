using System.Diagnostics;
using Avalonia.Headless.NUnit;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensionsDesktopTests;

/// <summary>
///  Odd repositories and hostile states: the app must say what is wrong and keep running.
/// </summary>
[TestFixture]
public class RobustnessTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private readonly List<string> _folders = [];

    [TearDown]
    public void DeleteFolders()
    {
        foreach (string folder in _folders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort cleanup of temp folders.
            }
        }

        _folders.Clear();
    }

    private static MainWindowViewModel CreateViewModel() => new(GitEnvironment.Initialize(), new NoFolderPicker());

    private string NewFolder(string name = "repo")
    {
        string root = Directory.CreateTempSubdirectory("ge-robust-").FullName;
        _folders.Add(root);
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Git(string folder, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-c", "user.name=T", "-c", "user.email=t@t", "-c", "protocol.file.allow=always", "-c", "init.defaultBranch=main" }.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    private string CommittedRepository(string name = "repo")
    {
        string folder = NewFolder(name);
        Git(folder, "init", "--quiet");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "one\n");
        Git(folder, "add", "a.txt");
        Git(folder, "commit", "--quiet", "-m", "first");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "one\ntwo\n");
        File.WriteAllText(Path.Combine(folder, "b.txt"), "new\n");
        Git(folder, "add", "-A");
        Git(folder, "commit", "--quiet", "-m", "second");
        return folder;
    }

    [AvaloniaTest]
    public async Task Empty_repository_without_commits_opens()
    {
        string folder = NewFolder();
        Git(folder, "init", "--quiet");
        File.WriteAllText(Path.Combine(folder, "new.txt"), "x\n");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(folder);

        viewModel.IsBusy.Should().BeFalse();
        viewModel.Commits.Should().BeEmpty();
        viewModel.Status.Should().NotContain("Failed");
        viewModel.ChangedFiles.Select(f => f.Name).Should().Equal("new.txt");
        viewModel.Branch.Should().NotBeNullOrEmpty();
    }

    [AvaloniaTest]
    public async Task Detached_head_opens_and_shows_history()
    {
        string folder = CommittedRepository();
        Git(folder, "checkout", "--quiet", "--detach", "HEAD~1");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(folder);

        viewModel.Status.Should().NotContain("Failed");
        viewModel.Commits.Select(c => c.Subject).Should().Equal("second", "first");
        viewModel.Branch.Should().NotBeNullOrEmpty();
    }

    [AvaloniaTest]
    public async Task Shallow_clone_opens()
    {
        string source = CommittedRepository("source");
        string folder = Path.Combine(Path.GetDirectoryName(source)!, "shallow");
        Git(Path.GetDirectoryName(source)!, "clone", "--quiet", "--depth", "1", "file://" + source, folder);
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(folder);

        viewModel.Status.Should().NotContain("Failed");
        viewModel.Commits.Select(c => c.Subject).Should().Equal("second");
        viewModel.Commits[0].Revision.ParentIds.Should().BeNullOrEmpty();
    }

    [AvaloniaTest]
    public async Task Bare_repository_reports_a_message_and_keeps_running()
    {
        string source = CommittedRepository("source");
        string folder = Path.Combine(Path.GetDirectoryName(source)!, "bare.git");
        Git(Path.GetDirectoryName(source)!, "clone", "--quiet", "--bare", source, folder);
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(folder);

        viewModel.IsBusy.Should().BeFalse();
        viewModel.ChangedFiles.Should().BeEmpty("the \"Git Error\" placeholder must not be listed as a file");
        viewModel.ErrorMessage.Should().Contain("bare repository");
    }

    [AvaloniaTest]
    public async Task Linked_worktree_opens_with_its_own_branch()
    {
        string folder = CommittedRepository();
        string worktree = Path.Combine(Path.GetDirectoryName(folder)!, "wt");
        Git(folder, "worktree", "add", "--quiet", "-b", "feature", worktree);
        File.WriteAllText(Path.Combine(worktree, "wt.txt"), "x\n");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(worktree);

        viewModel.Status.Should().NotContain("Failed");
        viewModel.Branch.Should().Be("feature");
        viewModel.Commits.Should().HaveCount(2);
        viewModel.ChangedFiles.Select(f => f.Name).Should().Equal("wt.txt");
    }

    [AvaloniaTest]
    public async Task Submodule_folder_opens_as_its_own_repository()
    {
        string library = CommittedRepository("library");
        string folder = CommittedRepository("super");
        Git(folder, "submodule", "--quiet", "add", "file://" + library, "lib");
        Git(folder, "commit", "--quiet", "-m", "add submodule");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(Path.Combine(folder, "lib"));

        viewModel.Status.Should().NotContain("Failed");
        viewModel.Commits.Should().HaveCount(2);
        Path.TrimEndingDirectorySeparator(viewModel.RepositoryPath).Should().Be(Path.Combine(folder, "lib"));

        // And the superproject lists the submodule as a changed entry once it moved.
        File.WriteAllText(Path.Combine(folder, "lib", "x.txt"), "x\n");
        Git(Path.Combine(folder, "lib"), "add", "x.txt");
        Git(Path.Combine(folder, "lib"), "commit", "--quiet", "-m", "move");
        await viewModel.OpenAsync(folder);
        viewModel.ChangedFiles.Select(f => f.Name).Should().Contain("lib");
        viewModel.Status.Should().NotContain("Failed");
    }

    [AvaloniaTest]
    public async Task Unusual_file_and_folder_names_are_listed_and_diffable()
    {
        string folder = NewFolder("dir with spaces");
        Git(folder, "init", "--quiet");
        Directory.CreateDirectory(Path.Combine(folder, "dir with spaces"));
        string[] names = ["dir with spaces/ünïcode.txt", "quote\"d.txt", "back\\slash.txt", "日本語.txt", "tab\tname.txt", "-dash.txt"];
        foreach (string name in names)
        {
            File.WriteAllText(Path.Combine(folder, name), "first\n");
        }

        Git(folder, "add", "-A");
        Git(folder, "commit", "--quiet", "-m", "names");
        foreach (string name in names)
        {
            File.WriteAllText(Path.Combine(folder, name), "first\nsecond\n");
        }

        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(folder);

        viewModel.Status.Should().NotContain("Failed");
        viewModel.ChangedFiles.Select(f => f.Name).Should().BeEquivalentTo(names);
        foreach (ChangedFileViewModel file in viewModel.ChangedFiles)
        {
            await viewModel.Diff!.ShowAsync(file);
            viewModel.Diff.Message.Should().BeEmpty(file.Name);
            viewModel.Diff.Lines.Should().Contain(l => l.Text == "+second", file.Name);
        }

        // Staging works for every one of them as well.
        await viewModel.Diff!.StageAllCommand.ExecuteAsync(null);
        viewModel.ChangedFiles.Should().OnlyContain(f => f.IsStaged);
    }

    [AvaloniaTest]
    public async Task Repository_behind_a_symlink_opens()
    {
        string folder = CommittedRepository();
        string link = Path.Combine(Path.GetDirectoryName(folder)!, "link to repo");
        Directory.CreateSymbolicLink(link, folder);
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(link);

        viewModel.Status.Should().NotContain("Failed");
        viewModel.Commits.Should().HaveCount(2);
    }

    [AvaloniaTest]
    public async Task Folder_deleted_while_open_does_not_crash_refresh_or_diff()
    {
        string folder = CommittedRepository();
        File.WriteAllText(Path.Combine(folder, "a.txt"), "changed\n");
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(folder);
        ChangedFileViewModel file = viewModel.ChangedFiles.Single();
        Directory.Delete(folder, recursive: true);

        Func<Task> act = async () =>
        {
            await viewModel.RefreshChangesAsync();
            await viewModel.Diff!.ShowAsync(file);
        };

        await act.Should().NotThrowAsync();
        viewModel.IsBusy.Should().BeFalse();
        viewModel.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [AvaloniaTest]
    public async Task Opening_a_vanished_path_reports_a_message()
    {
        string folder = CommittedRepository();
        Directory.Delete(folder, recursive: true);
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(folder);

        viewModel.IsBusy.Should().BeFalse();
        viewModel.Status.Should().Contain("not inside a Git repository");
    }

    [AvaloniaTest]
    public async Task Repository_owned_by_someone_else_reports_a_clear_message()
    {
        string folder = CommittedRepository();
        string? previous = Environment.GetEnvironmentVariable("GIT_TEST_ASSUME_DIFFERENT_OWNER");
        string? previousSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        string? previousGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        string emptyGlobalConfig = Path.Combine(folder, "empty-global-gitconfig");
        File.WriteAllText(emptyGlobalConfig, string.Empty);

        // Some machines (for example CI images) trust every directory through the system or global safe.directory setting.
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", emptyGlobalConfig);
        Environment.SetEnvironmentVariable("GIT_TEST_ASSUME_DIFFERENT_OWNER", "1");
        try
        {
            MainWindowViewModel viewModel = CreateViewModel();

            await viewModel.OpenAsync(folder);

            viewModel.IsBusy.Should().BeFalse();
            viewModel.ChangedFiles.Should().BeEmpty();
            viewModel.ErrorMessage.Should().Contain("dubious ownership").And.Contain("safe.directory");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_TEST_ASSUME_DIFFERENT_OWNER", previous);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", previousSystem);
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobal);
        }
    }

    [AvaloniaTest]
    public async Task Existing_index_lock_reports_the_failure_when_staging()
    {
        string folder = CommittedRepository();
        File.WriteAllText(Path.Combine(folder, "a.txt"), "changed\n");
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(folder);
        File.WriteAllText(Path.Combine(folder, ".git", "index.lock"), string.Empty);
        viewModel.SelectedChangedFile = viewModel.ChangedFiles.Single();
        await viewModel.Diff!.ShowAsync(viewModel.ChangedFiles.Single());

        await viewModel.Diff.StageCommand.ExecuteAsync(null);

        viewModel.IsBusy.Should().BeFalse();
        viewModel.ErrorMessage.Should().Contain("Failed to update the index").And.Contain("index.lock");
        viewModel.ChangedFiles.Single().IsStaged.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Index_lock_does_not_prevent_opening_a_repository()
    {
        string folder = CommittedRepository();
        File.WriteAllText(Path.Combine(folder, ".git", "index.lock"), string.Empty);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "changed\n");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(folder);

        viewModel.Status.Should().NotContain("Failed");
        viewModel.Commits.Should().HaveCount(2);
        viewModel.ChangedFiles.Select(f => f.Name).Should().Equal("a.txt");
    }
}
