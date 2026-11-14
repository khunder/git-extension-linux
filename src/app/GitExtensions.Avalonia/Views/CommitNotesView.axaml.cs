using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class CommitNotesView : UserControl
{
    public CommitNotesView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is CommitDetailsViewModel { IsEditingNote: true })
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("NoteEditBox")?.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
            }
        };
    }
}
