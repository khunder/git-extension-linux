using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The "Stash" button of the repository bar: opens an in-window flyout with the stash list and its commands.
///  Its DataContext is the main view-model; the flyout content gets <see cref="MainWindowViewModel.Stash"/>.
/// </summary>
public sealed class StashMenuButton : Button
{
    private readonly StashView _view = new();
    private StashViewModel? _stash;

    // Style as a plain Button: the theme's control template is keyed on the exact type.
    protected override Type StyleKeyOverride => typeof(Button);

    public StashMenuButton()
    {
        Name = "StashMenuButton";
        Content = "Stash ▾";

        // The content stays the plain label text; the template adds the original's stash icon.
        ContentTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((_, _) => new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new AppIcon { IconName = "Stash" },
                new TextBlock { [!TextBlock.TextProperty] = new Avalonia.Data.Binding(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            },
        });
        Flyout flyout = new() { Content = _view, Placement = PlacementMode.BottomEdgeAlignedRight };

        // The theme caps flyouts at 456px wide, which would clip the stash list and diff.
        flyout.FlyoutPresenterClasses.Add("stash");
        Styles.Add(new Style(x => x.OfType<FlyoutPresenter>().Class("stash"))
        {
            Setters = { new Setter(MaxWidthProperty, double.PositiveInfinity), new Setter(PaddingProperty, new Thickness(0)) },
        });
        Flyout = flyout;
        DataContextChanged += (_, _) => Attach(DataContext as MainWindowViewModel);
    }

    public StashView View => _view;

    private void Attach(MainWindowViewModel? main)
    {
        if (_stash is not null)
        {
            _stash.PropertyChanged -= OnStashChanged;
        }

        _stash = main?.Stash;
        _view.DataContext = _stash;
        if (_stash is not null)
        {
            _stash.PropertyChanged += OnStashChanged;
        }

        Content = _stash?.ButtonText ?? "Stash ▾";
    }

    private void OnStashChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StashViewModel.ButtonText))
        {
            Content = _stash!.ButtonText;
        }
    }
}
