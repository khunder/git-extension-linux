using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  "Go to commit": a revision expression (anything <c>git rev-parse</c> understands) or a branch or tag picked from the lists.
///  A bad expression is explained inline and the dialog stays open.
/// </summary>
public sealed partial class GoToCommitViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly Func<ObjectId, Task<string?>> _goTo;
    private readonly Action _close;
    private bool _suppressPick;

    [ObservableProperty]
    private string _expression = string.Empty;

    [ObservableProperty]
    private string? _selectedBranch;

    [ObservableProperty]
    private string? _selectedTag;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <param name="goTo">Selects the commit in the grid (loading more history if needed); returns an error text or <see langword="null"/>.</param>
    public GoToCommitViewModel(Func<GitModule?> getModule, Func<ObjectId, Task<string?>> goTo, Action close)
    {
        _getModule = getModule;
        _goTo = goTo;
        _close = close;
    }

    public ObservableCollection<string> Branches { get; } = [];

    public ObservableCollection<string> Tags { get; } = [];

    public bool HasError => Error.Length > 0;

    public string Title => "Go to commit";

    /// <summary>Fills the branch and tag lists off the UI thread.</summary>
    public async Task LoadRefsAsync()
    {
        if (_getModule() is not { } module)
        {
            return;
        }

        try
        {
            (List<string> branches, List<string> tags) = await Task.Run(() =>
            {
                IReadOnlyList<IGitRef> refs = module.GetRefs(RefsFilter.Heads | RefsFilter.Tags | RefsFilter.Remotes);
                List<string> b = refs
                    .Where(r => !r.IsTag && !(r.IsRemote && r.Name.EndsWith("/HEAD", StringComparison.Ordinal)))
                    .Select(r => r.IsRemote ? r.Name : r.LocalName)
                    .Distinct()
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                List<string> t = refs.Where(r => r.IsTag).Select(r => r.LocalName).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
                return (b, t);
            });
            Branches.Clear();
            foreach (string branch in branches)
            {
                Branches.Add(branch);
            }

            Tags.Clear();
            foreach (string tag in tags)
            {
                Tags.Add(tag);
            }
        }
        catch (Exception ex)
        {
            Error = $"Could not list the branches and tags: {ex.Message}";
        }
    }

    partial void OnSelectedBranchChanged(string? value)
    {
        if (value is not null && !_suppressPick)
        {
            Pick(value, clearTag: true);
        }
    }

    partial void OnSelectedTagChanged(string? value)
    {
        if (value is not null && !_suppressPick)
        {
            Pick(value, clearTag: false);
        }
    }

    // Picking from one list empties the other, and the pick lands in the expression box.
    private void Pick(string name, bool clearTag)
    {
        _suppressPick = true;
        if (clearTag)
        {
            SelectedTag = null;
        }
        else
        {
            SelectedBranch = null;
        }

        _suppressPick = false;
        Expression = name;
        Error = string.Empty;
    }

    partial void OnExpressionChanged(string value) => Error = string.Empty;

    [RelayCommand]
    public async Task GoAsync()
    {
        string expression = Expression.Trim();
        if (expression.Length == 0)
        {
            Error = "Type a revision (a hash, a branch, a tag, HEAD~3, v1.0^{}…) or pick a branch or tag.";
            return;
        }

        if (_getModule() is not { } module)
        {
            Error = "No repository is open.";
            return;
        }

        IsBusy = true;
        try
        {
            ObjectId id = await Task.Run(() => module.RevParse(expression));
            if (id.IsZero)
            {
                Error = $"'{expression}' does not name a commit in this repository.";
                return;
            }

            string? problem = await _goTo(id);
            if (problem is not null)
            {
                Error = problem;
                return;
            }

            _close();
        }
        catch (Exception ex)
        {
            Error = $"Could not go to '{expression}': {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Cancel() => _close();
}
