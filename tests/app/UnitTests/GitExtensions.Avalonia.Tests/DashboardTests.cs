using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

/// <summary>An in-memory repository history, so the dashboard tests never touch the user's real one.</summary>
internal sealed class FakeHistory : ILocalRepositoryManager
{
    public List<Repository> Recent { get; } = [];

    public List<Repository> Favorites { get; } = [];

    public Task<IList<Repository>> AddAsMostRecentAsync(string repositoryPath)
    {
        Recent.RemoveAll(r => Same(r.Path, repositoryPath));
        Recent.Insert(0, new Repository(repositoryPath));
        return Task.FromResult<IList<Repository>>([.. Recent]);
    }

    public Task<IList<Repository>> LoadRecentHistoryAsync() => Task.FromResult<IList<Repository>>([.. Recent]);

    public Task<IList<Repository>> RemoveRecentAsync(string repositoryPath)
    {
        Recent.RemoveAll(r => Same(r.Path, repositoryPath));
        return Task.FromResult<IList<Repository>>([.. Recent]);
    }

    public Task SaveRecentHistoryAsync(IEnumerable<Repository> repositoryHistory)
    {
        Recent.Clear();
        Recent.AddRange(repositoryHistory);
        return Task.CompletedTask;
    }

    public Task<IList<Repository>> AssignCategoryAsync(Repository repository, string? category)
    {
        Repository? existing = Favorites.FirstOrDefault(f => Same(f.Path, repository.Path));
        if (existing is null)
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                repository.Category = category;
                Favorites.Add(repository);
            }
        }
        else if (string.IsNullOrWhiteSpace(category))
        {
            Favorites.Remove(existing);
        }
        else
        {
            existing.Category = category;
        }

        return Task.FromResult<IList<Repository>>([.. Favorites]);
    }

    public Task<IList<Repository>> LoadFavouriteHistoryAsync() => Task.FromResult<IList<Repository>>([.. Favorites]);

    public Task<IList<Repository>> RemoveFavouriteAsync(string repositoryPath)
    {
        Favorites.RemoveAll(r => Same(r.Path, repositoryPath));
        return Task.FromResult<IList<Repository>>([.. Favorites]);
    }

    public Task SaveFavouriteHistoryAsync(IEnumerable<Repository> repositoryHistory)
    {
        Favorites.Clear();
        Favorites.AddRange(repositoryHistory);
        return Task.CompletedTask;
    }

    public Task RemoveInvalidRepositoriesAsync(Func<string, bool> predicate) => Task.CompletedTask;

    private static bool Same(string a, string b)
        => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
}

[TestFixture]
public class DashboardTests
{
    private string _root = null!;

    [SetUp]
    public void CreateRoot() => _root = Directory.CreateTempSubdirectory("ge-dashboard-").FullName;

    [TearDown]
    public void DeleteRoot()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string MakeRepository(string name, string branch)
    {
        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        Git(directory, "init", "--quiet", "--initial-branch=" + branch);
        Git(directory, "-c", "user.name=T", "-c", "user.email=t@example.com", "commit", "--quiet", "--allow-empty", "-m", "first");
        return directory;
    }

    private static void Git(string directory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
    }

