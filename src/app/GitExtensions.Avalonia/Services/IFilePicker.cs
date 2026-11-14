namespace GitExtensions.Desktop.Services;

/// <summary>
///  Lets the user choose a file; implemented by the UI host (the view-model stays toolkit-free and testable).
/// </summary>
public interface IFilePicker
{
    /// <returns>The chosen file, or <see langword="null"/> if the user cancelled.</returns>
    Task<string?> PickFileAsync(string title);
}
