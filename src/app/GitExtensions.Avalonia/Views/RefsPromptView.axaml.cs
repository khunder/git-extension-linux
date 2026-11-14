using Avalonia.Controls;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class RefsPromptView : UserControl
{
    public RefsPromptView()
    {
        InitializeComponent();

        // The window clipboard is only known once the view is in the tree; "Copy name" uses it.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is RefsTreeViewModel { Clipboard: null } refs)
            {
                refs.Clipboard = new TopLevelClipboardService(TopLevel.GetTopLevel(this));
            }
        };
    }
}
