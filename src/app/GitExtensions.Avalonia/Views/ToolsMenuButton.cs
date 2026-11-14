using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The "Tools" button of the repository bar: opens an in-window flyout with the reflog, submodules and worktrees tabs.
///  Its DataContext is the main view-model; the flyout content gets <see cref="MainWindowViewModel.Tools"/>.
/// </summary>
public sealed class ToolsMenuButton : Button
{
    private readonly RepositoryToolsView _view = new();
    private RepositoryToolsViewModel? _tools;

    // Style as a plain Button: the theme's control template is keyed on the exact type.
    protected override Type StyleKeyOverride => typeof(Button);

    public ToolsMenuButton()
    {
        Name = "ToolsMenuButton";
        Content = "Tools ▾";
        Flyout flyout = new() { Content = _view, Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.FlyoutPresenterClasses.Add("tools");
        Styles.Add(new Style(x => x.OfType<FlyoutPresenter>().Class("tools"))
        {
            Setters = { new Setter(MaxWidthProperty, double.PositiveInfinity), new Setter(PaddingProperty, new Thickness(0)) },
        });
        flyout.Opened += (_, _) => SetOpen(true);
        flyout.Closed += (_, _) => SetOpen(false);
        Flyout = flyout;
        DataContextChanged += (_, _) => Attach(DataContext as MainWindowViewModel);
    }

    public RepositoryToolsView View => _view;

    private void Attach(MainWindowViewModel? main)
    {
        _tools = main?.Tools;
        _view.DataContext = _tools;
        if (_tools is not null)
        {
            _tools.Clipboard = new TopLevelClipboardService(TopLevel.GetTopLevel(this));
        }
    }

    private void SetOpen(bool open)
    {
        if (_tools is not null)
        {
            _tools.IsOpen = open;
        }
    }
}
