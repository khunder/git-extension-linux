using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.History;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

/// <summary>
///  Regression tests for the performance and robustness fixes. Small and fast; the big-repository measurements
///  live in <see cref="PerfBenchmarks"/> and do not run with the normal suite.
/// </summary>
[TestFixture]
public class PerformanceGuardTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static MainWindowViewModel CreateViewModel() => new(GitEnvironment.Initialize(), new NoFolderPicker());

    [AvaloniaTest]
    public void AddRange_and_ReplaceAll_raise_one_event()
    {
        RangeObservableCollection<int> collection = [];
        List<NotifyCollectionChangedAction> events = [];
        collection.CollectionChanged += (_, e) => events.Add(e.Action);

        collection.AddRange(Enumerable.Range(0, 5000));
        collection.AddRange([1, 2, 3]);
        collection.ReplaceAll(Enumerable.Range(0, 10));
        collection.ReplaceAll([]);

        events.Should().Equal(NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Reset);
        collection.Should().BeEmpty();
    }

    [AvaloniaTest]
    public void ToDisplayText_truncates_long_lines_and_replaces_control_characters()
    {
        DiffLimits.ToDisplayText("plain\ttext").Should().Be("plain\ttext");
        DiffLimits.ToDisplayText("a\0b\u001b").Should().Be("a�b�");

        string huge = new('x', 3 * 1024 * 1024);
        string shown = DiffLimits.ToDisplayText(huge);
        shown.Length.Should().Be(DiffLimits.MaxDisplayedLineLength + 1);
        shown.Should().EndWith("…");
    }

    [AvaloniaTest]
    public void Truncate_limits_the_number_of_diff_lines_and_adds_a_notice()
    {
        IReadOnlyList<DiffLine> lines = [.. Enumerable.Range(0, DiffLimits.MaxDisplayedLines + 10).Select(i => new DiffLine(DiffLineKind.Context, " " + i, i, i))];

        DiffLimits.Truncate(ref lines).Should().BeTrue();

        lines.Should().HaveCount(DiffLimits.MaxDisplayedLines + 1);
        lines[^1].Kind.Should().Be(DiffLineKind.Info);
        lines[^1].Text.Should().Contain("too large");

        IReadOnlyList<DiffLine> small = [new DiffLine(DiffLineKind.Context, " x", 1, 1)];
        DiffLimits.Truncate(ref small).Should().BeFalse();
    }

    [AvaloniaTest]
    public void Graph_builder_keeps_one_pass_segment_for_collapsed_lanes()
    {
        // 40 open branch tips, then one commit that every lane passes by.
        CommitGraphBuilder builder = new() { CollapseFromLane = 11 };
        for (int i = 0; i < 40; i++)
        {
            builder.Append("tip" + i, ["base"]);
        }

        CommitGraphRow row = builder.Append("unrelated", ["parent"]);

        row.Segments.Count(s => s.Kind == GraphSegmentKind.Pass && s.Lane >= 11).Should().Be(1);
        row.Segments.Count(s => s.Kind == GraphSegmentKind.Pass && s.Lane < 11).Should().Be(11);
        builder.MaxLaneCount.Should().BeGreaterThan(40);
    }

    [AvaloniaTest]
    public async Task Open_adds_history_and_changes_in_few_collection_events()
    {
        using TestRepository repository = new();
        for (int i = 0; i < 300; i++)
        {
            repository.RunGit("-c", "user.name=T", "-c", "user.email=t@t", "commit", "--quiet", "--allow-empty", "-m", "c" + i);
        }

        MainWindowViewModel viewModel = CreateViewModel();
        int events = 0;
        viewModel.Commits.CollectionChanged += (_, _) => events++;
        viewModel.ChangedFiles.CollectionChanged += (_, _) => events++;

        await viewModel.OpenAsync(repository.Path);

        viewModel.Commits.Should().HaveCount(302);
        viewModel.Commits.Should().OnlyContain(c => c.Graph != null);
        events.Should().BeLessThan(20, "items are added in batches, not one by one");
    }

    [AvaloniaTest]
    public async Task Switching_repositories_mid_load_never_shows_stale_items()
    {
        using TestRepository first = new();
        using TestRepository second = new();
        second.RunGit("-c", "user.name=T", "-c", "user.email=t@t", "commit", "--quiet", "--allow-empty", "-m", "only in second");
        MainWindowViewModel viewModel = CreateViewModel();

        List<Task> opens = [];
        for (int i = 0; i < 20; i++)
        {
            opens.Add(viewModel.OpenAsync(i % 2 == 0 ? first.Path : second.Path));
            await Task.Delay(i % 4);
        }

        opens.Add(viewModel.OpenAsync(second.Path));
        await Task.WhenAll(opens);

        viewModel.Commits.Select(c => c.Subject).Should().Equal("only in second", "Add b.txt", "Add a.txt");
        viewModel.ChangedFiles.Select(f => f.Name).Should().BeEquivalentTo("a.txt", "c.txt");
        viewModel.IsBusy.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Huge_diff_is_truncated_with_a_notice_and_no_partial_staging()
    {
        using TestRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), string.Join('\n', Enumerable.Range(0, DiffLimits.MaxDisplayedLines + 500).Select(i => "line " + i)) + "\n");
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);

        await viewModel.Diff!.ShowAsync(viewModel.ChangedFiles.Single(f => f.Name == "a.txt"));

        viewModel.Diff.Lines.Should().HaveCount(DiffLimits.MaxDisplayedLines + 1);
        viewModel.Diff.Lines[^1].Text.Should().Contain("too large");
        viewModel.Diff.Lines.Should().NotContain(l => l.CanStageHunk || l.CanDiscardHunk);
    }

    [AvaloniaTest]
    public async Task Diff_of_a_giant_line_and_binary_content_does_not_throw()
    {
        using TestRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), new string('A', 2 * 1024 * 1024) + "\nshort\n");
        File.WriteAllBytes(Path.Combine(repository.Path, "c.txt"), [0x61, 0x00, 0x62, 0xFF, 0xFE, 0x0A]);
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);

        await viewModel.Diff!.ShowAsync(viewModel.ChangedFiles.Single(f => f.Name == "a.txt"));
        viewModel.Diff.Lines.Should().Contain(l => l.Text.Length > 1_000_000);
        viewModel.Diff.Lines.Max(l => l.DisplayText.Length).Should().BeLessThanOrEqualTo(DiffLimits.MaxDisplayedLineLength + 1);
        viewModel.Diff.Message.Should().BeEmpty();

        await viewModel.Diff.ShowAsync(viewModel.ChangedFiles.Single(f => f.Name == "c.txt"));
        viewModel.Diff.Message.Should().NotStartWith("Failed");
    }

    [AvaloniaTest]
    public async Task Refs_filter_is_immediate_for_small_trees()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);

        viewModel.Refs.Filter = "feature";

        viewModel.Refs.Roots.Select(r => r.Name).Should().Equal("Branches", "Remotes");
    }

    [AvaloniaTest]
    public async Task Refs_filter_is_debounced_for_large_trees()
    {
        using TestRepository repository = new();
        string head = repository.GitOutput("rev-parse", "HEAD").Trim();
        string heads = Path.Combine(repository.Path, ".git", "refs", "heads", "topic");
        Directory.CreateDirectory(heads);
        for (int i = 0; i < RefsTreeViewModel.DebounceThreshold + 100; i++)
        {
            File.WriteAllText(Path.Combine(heads, "b" + i), head + "\n");
        }

        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);
        int rebuilds = 0;
        viewModel.Refs.Roots.CollectionChanged += (_, _) => rebuilds++;

        // Typing "b1", "b12", "b123" quickly: nothing is rebuilt until a pause.
        viewModel.Refs.Filter = "b1";
        viewModel.Refs.Filter = "b12";
        viewModel.Refs.Filter = "b123";
        rebuilds.Should().Be(0);

        await Task.Delay(RefsTreeViewModel.DebounceDelay + TimeSpan.FromMilliseconds(400));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        rebuilds.Should().BeGreaterThan(0);
        viewModel.Refs.Roots.Single(r => r.Name == "Branches").Children.Single(c => c.Name == "topic").Children.Select(c => c.Name).Should().Equal("b123");
    }

    [AvaloniaTest]
    public async Task Diff_view_only_creates_rows_for_the_visible_lines()
    {
        // Regression: the custom ItemsControl template ignored ItemsPanel, so all rows were created (15 s for 20,000 lines).
        using TestRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), string.Join('\n', Enumerable.Range(0, 5000).Select(i => "line " + i)) + "\n");
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);
        Window window = ShellTestSupport.CreateChangesHost(viewModel);

        ChangedFileViewModel file = viewModel.ChangedFiles.Single(f => f.Name == "a.txt");
        viewModel.SelectedChangedFile = file;
        await viewModel.Diff!.ShowAsync(file);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        viewModel.Diff.Lines.Count.Should().BeGreaterThan(5000);
        DiffView view = window.GetVisualDescendants().OfType<DiffView>().First();
        view.FindControl<ItemsControl>("DiffLines")!.GetVisualDescendants().OfType<Border>().Count().Should().BeLessThan(400);
        window.Close();
    }
}
