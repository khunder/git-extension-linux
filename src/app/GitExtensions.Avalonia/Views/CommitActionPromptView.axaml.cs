using Avalonia.Controls;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class CommitActionPromptView : UserControl
{
    public CommitActionPromptView()
    {
        InitializeComponent();

        // The window clipboard is only known once the view is in the tree; the context menu commands use it.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is CommitActionsViewModel { Clipboard: null } actions)
            {
                actions.Clipboard = new TopLevelClipboardService(TopLevel.GetTopLevel(this));
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is CommitActionsViewModel { Clipboard: null } actions && TopLevel.GetTopLevel(this) is { } topLevel)
            {
                actions.Clipboard = new TopLevelClipboardService(topLevel);
            }
        };
        PropertyChanged += (_, e) =>
        {
            // Focus the name box when the prompt opens.
            if (e.Property == IsVisibleProperty && IsVisible && DataContext is CommitActionsViewModel { NeedsInput: true })
            {
                this.FindControl<TextBox>("PromptInput")?.Focus();
            }
        };
    }
}
