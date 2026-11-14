using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.FileHistory;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  One displayed blame row. Commit info is only shown on the first row of a block of consecutive lines of one commit.
/// </summary>
public sealed class BlameLineViewModel
{
    public BlameLineViewModel(BlameEntry entry, bool isBlockStart, bool isAlternate)
    {
        Entry = entry;
        IsBlockStart = isBlockStart;
        IsAlternate = isAlternate;
        LineNumber = entry.LineNumber;
        Text = entry.Text;
        ShortHash = isBlockStart ? (entry.CommitHash.Length > 8 ? entry.CommitHash[..8] : entry.CommitHash) : string.Empty;
        Author = isBlockStart ? entry.Author : string.Empty;
        Date = isBlockStart ? entry.Date.ToString("d", CultureInfo.CurrentCulture) : string.Empty;
    }

    public BlameEntry Entry { get; }

    public int LineNumber { get; }

    public string Text { get; }

    public bool IsBlockStart { get; }

    /// <summary>Alternates per commit block, so neighbouring blocks get different backgrounds.</summary>
    public bool IsAlternate { get; }

    public string ShortHash { get; }

    public string Author { get; }

    public string Date { get; }

    /// <summary>Gets the text to draw: truncated for very long lines, control characters made visible.</summary>
    public string DisplayText => DiffLimits.ToDisplayText(Text);

    // Built on demand: a blame has one row per line of the file, but only hovered rows need a tooltip.
    public string ToolTip => $"{Entry.CommitHash[..Math.Min(8, Entry.CommitHash.Length)]}  {Entry.Author}  {Entry.Date:g}\n{Entry.Summary}";
}

/// <summary>
///  The annotated content of a file: for every line the commit, author and date that last changed it.
/// </summary>
public sealed partial class BlameViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly Action<ObjectId>? _commitSelected;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    private IReadOnlyList<BlameLineViewModel> _lines = [];

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private BlameLineViewModel? _selectedLine;

    public BlameViewModel(Func<GitModule?> getModule, Action<ObjectId>? commitSelected)
    {
        _getModule = getModule;
        _commitSelected = commitSelected;
    }

    /// <summary>
    ///  Groups consecutive lines of the same commit into blocks.
    /// </summary>
    public static IReadOnlyList<BlameLineViewModel> CreateRows(IReadOnlyList<BlameEntry> entries)
    {
        List<BlameLineViewModel> rows = new(entries.Count);
        bool alternate = true;
        string? previous = null;
        foreach (BlameEntry entry in entries)
        {
            bool blockStart = entry.CommitHash != previous;
            if (blockStart)
            {
                alternate = !alternate;
            }

            rows.Add(new BlameLineViewModel(entry, blockStart, alternate));
            previous = entry.CommitHash;
        }

        return rows;
    }

    /// <summary>
    ///  Loads the blame of <paramref name="path"/> at <paramref name="revision"/>; a load started earlier is cancelled.
    /// </summary>
    public async Task LoadAsync(string path, string revision)
    {
        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _cancellation = cancellation;
        CancellationToken cancellationToken = cancellation.Token;

        SelectedLine = null;
        Lines = [];
        GitModule? module = _getModule();
        if (module is null)
        {
            Message = "No repository is open.";
            return;
        }

        IsLoading = true;
        Message = "Loading blame…";
        try
        {
            // git blocks, so keep it off the UI thread.
            (IReadOnlyList<BlameLineViewModel> rows, string message) = await Task.Run(
                () =>
                {
                    BlameResult result = FileHistoryGit.GetBlame(module, path, revision, cancellationToken);
                    return (CreateRows(result.Entries), result.Message);
                },
                cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Lines = rows;
            Message = message;
        }
        catch (OperationCanceledException)
        {
            // A newer load replaced this one.
        }
        catch (Exception ex)
        {
            Message = $"Blame failed: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    public Task CancelAsync() => _cancellation?.CancelAsync() ?? Task.CompletedTask;

    partial void OnSelectedLineChanged(BlameLineViewModel? value)
    {
        if (value is not null && ObjectId.TryParse(value.Entry.CommitHash, out ObjectId id) && id != ObjectId.WorkTreeId && !value.Entry.CommitHash.All(c => c == '0'))
        {
            _commitSelected?.Invoke(id);
        }
    }
}
