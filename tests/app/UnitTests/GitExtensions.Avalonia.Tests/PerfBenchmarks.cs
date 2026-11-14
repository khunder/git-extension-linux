using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.FileHistory;
using GitExtensions.Desktop.History;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

/// <summary>
///  Large-repository benchmarks. Excluded from the normal run ([Explicit]); see docs/avalonia-performance.md.
///  Set GE_PERF_DATA to the folder holding the generated <c>graph</c>, <c>wide</c> and <c>big</c> repositories.
/// </summary>
[TestFixture]
[Explicit("Large-repository benchmarks: set GE_PERF_DATA and run with --filter Category=Benchmark")]
[Category("Benchmark")]
public class PerfBenchmarks
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static string DataDir => Environment.GetEnvironmentVariable("GE_PERF_DATA") ?? throw new InvalidOperationException("GE_PERF_DATA is not set.");

    private static void Report(string name, string value)
    {
        string line = $"{name}: {value}";
        TestContext.Progress.WriteLine("PERF " + line);
        File.AppendAllText(Path.Combine(DataDir, "results.txt"), line + Environment.NewLine);
    }

    private static string Memory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return $"managed {GC.GetTotalMemory(true) / 1048576} MB, working set {Process.GetCurrentProcess().WorkingSet64 / 1048576} MB";
    }

    private static MainWindowViewModel Create(int limit)
    {
        BehaviorSettings.Current = new BehaviorSettings(Path.Combine(Path.GetTempPath(), "ge-perf-behavior-" + Guid.NewGuid().ToString("N") + ".json"));
        BehaviorSettings.Current.SetHistoryLimit(limit);
        return new MainWindowViewModel(GitEnvironment.Initialize(), new NoFolderPicker());
    }

    [AvaloniaTest]
    [TestCase(2000)]
    [TestCase(10000)]
    public async Task Open_branchy_history(int limit)
    {
        MainWindowViewModel vm = Create(limit);
        Stopwatch sw = Stopwatch.StartNew();
        TimeSpan? first = null;
        int notifications = 0;
        vm.Commits.CollectionChanged += (_, _) =>
        {
            notifications++;
            if (vm.Commits.Count > 0)
            {
                first ??= sw.Elapsed;
            }
        };
        TimeSpan? branchAt = null;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Branch) && vm.Branch is not null)
            {
                branchAt ??= sw.Elapsed;
            }
        };

        await vm.OpenAsync(Path.Combine(DataDir, "graph"));
        TimeSpan total = sw.Elapsed;
        Report($"graph open limit={limit}", $"branch+changes {branchAt?.TotalMilliseconds:0} ms, first history batch {first?.TotalMilliseconds:0} ms, full {total.TotalMilliseconds:0} ms, {vm.Commits.Count} commits, {notifications} CollectionChanged events, refs roots {vm.Refs.Roots.Count}");
        Report($"graph open limit={limit} memory", Memory());
    }

    [AvaloniaTest]
    public async Task Graph_build_time()
    {
        MainWindowViewModel vm = Create(10000);
        await vm.OpenAsync(Path.Combine(DataDir, "graph"));
        List<(string, List<string>)> rows = [.. vm.Commits.Select(c => (c.ObjectId.ToString(), (c.Revision.ParentIds ?? []).Select(p => p.ToString()).ToList()))];
        CommitGraphBuilder builder = new();
        Stopwatch sw = Stopwatch.StartNew();
        foreach ((string id, List<string> parents) in rows)
        {
            builder.Append(id, parents);
        }

        Report("graph build 10000 rows", $"{sw.Elapsed.TotalMilliseconds:0} ms, max lanes {builder.MaxLaneCount}");
    }

    [AvaloniaTest]
    public async Task Open_wide_working_tree()
    {
        MainWindowViewModel vm = Create(2000);
        Stopwatch sw = Stopwatch.StartNew();
        await vm.OpenAsync(Path.Combine(DataDir, "wide"));
        Report("wide open (20k files, 2k changes)", $"{sw.Elapsed.TotalMilliseconds:0} ms, {vm.ChangedFiles.Count} changes");

        GitModule module = new(GitEnvironment.Initialize().ExecutorProvider, Path.Combine(DataDir, "wide"));
        sw.Restart();
        int count = module.GetAllChangedFiles().Count;
        Report("GetAllChangedFiles 2000 changes", $"{sw.Elapsed.TotalMilliseconds:0} ms ({count})");
        Report("wide memory", Memory());
    }

    [AvaloniaTest]
    public void Refs_tree_load_and_filter()
    {
        List<RefEntry> entries = [.. Enumerable.Range(0, 1000).Select(i => new RefEntry(RefNodeKind.LocalBranch, $"feature/area{i % 40}/topic-{i}"))
            .Concat(Enumerable.Range(0, 1000).Select(i => new RefEntry(RefNodeKind.Tag, $"v{i / 100}.{i % 100}")))
            .Concat(Enumerable.Range(0, 1000).Select(i => new RefEntry(RefNodeKind.RemoteBranch, $"origin/release/{i}")))];
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
        {
            RefsTreeBuilder.Build(entries, "main", string.Empty);
        }

        Report("refs tree build 3000 refs (avg of 10)", $"{sw.Elapsed.TotalMilliseconds / 10:0.0} ms");
    }

    [AvaloniaTest]
    public async Task Diff_of_10000_lines_and_big_files()
    {
        string repo = Path.Combine(DataDir, "big");
        MainWindowViewModel vm = Create(2000);
        await vm.OpenAsync(repo);
        foreach (string name in new[] { "small.txt", "big.txt", "big.bin", "longline.txt", "nul.txt" })
        {
            ChangedFileViewModel? file = vm.ChangedFiles.FirstOrDefault(f => f.Name == name);
            if (file is null)
            {
                Report("diff " + name, "not in the change list");
                continue;
            }

            Stopwatch sw = Stopwatch.StartNew();
            int events = 0;
            vm.Diff!.Lines.CollectionChanged += (_, _) => events++;
            await vm.Diff!.ShowAsync(file);
            Report("diff " + name, $"{sw.Elapsed.TotalMilliseconds:0} ms, {vm.Diff.Lines.Count} lines, {events} CollectionChanged events, message '{vm.Diff.Message}'");
        }

        Report("diff memory", Memory());
    }

    [AvaloniaTest]
    public async Task Blame_of_200k_lines()
    {
        GitModule module = new(GitEnvironment.Initialize().ExecutorProvider, Path.Combine(DataDir, "big"));
        BlameViewModel vm = new(() => module, null);
        Stopwatch sw = Stopwatch.StartNew();
        await vm.LoadAsync("big.txt", "HEAD");
        Report("blame big.txt (200k lines)", $"{sw.Elapsed.TotalMilliseconds:0} ms, {vm.Lines.Count} rows, message '{vm.Message}'");
        Report("blame memory", Memory());
    }

    [AvaloniaTest]
    public async Task Switching_repositories_mid_load_leaves_no_git_processes()
    {
        MainWindowViewModel vm = Create(50000);
        string[] repos = [Path.Combine(DataDir, "graph"), Path.Combine(DataDir, "wide"), Path.Combine(DataDir, "big")];
        List<Task> tasks = [];
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(vm.OpenAsync(repos[i % 3]));
            await Task.Delay(30);
        }

        await Task.WhenAll(tasks);
        await Task.Delay(1000);
        int git = Process.GetProcessesByName("git").Length;
        Report("git processes after 20 mid-load switches", $"{git}; commits {vm.Commits.Count}; repo {vm.RepositoryPath}");
    }

    [AvaloniaTest]
    [TestCase("graph", 10000)]
    public async Task Rendered_open_and_diff(string repo, int limit)
    {
        MainWindowViewModel vm = Create(limit);
        MainWindow window = new() { DataContext = vm };
        window.Show();
        Stopwatch sw = Stopwatch.StartNew();
        await vm.OpenAsync(Path.Combine(DataDir, repo));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Report($"rendered open {repo} limit={limit}", $"{sw.Elapsed.TotalMilliseconds:0} ms to loaded + laid out + rendered, list items {window.FindControl<RevisionGridView>("RevisionGrid")!.List.ItemCount}");

        string big = Path.Combine(DataDir, "big");
        await vm.OpenAsync(big);
        foreach (string name in new[] { "small.txt", "big.txt", "longline.txt" })
        {
            ChangedFileViewModel file = vm.ChangedFiles.First(f => f.Name == name);
            sw.Restart();
            vm.SelectedChangedFile = file;
            await vm.Diff!.ShowAsync(file);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Report($"rendered diff {name}", $"{sw.Elapsed.TotalMilliseconds:0} ms to loaded + laid out + rendered, {vm.Diff.Lines.Count} lines");
        }

        window.Close();
    }

    [AvaloniaTest]
    public async Task Probe_diff_virtualization()
    {
        MainWindowViewModel vm = Create(2000);
        MainWindow window = new() { DataContext = vm };
        window.Show();
        await vm.OpenAsync(Path.Combine(DataDir, "big"));
        ChangedFileViewModel file = vm.ChangedFiles.First(f => f.Name == "small.txt");
        vm.SelectedChangedFile = file;
        Stopwatch sw = Stopwatch.StartNew();
        await vm.Diff!.ShowAsync(file);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        DiffView view = window.GetVisualDescendants().OfType<DiffView>().First();
        ItemsControl lines = view.FindControl<ItemsControl>("DiffLines")!;
        int realized = lines.GetVisualDescendants().OfType<Border>().Count();
        Report("probe diff", $"{sw.Elapsed.TotalMilliseconds:0} ms; DiffView bounds {view.Bounds}; ItemsControl bounds {lines.Bounds}; realized borders {realized}; window {window.Bounds}");
        window.Close();
    }
}
