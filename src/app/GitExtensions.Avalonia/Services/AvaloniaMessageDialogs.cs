using Avalonia.Threading;
using GitExtensions.Extensibility;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Surfaces messages from the portable core as an in-window banner: it raises <see cref="MessageReported"/>
///  on the UI thread and the main view-model displays it. Never blocks the caller.
/// </summary>
public sealed class AvaloniaMessageDialogs : IMessageDialogs
{
    /// <summary>
    ///  Raised on the UI thread with the text to show.
    /// </summary>
    public event Action<string>? MessageReported;

    public void ShowError(object? owner, string text, string? caption = null)
        => Report(string.IsNullOrWhiteSpace(caption) ? text : $"{caption}: {text}");

    public void ShowWarning(object? owner, string text, string caption)
        => Report(string.IsNullOrWhiteSpace(caption) ? text : $"{caption}: {text}");

    public void ReportException(Exception exception)
        => Report(exception.Message);

    private void Report(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            MessageReported?.Invoke(message);
        }
        else
        {
            Dispatcher.UIThread.Post(() => MessageReported?.Invoke(message));
        }
    }
}
