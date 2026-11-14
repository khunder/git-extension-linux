namespace GitUI;

/// <summary>
///  WinForms-specific entry points to <see cref="TaskManager"/> and <see cref="ThreadHelper"/>.
///  The portable implementation lives in GitExtUtils; only the <see cref="Control"/> binding is here.
/// </summary>
public static class ControlTaskExtensions
{
    /// <summary>
    /// Asynchronously run <paramref name="asyncAction"/> on the UI thread and forward all exceptions to <see cref="Application.OnThreadException"/> except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public static void InvokeAndForget(this Control control, Func<Task> asyncAction, TaskManager? taskManager = null, CancellationToken cancellationToken = default)
        => (taskManager ?? ThreadHelper.TaskManager).InvokeAndForget(control, asyncAction, cancellationToken);

    /// <summary>
    /// Asynchronously run <paramref name="action"/> on the UI thread and forward all exceptions to <see cref="Application.OnThreadException"/> except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public static void InvokeAndForget(this Control control, Action action, TaskManager? taskManager = null, CancellationToken cancellationToken = default)
        => (taskManager ?? ThreadHelper.TaskManager).InvokeAndForget(control, action, cancellationToken);

    public static void InvokeAndForget(this TaskManager taskManager, Control control, Func<Task> asyncAction, CancellationToken cancellationToken = default)
        => taskManager.InvokeAndForget(control.GetDisposedCancellationToken(), asyncAction, cancellationToken);

    public static void InvokeAndForget(this TaskManager taskManager, Control control, Action action, CancellationToken cancellationToken = default)
        => taskManager.InvokeAndForget(control.GetDisposedCancellationToken(), action, cancellationToken);
}
