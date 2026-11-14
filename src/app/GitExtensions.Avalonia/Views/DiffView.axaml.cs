using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views.ContextMenus;

namespace GitExtensions.Desktop.Views;

public partial class DiffView : UserControl
{
    private bool _dragging;

    public DiffView()
    {
        InitializeComponent();
        DiffLines.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        DiffLines.AddHandler(PointerReleasedEvent, (_, _) => _dragging = false, RoutingStrategies.Bubble, handledEventsToo: true);
        DiffLines.AddHandler(PointerCaptureLostEvent, (_, _) => _dragging = false, RoutingStrategies.Bubble, handledEventsToo: true);
        MenuKit.AttachBuilt(
            DiffLines,
            row => DataContext is FileDiffViewModel diff && diff.File is not null
                ? DiffContextMenu.Build(diff, row as DiffLine, new TopLevelClipboardService(TopLevel.GetTopLevel(this)))
                : null);
    }

    private void OnLinePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: DiffLine line } || DataContext is not FileDiffViewModel viewModel)
        {
            return;
        }

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
        {
            // Keys (S, U, R, arrows) go to the lines from here on.
            DiffLines.Focus();
            viewModel.SelectLine(line, e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _dragging = !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            e.Handled = true;
        }
        else if (properties.IsRightButtonPressed)
        {
            // The context menu acts on the selection; a line outside of it becomes the selection (like in an editor).
            DiffLines.Focus();
            if (line.IsChange && !line.IsSelected)
            {
                viewModel.SelectLine(line, ctrl: false, shift: false);
            }
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || DataContext is not FileDiffViewModel viewModel)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragging = false;
            return;
        }

        // The line under the pointer (a row of the diff), also while the pointer is dragged over other rows.
        Avalonia.Visual? hit = DiffLines.GetVisualAt(e.GetPosition(DiffLines));
        if (hit?.FindAncestorOfType<Border>(includeSelf: true) is { DataContext: DiffLine line })
        {
            viewModel.ExtendSelectionTo(line);
        }
    }

    // Keys while the lines have the focus: S/U/R act on the selected lines, the arrows move or extend the selection.
    private void OnDiffKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FileDiffViewModel viewModel || e.Handled)
        {
            return;
        }

        KeyModifiers modifiers = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta | KeyModifiers.Shift);
        if (modifiers == KeyModifiers.None && viewModel.HasSelection)
        {
            switch (e.Key)
            {
                case Key.S when viewModel.CanStageSelected:
                    viewModel.StageSelectedCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.U when viewModel.CanUnstageSelected:
                    viewModel.UnstageSelectedCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.R when viewModel.CanDiscardSelected:
                    // Asks for confirmation first.
                    viewModel.DiscardSelectedCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key is Key.Up or Key.Down && (modifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
        {
            if (viewModel.MoveSelection(e.Key == Key.Down ? 1 : -1, extend: modifiers == KeyModifiers.Shift) is { } line)
            {
                int index = viewModel.Lines.IndexOf(line);
                if (index >= 0)
                {
                    DiffLines.ScrollIntoView(index);
                }
            }

            e.Handled = true;
        }
        else if (e.Key == Key.A && modifiers == KeyModifiers.Control)
        {
            viewModel.SelectAllLines();
            e.Handled = true;
        }
    }

    private void OnHunkAction(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DiffLine hunk, Tag: string action } && DataContext is FileDiffViewModel viewModel)
        {
            // The commands handle their own failures and report them through the view-model's message.
            switch (action)
            {
                case "stage":
                    viewModel.StageHunkCommand.Execute(hunk);
                    break;
                case "unstage":
                    viewModel.UnstageHunkCommand.Execute(hunk);
                    break;
                case "discard":
                    viewModel.DiscardHunkCommand.Execute(hunk);
                    break;
            }

            e.Handled = true;
        }
    }
}
