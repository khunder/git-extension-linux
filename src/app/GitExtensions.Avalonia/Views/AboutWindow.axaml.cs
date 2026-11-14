using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The small "About Git Extensions" dialog.
/// </summary>
public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        string version = typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AboutWindow).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        VersionText.Text = $"Version {version.Split('+')[0]}";
    }

    private void OnWebsite(object? sender, RoutedEventArgs e) => ShellLauncher.OpenUrl("https://gitextensions.github.io");

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
