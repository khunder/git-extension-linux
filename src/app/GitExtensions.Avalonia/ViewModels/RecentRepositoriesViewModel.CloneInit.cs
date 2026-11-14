using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

public sealed partial class RecentRepositoriesViewModel
{
    /// <summary>
    ///  Set by the host: opens the clone dialog.
    /// </summary>
    public Action? RequestClone { get; set; }

    /// <summary>
    ///  Set by the host: opens the init dialog.
    /// </summary>
    public Action? RequestInit { get; set; }

    [RelayCommand]
    private void Clone()
    {
        Chosen?.Invoke();
        RequestClone?.Invoke();
    }

    [RelayCommand]
    private void Init()
    {
        Chosen?.Invoke();
        RequestInit?.Invoke();
    }
}
