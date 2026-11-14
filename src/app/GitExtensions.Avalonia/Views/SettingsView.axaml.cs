using Avalonia.Controls;
using Avalonia.Input;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The in-window settings dialog. Esc closes it (like Cancel, nothing is reverted).
/// </summary>
public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        AttachedToVisualTree += (_, _) => Attach();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                // After layout, so the box can take focus; Esc is then received by this control.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("SearchBox")?.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
            }
        };
        KeyDown += OnKeyDown;
    }

    private void Attach()
    {
        if (DataContext is SettingsViewModel vm && TopLevel.GetTopLevel(this) is { } top)
        {
            vm.FilePicker ??= new WindowFilePicker(top);
            vm.FolderPicker ??= new WindowFolderPicker(top);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is SettingsViewModel vm && vm.CancelCommand.CanExecute(null))
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
