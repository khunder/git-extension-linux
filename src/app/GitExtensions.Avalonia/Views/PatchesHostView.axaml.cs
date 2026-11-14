using Avalonia.Controls;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class PatchesHostView : UserControl
{
    public PatchesHostView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is MainWindowViewModel { PatchFilePicker: null } vm && TopLevel.GetTopLevel(this) is { } top)
        {
            vm.PatchFilePicker = new WindowFilePicker(top);
        }
    }
}
