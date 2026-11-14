using Avalonia.Controls;
using Avalonia.Input;

namespace GitExtensions.Desktop.Views;

/// <summary>The non-modal "Solve merge conflicts" window.</summary>
public sealed partial class MergeConflictsWindow : Window
{
    public MergeConflictsWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }
}
