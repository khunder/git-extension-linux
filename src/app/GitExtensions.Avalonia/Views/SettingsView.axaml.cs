using Avalonia.Controls;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The content of the Settings dialog window. Esc (handled by the window) closes it like Cancel, nothing is reverted.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        AttachedToVisualTree += (_, _) =>
        {
            Attach();

            // After layout, so the box can take focus.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("SearchBox")?.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
        };
    }

    private void Attach()
    {
        if (DataContext is SettingsViewModel vm && TopLevel.GetTopLevel(this) is { } top)
        {
            vm.FilePicker ??= new WindowFilePicker(top);
            vm.FolderPicker ??= new WindowFolderPicker(top);
        }
    }
}
