using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GitExtensions.Desktop.Views;

public sealed partial class BisectView : UserControl
{
    public BisectView() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();
}
