using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Puts text on the system clipboard; an interface so view-models can be tested without a window.
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}

/// <summary>
///  Clipboard of the <see cref="TopLevel"/> (window) hosting the UI.
/// </summary>
public sealed class TopLevelClipboardService(TopLevel? topLevel) : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        IClipboard? clipboard = topLevel?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
