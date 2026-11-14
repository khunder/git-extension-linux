namespace GitExtensions.Extensibility;

/// <summary>
///  <see cref="IMessageDialogs"/> backed by <see cref="MessageBoxes"/>. Register with
///  <c>MessageDialogs.Instance = new WinFormsMessageDialogs();</c> at startup.
/// </summary>
public sealed class WinFormsMessageDialogs : IMessageDialogs
{
    public void ShowError(object? owner, string text, string? caption = null)
        => MessageBoxes.ShowError(ToWindow(owner), text, caption);

    public void ShowWarning(object? owner, string text, string caption)
        => MessageBoxes.Show(ToWindow(owner), text, caption, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

    private static IWin32Window? ToWindow(object? owner)
        => owner switch
        {
            IUiOwner { NativeOwner: IWin32Window window } => window,
            IWin32Window window => window,
            _ => null
        };
}
