using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExtensions.Desktop.History;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The second toolbar row of the main window: "Branches:" combo with its filter menu, and the "Filter:" box with its options menu.
/// </summary>
public sealed partial class RevisionFilterBarView : UserControl
{
    private static readonly (string Header, TextFilterMode Mode)[] Modes =
    [
        ("Commit message", TextFilterMode.CommitMessage),
        ("Author", TextFilterMode.Author),
        ("Committer", TextFilterMode.Committer),
        ("Hash / ref", TextFilterMode.HashOrRef),
        ("Diff content", TextFilterMode.DiffContent),
    ];

    private readonly MenuItem _remotes = new() { Header = "Show remote branches", ToggleType = MenuItemToggleType.CheckBox };
    private readonly MenuItem _tags = new() { Header = "Show tags", ToggleType = MenuItemToggleType.CheckBox };
    private readonly MenuItem _merges = new() { Header = "Show merge commits", ToggleType = MenuItemToggleType.CheckBox };
    private readonly List<(MenuItem Item, TextFilterMode Mode)> _modeItems = [];

    public RevisionFilterBarView()
    {
        InitializeComponent();

        _remotes.Click += (_, _) => Apply(vm => vm.ShowRemoteBranches = _remotes.IsChecked);
        _tags.Click += (_, _) => Apply(vm => vm.ShowTags = _tags.IsChecked);
        _merges.Click += (_, _) => Apply(vm => vm.ShowMergeCommits = _merges.IsChecked);
        MenuFlyout branchMenu = new() { ItemsSource = new[] { _remotes, _tags, _merges } };
        branchMenu.Opening += (_, _) => Apply(vm =>
        {
            _remotes.IsChecked = vm.ShowRemoteBranches;
            _tags.IsChecked = vm.ShowTags;
            _merges.IsChecked = vm.ShowMergeCommits;
        });
        FilterMenuButton.Flyout = branchMenu;

        List<MenuItem> items = [];
        foreach ((string header, TextFilterMode mode) in Modes)
        {
            MenuItem item = new() { Header = header, ToggleType = MenuItemToggleType.Radio, GroupName = "RevisionFilterMode" };
            item.Click += (_, _) => Apply(vm => vm.FilterMode = mode);
            _modeItems.Add((item, mode));
            items.Add(item);
        }

        MenuFlyout optionsMenu = new() { ItemsSource = items };
        optionsMenu.Opening += (_, _) => Apply(vm =>
        {
            foreach ((MenuItem item, TextFilterMode mode) in _modeItems)
            {
                item.IsChecked = vm.FilterMode == mode;
            }
        });
        FilterOptionsButton.Flyout = optionsMenu;

        Avalonia.Automation.AutomationProperties.SetName(BranchCombo, "Branches filter");
        Avalonia.Automation.AutomationProperties.SetName(FilterBox, "Revision filter");
        Avalonia.Automation.AutomationProperties.SetName(FilterMenuButton, "Branch filter options");
        Avalonia.Automation.AutomationProperties.SetName(FilterOptionsButton, "Filter options");
        Avalonia.Automation.AutomationProperties.SetName(ClearFilterButton, "Clear filter");
    }

    public TextBox Box => FilterBox;

    private void Apply(Action<MainWindowViewModel> action)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            action(viewModel);
        }
    }
}
