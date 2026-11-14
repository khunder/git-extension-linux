namespace GitExtensions.Desktop.Services;

/// <summary>
///  Lets the user choose a folder; implemented by the UI host (the view-model stays toolkit-free and testable).
/// </summary>
public interface IFolderPicker
{
    /// <returns>The chosen folder, or <see langword="null"/> if the user cancelled.</returns>
    Task<string?> PickFolderAsync(string title);
}
