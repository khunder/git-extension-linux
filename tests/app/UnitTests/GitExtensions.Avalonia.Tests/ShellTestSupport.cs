using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

internal sealed class NoPicker : IFolderPicker
{
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
}

internal sealed class RecordingClipboard : IClipboardService
{
    public string? Text { get; private set; }

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }
}

/// <summary>Records the secondary windows the shell asked for.</summary>
internal sealed class RecordingWindowService : IWindowService
{
    public int CommitWindows { get; private set; }

    public List<string> FileHistories { get; } = [];

    public void ShowCommitWindow() => CommitWindows++;

    public void ShowFileHistoryWindow(string path, string? revision = null) => FileHistories.Add(path);
}

internal static class ShellTestSupport
{
    /// <summary>
    ///  Finds a named control anywhere below <paramref name="root"/> (also inside user controls, which have their own name scope).
    /// </summary>
    public static T Find<T>(this Visual root, string name)
        where T : Control
        => root.TryFind<T>(name)
            ?? throw new InvalidOperationException($"No {typeof(T).Name} named {name}.");

    public static T? TryFind<T>(this Visual root, string name)
        where T : Control
        => (root as StyledElement)?.FindNameScope()?.Find<T>(name)
            ?? root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    public static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///  A minimal window hosting the working-tree changes UI (changes list, diff, conflict resolver) that the
    ///  main window no longer shows: the Commit window reuses these view-models, so their views are tested here.
    /// </summary>
    public static Window CreateChangesHost(MainWindowViewModel viewModel, double width = 1000, double height = 700)
    {
        ListBox list = new() { Name = "ChangeList", MinHeight = 60 };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MainWindowViewModel.ChangedFiles)));
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(MainWindowViewModel.SelectedChangedFile)) { Mode = BindingMode.TwoWay });
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ChangedFileViewModel>((file, _) => new TextBlock { Text = $"{file?.Status} {file?.Name}" });

        DiffView diff = new() { Name = "DiffPanel", MinHeight = 120 };
        diff.Bind(StyledElement.DataContextProperty, new Binding(nameof(MainWindowViewModel.Diff)));
        ConflictResolverView resolver = new() { Name = "ResolverView" };
        resolver.Bind(StyledElement.DataContextProperty, new Binding(nameof(MainWindowViewModel.Resolver)));
        resolver.Bind(Visual.IsVisibleProperty, new Binding("Resolver.IsActive"));

        Grid grid = new() { RowDefinitions = new RowDefinitions("130*,Auto,330*") };
        grid.Children.Add(list);
        Grid.SetRow(diff, 2);
        grid.Children.Add(diff);
        Grid.SetRow(resolver, 2);
        grid.Children.Add(resolver);
        GridSplitter splitter = new() { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(splitter, 1);
        grid.Children.Add(splitter);

        Window window = new() { DataContext = viewModel, Width = width, Height = height, Content = grid };
        window.Show();
        return window;
    }
}
