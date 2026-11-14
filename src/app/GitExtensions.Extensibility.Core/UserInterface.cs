namespace GitExtensions.Extensibility;

/// <summary>
///  The UI element that owns an operation: a WinForms <c>Control</c>, an Avalonia <c>Window</c>, etc.
///  Lets platform-neutral code marshal to the UI thread and parent message boxes without depending on a UI toolkit.
/// </summary>
public interface IUiOwner
{
    /// <summary>
    ///  Cancelled when the owner is disposed or closed.
    /// </summary>
    CancellationToken DisposedToken { get; }

    /// <summary>
    ///  The toolkit-specific owner object (for example an <c>IWin32Window</c>), or <see langword="null"/>.
    /// </summary>
    object? NativeOwner { get; }
}

/// <summary>
///  Shows simple modal messages. Implemented by the UI host.
/// </summary>
public interface IMessageDialogs
{
    /// <param name="owner">An <see cref="IUiOwner"/>, a toolkit-specific owner object, or <see langword="null"/>.</param>
    void ShowError(object? owner, string text, string? caption = null);

    /// <param name="owner">An <see cref="IUiOwner"/>, a toolkit-specific owner object, or <see langword="null"/>.</param>
    void ShowWarning(object? owner, string text, string caption);
}

/// <summary>
///  The <see cref="IMessageDialogs"/> used by platform-neutral code. The UI host replaces it at startup;
///  until then (and in headless scenarios) messages are written to the trace output.
/// </summary>
public static class MessageDialogs
{
    public static IMessageDialogs Instance { get; set; } = new TraceMessageDialogs();

    private sealed class TraceMessageDialogs : IMessageDialogs
    {
        public void ShowError(object? owner, string text, string? caption = null)
            => System.Diagnostics.Trace.TraceError($"{caption ?? "Error"}: {text}");

        public void ShowWarning(object? owner, string text, string caption)
            => System.Diagnostics.Trace.TraceWarning($"{caption}: {text}");
    }
}
