using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The host of the small modal dialogs (go to commit, bisect, recover lost objects): one overlay that shows whichever dialog
///  view-model is current. Esc or the dialog's own buttons close it.
/// </summary>
public sealed partial class ToolDialogsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private ObservableObject? _current;

    public bool IsOpen => Current is not null;

    public void Show(ObservableObject dialog) => Current = dialog;

    public void Close() => Current = null;
}
