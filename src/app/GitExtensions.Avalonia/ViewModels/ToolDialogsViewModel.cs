using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The host of the small modal dialogs (go to commit, bisect, recover lost objects): whichever dialog view-model is current
///  is shown in its own dialog window (one <see cref="ToolDialogSlot"/> per kind). The dialog's own buttons or Esc close it.
/// </summary>
public sealed partial class ToolDialogsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private ObservableObject? _current;

    public ToolDialogsViewModel()
    {
        GoToCommit = new ToolDialogSlot(this, typeof(GoToCommitViewModel));
        Bisect = new ToolDialogSlot(this, typeof(BisectViewModel));
        LostObjects = new ToolDialogSlot(this, typeof(LostObjectsViewModel));
    }

    public bool IsOpen => Current is not null;

    public ToolDialogSlot GoToCommit { get; }

    public ToolDialogSlot Bisect { get; }

    public ToolDialogSlot LostObjects { get; }

    public void Show(ObservableObject dialog) => Current = dialog;

    public void Close() => Current = null;

    partial void OnCurrentChanged(ObservableObject? value)
    {
        GoToCommit.Refresh();
        Bisect.Refresh();
        LostObjects.Refresh();
    }
}

/// <summary>An <c>IsOpen</c> flag for one kind of tool dialog, so that DialogHost can show it as a window.</summary>
public sealed class ToolDialogSlot : ObservableObject
{
    private readonly ToolDialogsViewModel _owner;
    private readonly Type _type;
    private bool _wasOpen;

    public ToolDialogSlot(ToolDialogsViewModel owner, Type type)
    {
        _owner = owner;
        _type = type;
    }

    public bool IsOpen
    {
        get => _owner.Current is { } current && _type.IsInstanceOfType(current);
        set
        {
            if (!value && IsOpen)
            {
                _owner.Close();
            }
        }
    }

    internal void Refresh()
    {
        bool open = IsOpen;
        if (open != _wasOpen)
        {
            _wasOpen = open;
            OnPropertyChanged(nameof(IsOpen));
        }
    }
}
