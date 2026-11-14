using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>The tab control below the revision grid. Its data context is the <see cref="MainWindowViewModel"/>.</summary>
public sealed partial class RevisionTabsView : UserControl
{
    public RevisionTabsView()
        => InitializeComponent();
}
