using Avalonia.Controls;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

// Format/apply/view patch, go to commit, bisect and recover lost objects: each one is a dialog window.
public sealed partial class MainWindow
{
    private void BindToolDialogs(MainWindowViewModel viewModel)
    {
        DialogHost.Bind(this, viewModel.FormatPatch, () => new FormatPatchView { DataContext = viewModel.FormatPatch }, "Format patch", 640, 460);
        DialogHost.Bind(this, viewModel.ApplyPatch, () => WithPicker(new ApplyPatchView { DataContext = viewModel.ApplyPatch }, viewModel), "Apply patch", 660, 480);
        DialogHost.Bind(this, viewModel.ViewPatch, () => WithPicker(new ViewPatchView { DataContext = viewModel.ViewPatch }, viewModel), "View patch file", 900, 600);

        ToolDialogsViewModel tools = viewModel.Dialogs;
        DialogHost.Bind(this, tools.GoToCommit, () => new GoToCommitView { DataContext = tools.Current }, "Go to commit", 520, 360, canResize: false);
        DialogHost.Bind(this, tools.Bisect, () => new BisectView { DataContext = tools.Current }, "Bisect", 560, 580, canResize: false);
        DialogHost.Bind(this, tools.LostObjects, () => new LostObjectsView { DataContext = tools.Current }, "Verify database", 900, 620);
    }

    // The patch dialogs browse for files: the picker is owned by the dialog window the view ends up in.
    private static T WithPicker<T>(T view, MainWindowViewModel viewModel)
        where T : Control
    {
        view.AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(view) is { } top)
            {
                viewModel.PatchFilePicker = new WindowFilePicker(top);
            }
        };
        return view;
    }
}
