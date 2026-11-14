using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The in-window settings overlay. Esc cancels.
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
                Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("GitPathBox")?.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
            }
        };
        KeyDown += OnKeyDown;
        SettingsViewModelChanged();
    }

    private void SettingsViewModelChanged()
    {
        // Keep the "ok" style class in step with the test result.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(SettingsViewModel.GitTestSucceeded) or nameof(SettingsViewModel.GitTestResult))
                    {
                        this.FindControl<TextBlock>("GitTestText")?.Classes.Set("ok", vm.GitTestSucceeded);
                    }
                };
            }
        };
    }

    private void Attach()
    {
        if (DataContext is SettingsViewModel { FilePicker: null } vm && TopLevel.GetTopLevel(this) is { } top)
        {
            vm.FilePicker = new WindowFilePicker(top);
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
