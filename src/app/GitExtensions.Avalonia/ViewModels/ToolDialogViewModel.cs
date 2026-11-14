using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  Base of the repository tool view-models (reflog, submodules, worktrees): each one is shown in its own dialog window
///  while <see cref="IsOpen"/> is true (see <c>DialogHost</c>).
/// </summary>
public abstract partial class ToolDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Gets or sets the tools that own this one; they remember where to go back to after a submodule or worktree was opened.</summary>
    public RepositoryToolsViewModel? Owner { get; set; }

    [RelayCommand]
    private void Close() => IsOpen = false;
}
