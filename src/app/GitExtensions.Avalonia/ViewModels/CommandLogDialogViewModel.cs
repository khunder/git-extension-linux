using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>The "Command log" dialog: the git commands the application ran (the same log as the Console tab).</summary>
public sealed partial class CommandLogDialogViewModel(CommandLogService log) : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    public CommandLogService Log { get; } = log;

    [RelayCommand]
    private void Close() => IsOpen = false;
}
