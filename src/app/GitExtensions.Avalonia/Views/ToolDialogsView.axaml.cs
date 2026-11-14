using Avalonia.Controls;

namespace GitExtensions.Desktop.Views;

/// <summary>The overlay that hosts the small modal dialogs. Esc is handled by the window (topmost overlay first).</summary>
public sealed partial class ToolDialogsView : UserControl
{
    public ToolDialogsView() => InitializeComponent();
}
