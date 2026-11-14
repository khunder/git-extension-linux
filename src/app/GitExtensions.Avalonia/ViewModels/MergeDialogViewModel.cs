using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Merge branches" dialog (FormMergeBranch): the branch or commit to merge into the current branch, the fast-forward mode,
///  squash / no commit, the strategy and a custom message. Conflicts hand over to the operation bar.
/// </summary>
public sealed partial class MergeDialogViewModel : GitDialogViewModelBase
{
    /// <summary>The strategy entry that leaves the choice to git.</summary>
    public const string DefaultStrategy = "(default)";

    [ObservableProperty]
    private string _target = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFastForward), nameof(IsNoFastForward), nameof(IsFastForwardOnly))]
    private MergeMode _mode = MergeMode.Default;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFastForward))]
    private bool _squash;

    [ObservableProperty]
    private bool _noCommit;

    [ObservableProperty]
    private string _strategy = DefaultStrategy;

    [ObservableProperty]
    private string _message = string.Empty;

    public MergeDialogViewModel(Func<GitModule?> getModule, Func<Task> refresh, Action<string> reportStatus)
        : base(getModule, refresh, reportStatus)
    {
    }

    /// <summary>Gets the merge strategies of <c>git merge -s</c>; the first entry is git's default.</summary>
    public IReadOnlyList<string> Strategies { get; } = [DefaultStrategy, "resolve", "recursive", "ort", "octopus", "ours", "subtree"];

    public bool IsFastForward
    {
        get => Mode == MergeMode.Default;
        set => SetMode(value, MergeMode.Default);
    }

    public bool IsNoFastForward
    {
        get => Mode == MergeMode.NoFastForward;
        set => SetMode(value, MergeMode.NoFastForward);
    }

    public bool IsFastForwardOnly
    {
        get => Mode == MergeMode.FastForwardOnly;
        set => SetMode(value, MergeMode.FastForwardOnly);
    }

    /// <summary>A squash merge never fast-forwards or creates a merge commit, so the mode does not apply.</summary>
    public bool CanChooseFastForward => !Squash;

    /// <summary>Opens the dialog; <paramref name="target"/> (a branch or commit) is prefilled.</summary>
    public async Task OpenAsync(string? target = null)
    {
        Target = target ?? string.Empty;
        Mode = MergeMode.Default;
        Squash = false;
        NoCommit = false;
        Strategy = DefaultStrategy;
        Message = string.Empty;
        BeginOpen();
        await LoadRefsAsync();
    }

    /// <summary>Gets the arguments of the merge for the current options.</summary>
    public string[] BuildArguments(string target)
    {
        List<string> args = ["merge"];
        if (Squash)
        {
            args.Add("--squash");
        }
        else if (Mode == MergeMode.NoFastForward)
        {
            args.Add("--no-ff");
        }
        else if (Mode == MergeMode.FastForwardOnly)
        {
            args.Add("--ff-only");
        }

        if (NoCommit && !Squash)
        {
            args.Add("--no-commit");
        }

        if (Strategy != DefaultStrategy && Strategy.Length > 0)
        {
            args.Add("--strategy=" + Strategy);
        }

        if (!Squash && Message.Trim().Length > 0)
        {
            args.Add("-m");
            args.Add(Message.Trim());
        }
        else
        {
            args.Add("--no-edit");
        }

        args.Add(target);
        return [.. args];
    }

    [RelayCommand]
    private Task MergeAsync()
        => GuardedAsync(async module =>
        {
            if (await ResolveAsync(module, Target, "the branch to merge") is null)
            {
                return;
            }

            string target = Target.Trim();
            await CompleteAsync(module, await ExecAsync(module, BuildArguments(target)), Squash ? $"Squashed {target} into the working tree; commit to finish." : $"Merged {target}", "Merge");
        });

    private void SetMode(bool selected, MergeMode mode)
    {
        if (selected)
        {
            Mode = mode;
        }
    }
}
