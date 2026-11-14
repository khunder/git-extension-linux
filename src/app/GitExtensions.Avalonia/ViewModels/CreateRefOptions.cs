using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>What happens to uncommitted changes when a new branch is checked out.</summary>
public enum BranchLocalChanges
{
    /// <summary>Plain checkout: git refuses when the changes would be overwritten.</summary>
    Keep,
    Merge,
    Reset,

    /// <summary>Stash the changes, switch, then re-apply them.</summary>
    Stash,
}

/// <summary>The options of the "Create branch" dialogs (the grid's and the refs tree's share them).</summary>
public sealed partial class CreateBranchOptions : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLocalChanges))]
    private bool _checkoutAfterCreate = true;

    [ObservableProperty]
    private BranchLocalChanges _localChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLocalChanges), nameof(CanChooseCheckout))]
    private bool _orphan;

    [ObservableProperty]
    private bool _clearOrphan;

    public bool ShowLocalChanges => CheckoutAfterCreate && !Orphan;

    public bool CanChooseCheckout => !Orphan;

    public bool IsKeep
    {
        get => LocalChanges == BranchLocalChanges.Keep;
        set => Select(value, BranchLocalChanges.Keep);
    }

    public bool IsMerge
    {
        get => LocalChanges == BranchLocalChanges.Merge;
        set => Select(value, BranchLocalChanges.Merge);
    }

    public bool IsReset
    {
        get => LocalChanges == BranchLocalChanges.Reset;
        set => Select(value, BranchLocalChanges.Reset);
    }

    public bool IsStash
    {
        get => LocalChanges == BranchLocalChanges.Stash;
        set => Select(value, BranchLocalChanges.Stash);
    }

    /// <summary>Back to the defaults (checkout after create, keep local changes, no orphan); called when a dialog opens.</summary>
    public void Reset()
    {
        CheckoutAfterCreate = true;
        LocalChanges = BranchLocalChanges.Keep;
        Orphan = false;
        ClearOrphan = false;
    }

    /// <summary>
    ///  Creates the branch (and switches to it when asked) on the calling thread. Returns an explanation of the failure, or
    ///  <see langword="null"/> on success.
    /// </summary>
    public string? Create(GitModule module, string name, string start)
    {
        IExecutable git = module.GitExecutable;
        if (Orphan)
        {
            ExecutionResult orphan = Run(git, new GitArgumentBuilder("checkout") { "--orphan", name.Quote(), { start.Length > 0, start } });
            if (!orphan.ExitedSuccessfully)
            {
                return $"git checkout --orphan failed: {ErrorOf(orphan)}";
            }

            if (ClearOrphan)
            {
                ExecutionResult clear = Run(git, new GitArgumentBuilder("rm") { "-r", "-f", "--ignore-unmatch", "--quiet", "." });
                if (!clear.ExitedSuccessfully)
                {
                    return $"The orphan branch was created, but emptying it failed: {ErrorOf(clear)}";
                }
            }

            return null;
        }

        if (!CheckoutAfterCreate)
        {
            ExecutionResult branch = Run(git, new GitArgumentBuilder("branch") { name.Quote(), start });
            return branch.ExitedSuccessfully ? null : $"git branch failed: {ErrorOf(branch)}";
        }

        bool stashed = false;
        if (LocalChanges == BranchLocalChanges.Stash)
        {
            string before = Run(git, "rev-parse -q --verify refs/stash").StandardOutput.Trim();
            ExecutionResult stash = Run(git, "stash push --include-untracked -m \"Stashed before creating branch\"");
            if (!stash.ExitedSuccessfully)
            {
                return $"Stashing the local changes failed: {ErrorOf(stash)}";
            }

            stashed = Run(git, "rev-parse -q --verify refs/stash").StandardOutput.Trim() != before;
        }

        ExecutionResult checkout = Run(git, new GitArgumentBuilder("checkout")
        {
            { LocalChanges == BranchLocalChanges.Merge, "--merge" },
            { LocalChanges == BranchLocalChanges.Reset, "--force" },
            "-b",
            name.Quote(),
            start,
        });
        if (!checkout.ExitedSuccessfully)
        {
            if (stashed)
            {
                Run(git, "stash pop --quiet");
            }

            return $"git checkout failed: {ErrorOf(checkout)}";
        }

        if (stashed)
        {
            ExecutionResult pop = Run(git, "stash pop --quiet");
            if (!pop.ExitedSuccessfully)
            {
                return $"The branch was created and checked out, but re-applying the stashed changes failed (they are still in the stash): {ErrorOf(pop)}";
            }
        }

        return null;
    }

    internal static ExecutionResult Run(IExecutable git, ArgumentString arguments) => git.Execute(arguments, throwOnErrorExit: false);

    internal static string ErrorOf(ExecutionResult result)
        => (string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim();

    partial void OnLocalChangesChanged(BranchLocalChanges value)
    {
        OnPropertyChanged(nameof(IsKeep));
        OnPropertyChanged(nameof(IsMerge));
        OnPropertyChanged(nameof(IsReset));
        OnPropertyChanged(nameof(IsStash));
    }

    private void Select(bool selected, BranchLocalChanges mode)
    {
        if (selected)
        {
            LocalChanges = mode;
        }
    }
}

/// <summary>The options of the "Create tag" dialogs: message (annotated), signing and pushing the new tag.</summary>
public sealed partial class CreateTagOptions : ObservableObject
{
    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _sign;

    [ObservableProperty]
    private bool _pushAfterCreate;

    [ObservableProperty]
    private string? _selectedRemote;

    public ObservableCollection<string> Remotes { get; } = [];

    public bool HasRemotes => Remotes.Count > 0;

    public string RemotesNote => HasRemotes ? string.Empty : "This repository has no remotes to push to.";

    /// <summary>Back to the defaults and the given remotes (origin, or the first one, preselected).</summary>
    public void Reset(IReadOnlyList<string> remotes)
    {
        Message = string.Empty;
        Sign = false;
        PushAfterCreate = false;
        Remotes.Clear();
        foreach (string remote in remotes)
        {
            Remotes.Add(remote);
        }

        SelectedRemote = remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
        OnPropertyChanged(nameof(HasRemotes));
        OnPropertyChanged(nameof(RemotesNote));
    }

    /// <summary>The remote to push to, or null when pushing is off or there is nothing to push to.</summary>
    public string? PushTarget => PushAfterCreate && HasRemotes ? SelectedRemote : null;

    /// <summary>
    ///  Creates the tag (annotated when there is a message, signed when asked) on the calling thread. Returns an explanation
    ///  of the failure, or <see langword="null"/> on success.
    /// </summary>
    public string? Create(GitModule module, string name, string start)
    {
        string text = Message.Trim();
        if (Sign && text.Length == 0)
        {
            return "A signed tag needs a message.";
        }

        if (text.Length == 0)
        {
            ExecutionResult light = CreateBranchOptions.Run(module.GitExecutable, new GitArgumentBuilder("tag") { name.Quote(), start });
            return light.ExitedSuccessfully ? null : $"git tag failed: {CreateBranchOptions.ErrorOf(light)}";
        }

        // The message goes through a file: no quoting problems with quotes or line breaks.
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, text + "\n");
            ExecutionResult annotated = CreateBranchOptions.Run(module.GitExecutable, new GitArgumentBuilder("tag") { Sign ? "-s" : "-a", name.Quote(), "-F", file.Quote(), start });
            return annotated.ExitedSuccessfully ? null : $"git tag failed: {CreateBranchOptions.ErrorOf(annotated)}";
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort cleanup of a temp file.
            }
        }
    }
}
