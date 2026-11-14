namespace GitExtensions.Desktop.Services;

/// <summary>
///  Lets the user choose where to save a file; implemented by the UI host (the view-model stays toolkit-free and testable).
/// </summary>
public interface IFileSavePicker
{
    /// <returns>The chosen file, or <see langword="null"/> if the user cancelled.</returns>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension);
}