    private static MainWindowViewModel CreateViewModel(FakeHistory history) => new(GitEnvironment.Initialize(), new NoPicker(), history);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(20);
            ShellTestSupport.Settle();
        }

        condition().Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Dashboard_is_the_page_without_a_repository_and_again_after_closing()
    {
        string path = MakeRepository("alpha", "main");
        FakeHistory history = new();
        MainWindowViewModel viewModel = CreateViewModel(history);
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        ShellTestSupport.Settle();

        window.Find<DashboardView>("DashboardPage").IsVisible.Should().BeTrue();
        window.Find<Grid>("MainColumns").IsVisible.Should().BeFalse();

        await viewModel.OpenAsync(path);
        ShellTestSupport.Settle();
        window.Find<DashboardView>("DashboardPage").IsVisible.Should().BeFalse();
        window.Find<Grid>("MainColumns").IsVisible.Should().BeTrue();
        viewModel.Commits.Should().NotBeEmpty();

        viewModel.CloseRepositoryCommand.Execute(null);
        ShellTestSupport.Settle();
        window.Find<DashboardView>("DashboardPage").IsVisible.Should().BeTrue();
        window.Find<Grid>("MainColumns").IsVisible.Should().BeFalse();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Closing_clears_the_state_and_a_second_open_works()
    {
        using TestRepository first = new();
        using TestRepository second = new();
        second.AddRichHistory();
        FakeHistory history = new();
        MainWindowViewModel viewModel = CreateViewModel(history);

        await viewModel.OpenAsync(first.Path);
        viewModel.SelectedCommit = viewModel.Commits[0];
        viewModel.SelectedChangedFile = viewModel.ChangedFiles.First();
        viewModel.Commits.Should().HaveCount(2);

        viewModel.CloseRepositoryCommand.Execute(null);
        viewModel.HasOpenedRepository.Should().BeFalse();
        viewModel.ShowStartView.Should().BeTrue();
        viewModel.Commits.Should().BeEmpty();
        viewModel.ChangedFiles.Should().BeEmpty();
        viewModel.SelectedCommit.Should().BeNull();
        viewModel.SelectedChangedFile.Should().BeNull();
        viewModel.Diff.Should().BeNull();
        viewModel.Commit.Should().BeNull();
        viewModel.Branch.Should().BeNull();
        viewModel.Refs.Roots.Should().BeEmpty();
        viewModel.RepositoryPath.Should().BeEmpty();
        viewModel.IsBusy.Should().BeFalse();
        viewModel.CommitDetails.HasCommit.Should().BeFalse();

        // Closing twice is harmless.
        viewModel.CloseRepositoryCommand.Execute(null);

        await viewModel.OpenAsync(second.Path);
        viewModel.HasOpenedRepository.Should().BeTrue();
        viewModel.Commits.Count.Should().Be(4);
        viewModel.Branch.Should().Be("main");
        viewModel.Refs.Roots.Should().NotBeEmpty();
    }

    [AvaloniaTest]
    public async Task Closing_while_a_repository_is_still_loading_shows_nothing_stale()
    {
        using TestRepository repository = new();
        FakeHistory history = new();
        MainWindowViewModel viewModel = CreateViewModel(history);

        Task opening = viewModel.OpenAsync(repository.Path);
        viewModel.CloseRepositoryCommand.Execute(null);
        await opening;
        ShellTestSupport.Settle();

        // The cancelled open may already have shown the repository; closing after it has finished must still leave nothing behind.
        viewModel.CloseRepositoryCommand.Execute(null);
        ShellTestSupport.Settle();
        viewModel.HasOpenedRepository.Should().BeFalse();
        viewModel.Commits.Should().BeEmpty();
        viewModel.ChangedFiles.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Enter_on_the_dashboard_opens_the_first_repository_without_clicking()
    {
        using TestRepository repository = new();
        FakeHistory history = new();
        history.Recent.Add(new GitCommands.UserRepositoryHistory.Repository(repository.Path));
        MainWindowViewModel viewModel = CreateViewModel(history);
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.Dashboard.LoadAsync();
        ShellTestSupport.Settle();
        ShellTestSupport.Settle();
        ListBox[] lists = [.. window.GetVisualDescendants().OfType<ListBox>().Where(l => l.Classes.Contains("repos"))];
        lists.Should().NotBeEmpty("the dashboard shows the recent repository");
        lists[0].SelectedItem.Should().NotBeNull("the first repository is selected");
        TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement().Should().NotBeNull();

        window.KeyPress(Key.Enter, Avalonia.Input.RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Enter, Avalonia.Input.RawInputModifiers.None, PhysicalKey.None, null);
        for (int i = 0; i < 100 && !viewModel.HasOpenedRepository; i++)
        {
            await Task.Delay(25);
            ShellTestSupport.Settle();
        }

        viewModel.HasOpenedRepository.Should().BeTrue();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Ctrl_W_closes_the_repository_only_when_one_is_open()
    {
        using TestRepository repository = new();
        FakeHistory history = new();
        MainWindowViewModel viewModel = CreateViewModel(history);
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        AppCommand close = viewModel.Commands.Single(c => c.Id == "repo.close");
        close.CanExecute().Should().BeFalse();

        await viewModel.OpenAsync(repository.Path);
        close.CanExecute().Should().BeTrue();
        window.KeyPress(Key.W, Avalonia.Input.RawInputModifiers.Control, PhysicalKey.None, null);
        window.KeyRelease(Key.W, Avalonia.Input.RawInputModifiers.Control, PhysicalKey.None, null);
        ShellTestSupport.Settle();

        viewModel.HasOpenedRepository.Should().BeFalse();
        window.Find<DashboardView>("DashboardPage").IsVisible.Should().BeTrue();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Opening_from_the_dashboard_switches_to_browse_and_records_the_recent_entry()
    {
        string path = MakeRepository("beta", "develop");
        FakeHistory history = new();
        history.Recent.Add(new Repository(path + Path.DirectorySeparatorChar));
        MainWindowViewModel viewModel = CreateViewModel(history);
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.Dashboard.LoadAsync();

        DashboardEntry entry = viewModel.Dashboard.Categories.Single().Entries.Single();
        viewModel.Dashboard.OpenCommand.Execute(entry);
        await WaitUntilAsync(() => viewModel.HasOpenedRepository && !viewModel.IsBusy);
        viewModel.Branch.Should().Be("develop");
        window.Find<Grid>("MainColumns").IsVisible.Should().BeTrue();
        await WaitUntilAsync(() => history.Recent.Count == 1 && viewModel.Dashboard.Categories.Count == 1);

        viewModel.CloseRepositoryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Dashboard.IsLoaded && viewModel.Dashboard.Categories.Count == 1);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Dashboard_lists_the_recent_repositories_with_their_branch()
    {
        string alpha = MakeRepository("alpha", "main");
        string beta = MakeRepository("beta", "feature/login");
        string gone = Path.Combine(_root, "missing");
        FakeHistory history = new();
        history.Recent.AddRange([new Repository(alpha + "/"), new Repository(beta + "/"), new Repository(gone + "/")]);
        MainWindowViewModel viewModel = CreateViewModel(history);

        await viewModel.Dashboard.LoadAsync();
        DashboardCategory recent = viewModel.Dashboard.Categories.Single();
        recent.Title.Should().Be(DashboardViewModel.RecentTitle);
        recent.Entries.Select(e => e.Name).Should().Equal("alpha", "beta", "missing");
        recent.Entries[0].Path.Should().Be(alpha + "/");

        // The branch is read in the background (git rev-parse --abbrev-ref HEAD); a folder that is not a repository has none.
        await WaitUntilAsync(() => recent.Entries[0].Branch is not null && recent.Entries[1].Branch is not null);
        recent.Entries.Select(e => e.Branch).Should().Equal("main", "feature/login", null);

        // Filtering narrows the list by path.
        viewModel.Dashboard.Filter = "bet";
        viewModel.Dashboard.Categories.Single().Entries.Select(e => e.Name).Should().Equal("beta");
        viewModel.Dashboard.Filter = "nothing like it";
        viewModel.Dashboard.Categories.Should().BeEmpty();
        viewModel.Dashboard.IsEmpty.Should().BeTrue();
        viewModel.Dashboard.Filter = string.Empty;
        viewModel.Dashboard.Categories.Single().Entries.Should().HaveCount(3);
    }

    [AvaloniaTest]
    public async Task Context_menu_actions_remove_favorite_copy_and_explore()
    {
        string alpha = MakeRepository("alpha", "main");
        string beta = MakeRepository("beta", "main");
        FakeHistory history = new();
        history.Recent.AddRange([new Repository(alpha + "/"), new Repository(beta + "/")]);
        MainWindowViewModel viewModel = CreateViewModel(history);
        DashboardViewModel dashboard = viewModel.Dashboard;
        dashboard.ReadBranch = _ => Task.FromResult<string?>("main");
        await dashboard.LoadAsync();

        // Add to favorites: a Favorites category appears before the recent list; the entry in both is marked.
        DashboardEntry alphaEntry = dashboard.Categories.Single().Entries[0];
        alphaEntry.FavoriteMenuText.Should().Be("Add to favorites");
        await dashboard.ToggleFavoriteCommand.ExecuteAsync(alphaEntry);
        dashboard.Categories.Select(c => c.Title).Should().Equal(DashboardViewModel.DefaultFavoriteCategory, DashboardViewModel.RecentTitle);
        dashboard.Categories[0].Entries.Single().Path.Should().Be(alpha + "/");
        dashboard.Categories.SelectMany(c => c.Entries).Where(e => e.Name == "alpha").Should().OnlyContain(e => e.IsFavorite);
        history.Favorites.Single().Category.Should().Be(DashboardViewModel.DefaultFavoriteCategory);

        // Remove from favorites again.
        DashboardEntry favorite = dashboard.Categories[0].Entries.Single();
        favorite.FavoriteMenuText.Should().Be("Remove from favorites");
        await dashboard.ToggleFavoriteCommand.ExecuteAsync(favorite);
        history.Favorites.Should().BeEmpty();
        dashboard.Categories.Select(c => c.Title).Should().Equal(DashboardViewModel.RecentTitle);

        // User-defined categories of the history show up as their own lists.
        history.Favorites.Add(new Repository(beta + "/") { Category = "Work" });
        await dashboard.LoadAsync();
        dashboard.Categories.Select(c => c.Title).Should().Equal("Work", DashboardViewModel.RecentTitle);

        // Remove from list: the entry of the recent list leaves the history, a favorite leaves the favorites.
        await dashboard.RemoveFromListCommand.ExecuteAsync(dashboard.Categories[1].Entries.Single(e => e.Name == "alpha"));
        history.Recent.Select(r => r.Path).Should().Equal(beta + "/");
        await dashboard.RemoveFromListCommand.ExecuteAsync(dashboard.Categories[0].Entries.Single());
        history.Favorites.Should().BeEmpty();
        history.Recent.Should().HaveCount(1);

        // Copy path and open in the file explorer.
        RecordingClipboard clipboard = new();
        dashboard.Clipboard = clipboard;
        DashboardEntry betaEntry = dashboard.Categories.Single().Entries.Single();
        await dashboard.CopyPathCommand.ExecuteAsync(betaEntry);
        clipboard.Text.Should().Be(beta + "/");

        List<string> revealed = [];
        dashboard.RevealFolder = folder =>
        {
            revealed.Add(folder);
            return true;
        };
        dashboard.OpenInExplorerCommand.Execute(betaEntry);
        revealed.Should().Equal(beta + "/");
    }

    [AvaloniaTest]
    public async Task The_entries_have_a_context_menu_with_the_actions()
    {
        string alpha = MakeRepository("alpha", "main");
        FakeHistory history = new();
        history.Recent.Add(new Repository(alpha + "/"));
        MainWindowViewModel viewModel = CreateViewModel(history);
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.Dashboard.LoadAsync();
        ShellTestSupport.Settle();

        DashboardView view = window.Find<DashboardView>("DashboardPage");
        ListBoxItem row = view.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext is DashboardEntry);
        row.RaiseEvent(new ContextRequestedEventArgs());
        ShellTestSupport.Settle();
        ContextMenu menu = view.ContextMenu!;
        menu.Items.OfType<Control>().Select(GitExtensions.Desktop.Views.ContextMenus.MenuKit.TextOf).Should().Equal(
            "Open", "Open in file explorer", "Open in terminal", "-", "Category…", "Pin to top", "Add to favorites", "-", "Remove from list", "Copy path");
        menu.Close();

        // The create / open / clone links are wired to the existing dialogs.
        window.Find<Button>("CloneButton").Command!.Execute(null);
        viewModel.Clone.IsOpen.Should().BeTrue();
        viewModel.Clone.CancelCommand.Execute(null);
        window.Find<Button>("CreateButton").Command!.Execute(null);
        viewModel.Init.IsOpen.Should().BeTrue();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Left_panel_shows_the_counts_and_the_submodule_and_stash_groups()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "stash me");
        repository.RunGit("stash", "push", "-m", "work in progress");
        FakeHistory history = new();
        MainWindowViewModel viewModel = CreateViewModel(history);
        await viewModel.OpenAsync(repository.Path);

        string[] headers = [.. viewModel.Refs.Roots.Select(r => r.Header)];
        headers.Should().Equal("Branches (3)", "Remotes", "Tags (2)", "Stashes (1)");
        viewModel.Refs.Roots.Single(r => r.Name == "Stashes").Children.Single().Name.Should().Contain("work in progress");

        // The counts follow the search box.
        viewModel.Refs.Filter = "feature";
        viewModel.Refs.Roots.Select(r => r.Header).Should().Equal("Branches (1)", "Remotes");
    }

    [AvaloniaTest]
    public async Task Screenshots_of_the_dashboard_and_the_browse_page()
    {
        string[] names = ["gitextensions", "avalonia-playground", "dotfiles", "website", "notes"];
        string[] branches = ["master", "feature/dashboard", "main", "gh-pages", "main"];
        FakeHistory history = new();
        for (int i = 0; i < names.Length; i++)
        {
            history.Recent.Add(new Repository(MakeRepository(names[i], branches[i]) + Path.DirectorySeparatorChar));
        }

        history.Favorites.Add(new Repository(history.Recent[0].Path) { Category = DashboardViewModel.DefaultFavoriteCategory });
        history.Favorites.Add(new Repository(history.Recent[2].Path) { Category = "Work" });

        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        repository.AddRichHistory();

        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        foreach ((int width, int height) in new[] { (1200, 720), (900, 600) })
        {
            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                string suffix = $"{width}x{height}-{variant.Key.ToString()!.ToLowerInvariant()}";
                MainWindowViewModel viewModel = CreateViewModel(history);
                MainWindow window = new() { DataContext = viewModel, Width = width, Height = height, RequestedThemeVariant = variant };
                window.Show();
                await viewModel.Dashboard.LoadAsync();
                await WaitUntilAsync(() => viewModel.Dashboard.Categories.SelectMany(c => c.Entries).All(e => e.HasBranch));
                ShellTestSupport.Settle();
                window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"dashboard-{suffix}.png"));

                await viewModel.OpenAsync(repository.Path);
                viewModel.SelectedCommit = viewModel.Commits[0];
                await WaitUntilAsync(() => viewModel.CommitDetails.HasCommit);
                ShellTestSupport.Settle();
                window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"browse-{suffix}.png"));
                window.Close();
            }
        }
    }
}
