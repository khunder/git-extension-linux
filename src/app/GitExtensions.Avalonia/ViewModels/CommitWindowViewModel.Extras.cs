using CommunityToolkit.Mvvm.Input;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

// Conflict resolution, .gitignore editing and the commit template of the Commit window.
public sealed partial class CommitWindowViewModel
{
    /// <summary>Resolves the selected unmerged file (shown in place of the diff).</summary>
    public ConflictResolverViewModel Resolver { get; }

    /// <summary>Gets a value indicating whether the working tree has unmerged files.</summary>
    public bool HasConflicts => AllChanges.Any(c => c.IsConflicted);

    /// <summary>The window title: "Commit to {repository} ({branch})".</summary>
    private string TitleFor(string? branch) => string.IsNullOrEmpty(branch) ? $"Commit to {RepositoryName}" : $"Commit to {RepositoryName} ({branch})";

    /// <summary>Opens the .gitignore of the repository in the default editor (created when missing).</summary>
    [RelayCommand]
    private void EditGitIgnore() => _main.EditWorkingTreeFileCommand.Execute(".gitignore");

    /// <summary>
    ///  Appends the selected files (or directories) to the .gitignore (like "Add to .gitignore" of the original) and refreshes.
    /// </summary>
    [RelayCommand]
    private async Task AddToGitIgnoreAsync()
    {
        string[] names = [.. UnstagedSelection.Where(f => !f.Item.IsTracked).Select(f => f.Name).Distinct()];
        if (names.Length == 0)
        {
            return;
        }

        try
        {
            string path = Path.Combine(_workingDir, ".gitignore");
            await Task.Run(() =>
            {
                string existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                HashSet<string> lines = [.. existing.Split('\n').Select(l => l.TrimEnd('\r'))];
                string prefix = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : string.Empty;
                string add = string.Concat(names.Select(n => "/" + n).Where(n => !lines.Contains(n)).Select(n => n + "\n"));
                if (add.Length > 0)
                {
                    File.AppendAllText(path, prefix + add);
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError($"Could not update .gitignore: {ex.Message}");
        }

        await AfterIndexChangeAsync();
    }

    /// <summary>The git <c>commit.template</c> (when configured) fills an empty message, like in the original.</summary>
    private void ApplyConfiguredTemplate()
    {
        if (string.IsNullOrWhiteSpace(Commit.Message) && !Commit.Amend && Templates.FirstOrDefault(t => t.Name.StartsWith("Template:", StringComparison.Ordinal)) is { } template)
        {
            Commit.Message = template.Text;
        }
    }
}
