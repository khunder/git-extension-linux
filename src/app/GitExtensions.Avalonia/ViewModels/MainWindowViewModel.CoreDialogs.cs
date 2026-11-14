using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

// Commands that open the small dialog windows converted from overlays (command log).
public sealed partial class MainWindowViewModel
{
    private CommandLogDialogViewModel? _commandLogDialog;

    /// <summary>The "Command log" dialog; it shows the log that feeds the Console tab.</summary>
    public CommandLogDialogViewModel CommandLogDialog => _commandLogDialog ??= new CommandLogDialogViewModel(RevisionTabs.Console);

    [RelayCommand]
    private void ShowCommandLog() => CommandLogDialog.IsOpen = true;
}
