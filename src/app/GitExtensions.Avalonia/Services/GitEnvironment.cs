using System.ComponentModel.Design;
using System.Diagnostics;
using GitCommands;
using GitCommands.Git;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUI;
using Microsoft.VisualStudio.Threading;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Wires up the platform-neutral Git Extensions core (threading, services) for this UI host.
/// </summary>
public sealed class GitEnvironment
{
    private GitEnvironment(IGitExecutorProvider executorProvider, AvaloniaMessageDialogs messages)
    {
        ExecutorProvider = executorProvider;
        Messages = messages;
    }

    /// <summary>
    ///  Receives errors and warnings reported by the core; the main view-model shows them as a banner.
    /// </summary>
    public AvaloniaMessageDialogs Messages { get; }

    public IGitExecutorProvider ExecutorProvider { get; }

    /// <summary>
    ///  Initializes the core. Call once, on the UI thread, before using any repository.
    /// </summary>
    public static GitEnvironment Initialize()
    {
        if (!ThreadHelper.HasJoinableTaskContext)
        {
            ThreadHelper.JoinableTaskContext = new JoinableTaskContext();
        }

        // Exceptions from fire-and-forget operations: log them and surface them in the error banner.
        AvaloniaMessageDialogs messages = new();
        MessageDialogs.Instance = messages;
        TaskManager.ExceptionReporter = ex =>
        {
            Trace.TraceError(ex.ToString());
            messages.ReportException(ex);
        };

        ServiceContainer serviceContainer = new();
        serviceContainer.AddService<IGitDirectoryResolver>(new GitDirectoryResolver());
        GitExtUtils.ServiceContainerRegistry.RegisterServices(serviceContainer);
        GitCommands.ServiceContainerRegistry.RegisterServices(serviceContainer);

        return new GitEnvironment(serviceContainer.GetRequiredService<IGitExecutorProvider>(), messages);
    }

    /// <summary>
    ///  Opens the repository containing <paramref name="path"/>, or returns <see langword="null"/> if there is none.
    /// </summary>
    public GitModule? OpenRepository(string path)
    {
        string? workingDir = GitModule.TryFindGitWorkingDir(path);
        return workingDir is null ? null : new GitModule(ExecutorProvider, workingDir);
    }
}
