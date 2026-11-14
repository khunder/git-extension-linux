using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

public enum ConflictKind
{
    BothModified,
    BothAdded,
    DeletedByUs,
    DeletedByThem,
    BothDeleted,
    Other,
}

public enum ConflictLineKind
{
    Normal,
    Marker,
    Ours,
    Theirs,
}

/// <summary>One line of a conflicted file; marker lines and the two sides are tinted by the view.</summary>
public sealed record ConflictLine(string Text, ConflictLineKind Kind)
{
    public bool IsMarker => Kind == ConflictLineKind.Marker;

    public bool IsOurs => Kind == ConflictLineKind.Ours;

    public bool IsTheirs => Kind == ConflictLineKind.Theirs;
}

/// <summary>
///  Resolves one unmerged file: take our or their version, keep or delete it for delete-vs-modify conflicts,
///  or mark the (externally edited) working tree file as resolved. The file text is shown read-only with the markers highlighted.
/// </summary>
public sealed partial class ConflictResolverViewModel : ObservableObject
{
    private const int MaxBytes = 2 * 1024 * 1024;

    private readonly Func<GitModule?> _getModule;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _reportError;
    private int _version;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseSides), nameof(IsDeleteConflict), nameof(KeepText), nameof(Summary))]
    private ConflictKind _kind;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _hasMarkers;

    [ObservableProperty]
    private bool _isBusy;

    public ConflictResolverViewModel(Func<GitModule?> getModule, Func<Task> refresh, Action<string> reportError)
    {
        _getModule = getModule;
        _refresh = refresh;
        _reportError = reportError;
    }

    public ObservableCollection<ConflictLine> Lines { get; } = [];

    public bool HasMessage => Message.Length > 0;

    public bool CanChooseSides => Kind is ConflictKind.BothModified or ConflictKind.BothAdded or ConflictKind.Other;

    public bool IsDeleteConflict => Kind is ConflictKind.DeletedByUs or ConflictKind.DeletedByThem;

    public string KeepText => Kind == ConflictKind.DeletedByUs ? "Keep file (their version)" : "Keep file (our version)";

    public string Summary => Kind switch
    {
        ConflictKind.BothModified => "Both sides modified this file.",
        ConflictKind.BothAdded => "Both sides added this file.",
        ConflictKind.DeletedByUs => "Deleted by us, modified by them.",
        ConflictKind.DeletedByThem => "Modified by us, deleted by them.",
        ConflictKind.BothDeleted => "Both sides deleted this file.",
        _ => "This file has a conflict.",
    };

    /// <summary>Shows the resolver for <paramref name="file"/> when it is unmerged, hides it otherwise.</summary>
    /// <param name="file">The selected entry.</param>
    /// <param name="all">
    ///  All entries of the list: git reports a delete-vs-modify conflict as two entries (staged "D" plus unmerged "U"),
    ///  and either of them opens the resolver.
    /// </param>
    public async Task ShowAsync(ChangedFileViewModel? file, IEnumerable<ChangedFileViewModel>? all = null)
    {
        if (file is { IsConflicted: false } && all?.Any(c => c.IsConflicted && c.Name == file.Name) == true)
        {
            file = all.First(c => c.IsConflicted && c.Name == file.Name);
        }

        int version = ++_version;
        GitModule? module = _getModule();
        if (file is not { IsConflicted: true } || module is null)
        {
            IsActive = false;
            FileName = string.Empty;
            Lines.Clear();
            HasMarkers = false;
            Message = string.Empty;
            return;
        }

        IsActive = true;
        FileName = file.Name;
        await LoadAsync(module, file.Name, version);
    }

    [RelayCommand]
    private Task ReloadAsync()
    {
        GitModule? module = _getModule();
        return module is null || !IsActive ? Task.CompletedTask : LoadAsync(module, FileName, ++_version);
    }

    private async Task LoadAsync(GitModule module, string name, int version)
    {
        try
        {
            (ConflictKind kind, string? text, string message) = await Task.Run(() => Read(module, name));
            if (version != _version)
            {
                return;
            }

            Kind = kind;
            Message = message;
            Lines.Clear();
            bool markers = false;
            if (text is not null)
            {
                foreach (ConflictLine line in Parse(text))
                {
                    markers |= line.IsMarker;
                    Lines.Add(line);
                }
            }

            HasMarkers = markers;
        }
        catch (Exception ex)
        {
            Message = $"Could not read {name}: {ex.Message}";
        }
    }

    private static (ConflictKind Kind, string? Text, string Message) Read(GitModule module, string name)
    {
        ExecutionResult stages = module.GitExecutable.Execute(new GitArgumentBuilder("ls-files") { "-u", "-z", "--", name.Quote() }, throwOnErrorExit: false);
        HashSet<char> present = [];
        if (stages.ExitedSuccessfully)
        {
            // "<mode> <object> <stage>\t<path>"
            foreach (string entry in stages.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int tab = entry.IndexOf('\t');
                if (tab > 2)
                {
                    present.Add(entry[tab - 1]);
                }
            }
        }

        ConflictKind kind = (present.Contains('1'), present.Contains('2'), present.Contains('3')) switch
        {
            (_, true, true) when !present.Contains('1') => ConflictKind.BothAdded,
            (_, true, true) => ConflictKind.BothModified,
            (_, true, false) when present.Contains('1') => ConflictKind.DeletedByThem,
            (_, false, true) when present.Contains('1') => ConflictKind.DeletedByUs,
            (true, false, false) => ConflictKind.BothDeleted,
            _ => ConflictKind.Other,
        };

        string path = Path.Combine(module.WorkingDir, name);
        if (!File.Exists(path))
        {
            return (kind, null, "The file does not exist in the working tree.");
        }

        FileInfo info = new(path);
        if (info.Length > MaxBytes)
        {
            return (kind, null, "The file is too large to display.");
        }

        byte[] bytes = File.ReadAllBytes(path);
        return Array.IndexOf(bytes, (byte)0) >= 0
            ? (kind, null, "Binary file. Choose our or their version.")
            : (kind, System.Text.Encoding.UTF8.GetString(bytes), string.Empty);
    }

    /// <summary>Splits the text into lines and classifies them relative to the conflict markers.</summary>
    public static IEnumerable<ConflictLine> Parse(string text)
    {
        ConflictLineKind region = ConflictLineKind.Normal;
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        for (int i = 0; i < count; i++)
        {
            string line = lines[i];
            if (line.StartsWith("<<<<<<<", StringComparison.Ordinal))
            {
                region = ConflictLineKind.Ours;
                yield return new ConflictLine(line, ConflictLineKind.Marker);
            }
            else if (region != ConflictLineKind.Normal && (line.StartsWith("=======", StringComparison.Ordinal) || line.StartsWith("|||||||", StringComparison.Ordinal)))
            {
                // "|||||||" starts the diff3 base section, which is shown like the ours side's remainder.
                region = line[0] == '=' ? ConflictLineKind.Theirs : ConflictLineKind.Normal;
                yield return new ConflictLine(line, ConflictLineKind.Marker);
            }
            else if (region != ConflictLineKind.Normal && line.StartsWith(">>>>>>>", StringComparison.Ordinal))
            {
                region = ConflictLineKind.Normal;
                yield return new ConflictLine(line, ConflictLineKind.Marker);
            }
            else
            {
                yield return new ConflictLine(line, region);
            }
        }
    }

    // ---- resolve ----

    [RelayCommand]
    public Task<bool> UseOursAsync() => ResolveAsync(["checkout", "--ours", "--", FileName], ["add", "--", FileName]);

    [RelayCommand]
    public Task<bool> UseTheirsAsync() => ResolveAsync(["checkout", "--theirs", "--", FileName], ["add", "--", FileName]);

    [RelayCommand]
    public Task<bool> MarkResolvedAsync() => ResolveAsync(["add", "--", FileName]);

    /// <summary>Delete-vs-modify: keeps the surviving version of the file.</summary>
    [RelayCommand]
    public Task<bool> KeepFileAsync()
        => Kind == ConflictKind.DeletedByUs
            ? ResolveAsync(["checkout", "--theirs", "--", FileName], ["add", "--", FileName])
            : ResolveAsync(["add", "--", FileName]);

    /// <summary>Delete-vs-modify: accepts the deletion.</summary>
    [RelayCommand]
    public Task<bool> DeleteFileAsync() => ResolveAsync(["rm", "--quiet", "--", FileName]);

    private async Task<bool> ResolveAsync(params string[][] commands)
    {
        GitModule? module = _getModule();
        if (module is null || !IsActive || FileName.Length == 0 || IsBusy)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            foreach (string[] command in commands)
            {
                GitRunResult result = await GitOperationRunner.RunAsync(module.GitExecutable, command);
                if (!result.Succeeded)
                {
                    _reportError($"git {command[0]} failed for {FileName}: {result.Message}");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _reportError($"Could not resolve {FileName}: {ex.Message}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        await _refresh();
        return true;
    }
}
