using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Rebase" dialog (FormRebase): rebases the current branch on a branch or commit, optionally only a range
///  (<c>--onto</c>), with preserve merges, autosquash, autostash and date options. Conflicts hand over to the operation bar.
/// </summary>
public sealed partial class RebaseDialogViewModel : GitDialogViewModelBase
{
    [ObservableProperty]
    private string _target = string.Empty;

    [ObservableProperty]
    private bool _rebaseMerges;

    [ObservableProperty]
    private bool _autoSquash;

    [ObservableProperty]
    private bool _autoStash;

    [ObservableProperty]
    private bool _ignoreDate;

    [ObservableProperty]
    private bool _committerDateIsAuthorDate;

    [ObservableProperty]
    private bool _specificRange;

    [ObservableProperty]
    private string _rangeFrom = string.Empty;

    [ObservableProperty]
    private string _rangeTo = string.Empty;

    public RebaseDialogViewModel(Func<GitModule?> getModule, Func<Task> refresh, Action<string> reportStatus)
        : base(getModule, refresh, reportStatus)
    {
    }

    /// <summary>Interactive rebase needs an editor for the todo list, which the app does not have yet.</summary>
    public bool CanInteractive => false;

    public string InteractiveToolTip => "Interactive rebase is not available yet";

    /// <summary>Opens the dialog; <paramref name="target"/> (a branch or commit) is prefilled.</summary>
    public async Task OpenAsync(string? target = null)
    {
        Target = target ?? string.Empty;
        RebaseMerges = false;
        AutoSquash = false;
        AutoStash = false;
        IgnoreDate = false;
        CommitterDateIsAuthorDate = false;
        SpecificRange = false;
        RangeFrom = string.Empty;
        RangeTo = string.Empty;
        BeginOpen();
        await LoadRefsAsync();
    }

    /// <summary>Gets the arguments of the rebase for the current options.</summary>
    public string[] BuildArguments(string target)
    {
        // Autosquash only applies to an interactive rebase (before git 2.44); the todo editor is "true", so nothing is asked.
        List<string> args = ["rebase"];
        if (AutoSquash)
        {
            args.Add("--interactive");
            args.Add("--autosquash");
        }

        if (RebaseMerges)
        {
            args.Add("--rebase-merges");
        }

        if (AutoStash)
        {
            args.Add("--autostash");
        }

        if (IgnoreDate)
        {
            args.Add("--ignore-date");
        }

        if (CommitterDateIsAuthorDate)
        {
            args.Add("--committer-date-is-author-date");
        }

        if (SpecificRange)
        {
            args.Add("--onto");
            args.Add(target);
            args.Add(RangeFrom.Trim());
            if (RangeTo.Trim().Length > 0)
            {
                args.Add(RangeTo.Trim());
            }
        }
        else
        {
            args.Add(target);
        }

        return [.. args];
    }

    [RelayCommand]
    private Task RebaseAsync()
        => GuardedAsync(async module =>
        {
            if (await ResolveAsync(module, Target, "the branch to rebase on") is null)
            {
                return;
            }

            if (SpecificRange)
            {
                if (await ResolveAsync(module, RangeFrom, "the first commit not to rebase (From)") is null
                    || (RangeTo.Trim().Length > 0 && await ResolveAsync(module, RangeTo, "the last commit to rebase (To)") is null))
                {
                    return;
                }
            }

            string target = Target.Trim();
            await CompleteAsync(module, await ExecAsync(module, BuildArguments(target)), $"Rebased onto {target}", "Rebase");
        });
}
