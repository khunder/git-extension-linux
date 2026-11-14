using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensions.Desktop;

public sealed class App : Application
{
    public override void Initialize()
        => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Must run on the UI thread: the joinable task context captures it as the "main thread".
            GitEnvironment git = GitEnvironment.Initialize();

            MainWindow window = new();
            MainWindowViewModel viewModel = new(git, new WindowFolderPicker(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            WindowStateStore windowState = new();
            windowState.Apply(window);
            window.Closing += (_, _) => windowState.Capture(window);

            _ = viewModel.Recent.LoadAsync();

            // "git-extensions-avalonia <path>" opens that repository straight away.
            if (desktop.Args is [string path, ..])
            {
                _ = viewModel.OpenAsync(path);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
