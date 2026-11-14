using System.ComponentModel;
using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The "Stash" button of the repository bar: a quick menu (stash the changes, apply or pop the latest stash) and
///  "Manage stashes…", which opens the stash dialog. Its DataContext is the main view-model.
/// </summary>
public sealed class StashMenuButton : Button
{
    private readonly MenuFlyout _menu = new() { Placement = PlacementMode.BottomEdgeAlignedRight };
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
        _menu.Opening += (_, _) => FillMenu();
        Flyout = _menu;
        DataContextChanged += (_, _) => Attach(DataContext as MainWindowViewModel);
    }

    public MenuFlyout Menu => _menu;

    private void Attach(MainWindowViewModel? main)
    {
        if (_stash is not null)
        {
            _stash.PropertyChanged -= OnStashChanged;
        }

        _stash = main?.Stash;
        if (_stash is not null)
        {
            _stash.PropertyChanged += OnStashChanged;
        }

        Content = _stash?.ButtonText ?? "Stash ▾";
    }

    private void FillMenu()
    {
        _menu.Items.Clear();
        if (_stash is not { } stash)
        {
            return;
        }

        StashItemViewModel? latest = stash.Items.FirstOrDefault();
        _menu.Items.Add(Item("_Stash changes", () => QuickStash(stash, includeUntracked: false)));
        _menu.Items.Add(Item("Stash changes incl. _untracked files", () => QuickStash(stash, includeUntracked: true)));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item(latest is null ? "_Apply latest stash" : $"_Apply latest stash ({latest.Name})", () => _ = stash.ApplyAsync(latest), latest is not null));
        _menu.Items.Add(Item(latest is null ? "_Pop latest stash" : $"_Pop latest stash ({latest.Name})", () => _ = stash.PopAsync(latest), latest is not null));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(new MenuItem { Header = "_Manage stashes…", InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.T, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift), Command = stash.OpenCommand });
    }

    private static MenuItem Item(string header, Action click, bool enabled = true)
    {
        MenuItem item = new() { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => click();
        return item;
    }

    private static void QuickStash(StashViewModel stash, bool includeUntracked)
    {
        stash.StashMessage = string.Empty;
        stash.IncludeUntracked = includeUntracked;
        _ = stash.ConfirmStashAsync();
    }

    private void OnStashChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StashViewModel.ButtonText))
        {
            Content = _stash!.ButtonText;
        }
    }
}
