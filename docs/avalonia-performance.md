# Avalonia UI: performance and robustness

## Harness

- `tests/app/UnitTests/GitExtensions.Avalonia.Tests/PerfData/gen-graph.py` and `gen-wide-and-big.py` build the synthetic
  repositories with `git fast-import` (a 50,500-commit branchy history with ~1,000 branches, ~1,800 refs and ~500 merges;
  a 20,000-file tree with 2,000 changes; a repo with a 5 MB / 200k-line text file, a 30 MB binary, a 10,000-line diff
  (20,005 diff lines), a 3 MB single-line file and a file with NUL/invalid UTF-8 bytes).
- `PerfBenchmarks.cs` holds the measurements. They are `[Explicit]` and `[Category("Benchmark")]`, so the normal run skips them.

```
D=/some/folder
python3 PerfData/gen-graph.py $D        # creates $D/graph
python3 PerfData/gen-wide-and-big.py $D # creates $D/wide and $D/big
cd tests/app/UnitTests/GitExtensions.Avalonia.Tests && dotnet build
GE_PERF_DATA=$D dotnet test --no-build --filter "Category=Benchmark"   # results are appended to $D/results.txt
```

Numbers are from a 20-core Linux box, Debug build, headless Avalonia (Skia, no GPU). "Rendered" means loaded, laid out
and rendered once by the headless platform.

## Measurements (before -> after)

| Scenario | Before | After |
| --- | --- | --- |
| Open, branch + changes ready | 33 ms | 32 ms |
| First history batch (50k-commit repo, `--topo-order`) | 172 ms | 181 ms (git itself) |
| Full 2,000-commit history | 221 ms, 2,001 collection events | 213 ms, 3 events |
| Full 10,000-commit history | 439 ms, 10,001 events | 323 ms, 3 events |
| Managed memory after 10,000 commits | 72 MB | 16 MB |
| Working set after 10,000 commits | 255 MB | 192 MB |
| Graph build, 10,000 rows (496 lanes) | 162 ms | about 184 ms (on the reader thread now, not the UI thread) |
| Refs tree build, 3,000 refs | 3.5 ms | 2.9 ms (debounced above 500 refs) |
| `GetAllChangedFiles`, 2,000 changes | 23 ms | 16 ms |
| Open wide repo (20k files, 2k changes) | 67 ms | 42 ms |
| Rendered open, 10,000 commits | 1,986 ms | 1,661 ms |
| **Rendered diff, 20,005 lines** | **13,835 ms** (40,022 rows created) | **61 ms** (40 rows) |
| **Rendered diff, 100,011 lines** | **202,782 ms** | **250 ms** (capped to 50,000 lines + notice) |
| Rendered diff, 3 MB single line | not finished in 10+ minutes | 49 ms |
| Blame 200k lines | 397 ms, 69 MB managed | 314 ms, 49 MB managed |
| git processes left after 20 mid-load repo switches | 0 | 0 |

## What was wrong, and the fixes

1. **Diff viewers were not virtualized.** The custom `ItemsControl` template in `DiffView`, `StashView` and
   `FileHistoryView` used `<ItemsPresenter />` without `ItemsPanel="{TemplateBinding ItemsPanel}"`, so the declared
   `VirtualizingStackPanel` was ignored and every line became controls. This was the dominant cost.
2. Items were added to `ObservableCollection`s one by one. New `RangeObservableCollection` (`AddRange`, `ReplaceAll`) for
   commits, changed files and diff lines: one event per batch.
3. Graph lanes and commit row view-models are computed on the git reader thread, one `CommitGraphBuilder` per load.
   Pass-through segments of lanes at or beyond the 12th (all drawn in one column anyway) are collapsed to one, which
   removed most of the per-row memory with hundreds of open branches.
4. Diff guards (`Diff/DiffLimits.cs`): at most 50,000 lines (with a notice line; partial staging is disabled then), lines drawn
   truncated to 2,000 characters (`DiffLine.DisplayText`), control characters shown as U+FFFD. Blame is capped at
   250,000 lines with a notice row; blame tooltips are built lazily.
5. Refs filter is debounced (150 ms) when there are more than 500 refs; small trees still update immediately.
6. Opening a repository could apply stale results of an older open (a started `Task.Run` ignores its cancellation token).
   `OpenAsync` now re-checks cancellation after each await.
7. Staging a file whose name contains a quote, backslash or tab made `git update-index --stdin` reject the whole list
   (core bug in `GitModule.UpdateIndex`; fixed with C-style quoting, test in `GitModuleTests`).
8. Failures were silent: a failed `git status` (dubious ownership, bare repository, index.lock) appeared as a changed file
   named "Git Error"; staging errors were ignored; `RefreshChangesAsync` threw when the folder vanished. Now they show in the
   error banner with an explanation (`Services/GitErrorMessages.cs`), and `App` turns unhandled UI-thread exceptions into
   the banner instead of terminating.

## Robustness scenarios covered by `RobustnessTests`

empty repository, detached HEAD, shallow clone, bare repository, linked worktree, submodule (inside and as superproject),
names with spaces/unicode/quote/backslash/tab/leading dash, symlinked path, folder deleted while open, vanished path,
different owner (simulated with `GIT_TEST_ASSUME_DIFFERENT_OWNER=1`), `index.lock` present when opening and when staging.

## Not fixed / known limits

- `--topo-order` makes git walk the whole history before the first commit is listed (about 180 ms for 50k commits; grows with size).
- A huge text diff is still read completely into memory (twice: parsed and raw) before it is truncated for display.
- History errors from `git log` itself are not reported (the core reader ignores stderr); status errors are.
- An untracked FIFO/device file in the working tree is not special-cased.
- Layout of `MainWindow.axaml` was not touched; the 10,000 commit list is virtualized by the default `ListBox`.
