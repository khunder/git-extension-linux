using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  Bisect: start, mark good, bad or skip, and reset, with the live state of the session. Each step shows what git said (how many
///  revisions are left, or the first bad commit). Reset asks for an inline confirmation.
/// </summary>
public sealed partial class BisectViewModel : ObservableObject
{
    private readonly ToolsContext _context;
    private readonly Func<ObjectId?> _getSelected;
    private readonly Action<BisectState> _stateChanged;
    private int _loadVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(CanMark))]
    private bool _isBisecting;

    [ObservableProperty]
    private string _stateText = "Not bisecting.";

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFirstBad))]
    private string _firstBadText = string.Empty;

    [ObservableProperty]
    private string _badRevision = string.Empty;

    [ObservableProperty]
    private string _goodRevision = string.Empty;

    [ObservableProperty]
    private bool _markSelectedCommit;

    [ObservableProperty]
    private string _selectedText = string.Empty;

    [ObservableProperty]
    private bool _isResetPromptVisible;

    [ObservableProperty]
    private bool _isBusy;

    public BisectViewModel(ToolsContext context, Func<ObjectId?> getSelected, Action<BisectState> stateChanged)
    {
        _context = context;
        _getSelected = getSelected;
        _stateChanged = stateChanged;
    }

    public string Title => "Bisect";

    public bool CanStart => !IsBisecting;

    public bool CanMark => IsBisecting;

    public bool HasFirstBad => FirstBadText.Length > 0;

    public BisectState State { get; private set; } = BisectState.None;

    /// <summary>Called when the dialog opens and whenever the selected commit may have changed.</summary>
    public void UpdateSelection()
        => SelectedText = _getSelected() is { } id ? $"Selected commit: {id.ToShortString()}" : "No commit selected.";

    /// <summary>Reads the session state off the UI thread and publishes it (the grid marks follow).</summary>
    public async Task ReloadStateAsync()
    {
        GitModule? module = _context.Module;
        int version = ++_loadVersion;
        BisectState state = BisectState.None;
        if (module is not null)
        {
            try
            {
                state = await Task.Run(() =>
                {
                    if (!module.InTheMiddleOfBisect())
                    {
                        return BisectState.None;
                    }

                    ExecutionResult refs = module.GitExecutable.Execute("for-each-ref --format=\"%(objectname) %(refname)\" refs/bisect", throwOnErrorExit: false);
                    return BisectStateParser.Parse(true, refs.StandardOutput);
                });
            }
            catch (Exception ex)
            {
                _context.ReportError($"Could not read the bisect state: {ex.Message}");
            }
        }

        if (version != _loadVersion)
        {
            return;
        }

        State = state;
        IsBisecting = state.IsActive;
        StateText = state.Summary;
        if (!state.IsActive)
        {
            IsResetPromptVisible = false;
            FirstBadText = string.Empty;
        }

        _stateChanged(state);
        UpdateSelection();
    }

    [RelayCommand]
    private void UseSelectedAsBad()
    {
        if (_getSelected() is { } id)
        {
            BadRevision = id.ToString();
        }
    }

    [RelayCommand]
    private void UseSelectedAsGood()
    {
        if (_getSelected() is { } id)
        {
            GoodRevision = id.ToString();
        }
    }

    /// <summary><c>git bisect start</c>, then marks the optional bad and good revisions.</summary>
    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsBisecting)
        {
            return;
        }

        if (!await RunStepAsync("bisect start", reportOutput: false))
        {
            return;
        }

        FirstBadText = string.Empty;
        string bad = BadRevision.Trim();
        string good = GoodRevision.Trim();
        if (bad.Length > 0 && !await RunStepAsync(new GitArgumentBuilder("bisect") { "bad", bad.Quote() }))
        {
            return;
        }

        if (good.Length > 0)
        {
            await RunStepAsync(new GitArgumentBuilder("bisect") { "good", good.Quote() });
        }
    }

    [RelayCommand]
    public Task MarkGoodAsync() => MarkAsync("good");

    [RelayCommand]
    public Task MarkBadAsync() => MarkAsync("bad");

    [RelayCommand]
    public Task SkipAsync() => MarkAsync("skip");

    private async Task MarkAsync(string term)
    {
        if (!IsBisecting)
        {
            return;
        }

        ObjectId? target = MarkSelectedCommit ? _getSelected() : null;
        if (MarkSelectedCommit && target is null)
        {
            _context.ReportError("Select a commit in the history first, or untick \"Mark the selected commit\".");
            return;
        }

        await RunStepAsync(new GitArgumentBuilder("bisect") { term, target?.ToString() });
    }

    [RelayCommand]
    private void RequestReset()
    {
        if (IsBisecting)
        {
            IsResetPromptVisible = true;
        }
    }

    [RelayCommand]
    private void CancelReset() => IsResetPromptVisible = false;

    /// <summary>Ends the session and checks the original branch out again.</summary>
    [RelayCommand]
    public async Task ConfirmResetAsync()
    {
        IsResetPromptVisible = false;
        if (await RunStepAsync("bisect reset"))
        {
            FirstBadText = string.Empty;
        }
    }

    private async Task<bool> RunStepAsync(ArgumentString arguments, bool reportOutput = true)
    {
        GitModule? module = _context.Module;
        if (module is null)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            ExecutionResult result = await _context.RunGitAsync(module, arguments);
            string text = ToolsContext.Describe(result);
            if (!result.ExitedSuccessfully)
            {
                Output = text;
                _context.ReportError($"git bisect failed: {text}");
                return false;
            }

            if (reportOutput || text.Length > 0)
            {
                Output = text;
            }

            if (BisectStateParser.ParseFirstBad(text) is { } first)
            {
                FirstBadText = $"First bad commit: {first.ToShortString()}";
                _context.ReportStatus(FirstBadText);
            }

            // Bisect moves HEAD: reload the history, then the state (so the marks land on the new rows).
            await _context.RefreshAsync();
            await ReloadStateAsync();
            return true;
        }
        catch (Exception ex)
        {
            _context.ReportError($"Git operation failed: {ex.Message}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
