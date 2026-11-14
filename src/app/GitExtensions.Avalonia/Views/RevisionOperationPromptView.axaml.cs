using Avalonia.Controls;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class RevisionOperationPromptView : UserControl
{
    public RevisionOperationPromptView()
    {
        InitializeComponent();

        // The window's file dialog is only known once the view is in the tree; "Archive" uses it.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is RevisionOperationsViewModel { SavePicker: null } operations && TopLevel.GetTopLevel(this) is { } top)
            {
                operations.SavePicker = new WindowFileSavePicker(top);
            }
        };
    }
}
