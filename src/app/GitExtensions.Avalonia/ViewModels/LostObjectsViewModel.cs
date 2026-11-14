using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>A lost (dangling or unreachable) object in the list.</summary>
public sealed partial class LostObjectItem : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    public LostObjectItem(LostObjectLine line) => Line = line;

    public LostObjectLine Line { get; }

    public string Hash => Line.Hash;

    public string ShortHash => Line.Hash[..Math.Min(8, Line.Hash.Length)];

    public LostObjectType Type => Line.Type;

    public string RawType => Line.RawType;

    public bool IsCommitLike => Type is LostObjectType.Commit or LostObjectType.Tag;

    public string Author { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Parent { get; set; } = string.Empty;

    public string ShortParent => Parent.Length > 8 ? Parent[..8] : Parent;

    public DateTimeOffset? Date { get; set; }

    public string DateText => Date is { } d ? d.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
}

public enum LostObjectsPromptKind
{
    None,
    CreateBranch,
    CreateTag,
    ConfirmPrune,
    ConfirmDeleteTags,
}

/// <summary>
///  Recover lost objects: lists what <c>git fsck</c> finds that nothing points to, previews it and restores commits as tags
///  (<c>LOST_FOUND_&lt;id&gt;</c>), or as a branch or a tag with a name the user picks. Pruning asks for an inline confirmation.
/// </summary>
public sealed partial class LostObjectsViewModel : ObservableObject
{
    public const string TagPrefix = "LOST_FOUND_";
    private const int PreviewLimit = 100_000;
    private const int MetadataBatch = 200;

    private readonly ToolsContext _context;
    private readonly Action _close;
    private readonly List<LostObjectItem> _all = [];
    private int _loadVersion;
    private int _previewVersion;

    [ObservableProperty]
    private bool _unreachable;

    [ObservableProperty]
    private bool _fullCheck;

    [ObservableProperty]
    private bool _noReflogs;

    [ObservableProperty]
    private bool _showCommitsAndTags = true;

    [ObservableProperty]
    private bool _showOtherObjects;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanRestore))]
    private LostObjectItem? _selectedItem;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private string _statusText = "Press Scan to look for lost objects.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPromptVisible), nameof(NeedsInput), nameof(PromptText), nameof(ConfirmText))]
    private LostObjectsPromptKind _promptKind;

    [ObservableProperty]
    private string _promptName = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public LostObjectsViewModel(ToolsContext context, Action close)
    {
        _context = context;
        _close = close;
    }

    public string Title => "Recover lost objects";

    public ObservableCollection<LostObjectItem> Items { get; } = [];

    public bool HasSelection => SelectedItem is not null;

    public bool CanRestore => SelectedItem is { IsCommitLike: true };

    public bool IsPromptVisible => PromptKind != LostObjectsPromptKind.None;

    public bool NeedsInput => PromptKind is LostObjectsPromptKind.CreateBranch or LostObjectsPromptKind.CreateTag;

    public string PromptText => PromptKind switch
    {
        LostObjectsPromptKind.CreateBranch => $"New branch at {SelectedItem?.ShortHash}:",
        LostObjectsPromptKind.CreateTag => $"New tag at {SelectedItem?.ShortHash}:",
        LostObjectsPromptKind.ConfirmPrune => "Delete all dangling objects for good (git prune)? Anything listed here that was not restored first is gone afterwards.",
        LostObjectsPromptKind.ConfirmDeleteTags => $"Delete every tag that starts with {TagPrefix}? The commits stay until git prunes them.",
        _ => string.Empty,
    };

    public string ConfirmText => PromptKind switch
    {
        LostObjectsPromptKind.CreateBranch => "Create branch",
        LostObjectsPromptKind.CreateTag => "Create tag",
        LostObjectsPromptKind.ConfirmPrune => "Prune",
        _ => "Delete tags",
    };

    public IEnumerable<LostObjectItem> CheckedItems => _all.Where(i => i.IsChecked && i.IsCommitLike);

    partial void OnShowCommitsAndTagsChanged(bool value)
    {
        if (!value && !ShowOtherObjects)
        {
            ShowOtherObjects = true;
        }
        else
        {
            ApplyFilter();
        }
    }

    partial void OnShowOtherObjectsChanged(bool value)
    {
        if (!value && !ShowCommitsAndTags)
        {
            ShowCommitsAndTags = true;
        }
        else
        {
            ApplyFilter();
        }
    }

    partial void OnSelectedItemChanged(LostObjectItem? value) => _ = LoadPreviewAsync(value);

    partial void OnUnreachableChanged(bool value) => _ = ScanAsync();

    partial void OnFullCheckChanged(bool value) => _ = ScanAsync();

    partial void OnNoReflogsChanged(bool value) => _ = ScanAsync();

    private void ApplyFilter()
    {
        LostObjectItem? selected = SelectedItem;
        Items.Clear();
        foreach (LostObjectItem item in _all.Where(i => i.IsCommitLike ? ShowCommitsAndTags : ShowOtherObjects))
        {
            Items.Add(item);
        }

        if (selected is not null && !Items.Contains(selected))
        {
            SelectedItem = null;
        }
    }

    /// <summary>Runs <c>git fsck</c> and lists the lost objects, newest commits first.</summary>
    [RelayCommand]
    public async Task ScanAsync()
    {
        GitModule? module = _context.Module;
        if (module is null)
        {
            return;
        }

        int version = ++_loadVersion;
        string options = (Unreachable ? " --unreachable" : string.Empty) + (FullCheck ? " --full" : string.Empty) + (NoReflogs ? " --no-reflogs" : string.Empty);
        IsBusy = true;
        StatusText = "Scanning…";
        try
        {
            List<LostObjectItem> found = await _context.RunBusyAsync(() => Scan(module, options));
            if (version != _loadVersion)
            {
                return;
            }

            _all.Clear();
            _all.AddRange(found);
            ApplyFilter();
            int commits = found.Count(i => i.IsCommitLike);
            StatusText = found.Count == 0 ? "No lost objects found." : $"{found.Count} lost objects ({commits} commits or tags).";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            _context.ReportError($"Could not look for lost objects: {ex.Message}");
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsBusy = false;
            }
        }
    }

    private static List<LostObjectItem> Scan(GitModule module, string options)
    {
        ExecutionResult fsck = module.GitExecutable.Execute($"fsck --no-progress{options}", throwOnErrorExit: false);
        List<LostObjectItem> items = LostObjectParser.Parse(fsck.StandardOutput + "\n" + fsck.StandardError).Select(line => new LostObjectItem(line)).ToList();
        LostObjectItem[] commits = [.. items.Where(i => i.Type == LostObjectType.Commit)];
        for (int start = 0; start < commits.Length; start += MetadataBatch)
        {
            string hashes = string.Join(' ', commits.Skip(start).Take(MetadataBatch).Select(c => c.Hash));
            ExecutionResult show = module.GitExecutable.Execute($"show --no-patch --format=%H%x1f%aN%x1f%ct%x1f%P%x1f%s%x1e {hashes}", throwOnErrorExit: false);
            IReadOnlyDictionary<string, (string Author, DateTimeOffset Date, string Parent, string Subject)> metadata = LostObjectParser.ParseMetadata(show.StandardOutput);
            foreach (LostObjectItem commit in commits.Skip(start).Take(MetadataBatch))
            {
                if (metadata.TryGetValue(commit.Hash, out var data))
                {
                    commit.Author = data.Author;
                    commit.Date = data.Date;
                    commit.Parent = data.Parent;
                    commit.Subject = data.Subject;
                }
            }
        }

        return [.. items.OrderByDescending(i => i.Date ?? DateTimeOffset.MinValue)];
    }

    private async Task LoadPreviewAsync(LostObjectItem? item)
    {
        int version = ++_previewVersion;
        if (item is null || _context.Module is not { } module)
        {
            Preview = string.Empty;
            return;
        }

        try
        {
            string text = await Task.Run(() =>
            {
                string command = item.Type == LostObjectType.Blob ? $"cat-file -p {item.Hash}" : $"show --no-color {item.Hash}";
                ExecutionResult result = module.GitExecutable.Execute(command, throwOnErrorExit: false);
                string content = result.ExitedSuccessfully ? result.StandardOutput : "The object could not be read. It may be corrupted or no longer available.\n\n" + result.StandardError;
                return content.Length > PreviewLimit ? content[..PreviewLimit] + "\n… (truncated)" : content;
            });
            if (version == _previewVersion)
            {
                Preview = text;
            }
        }
        catch (Exception ex)
        {
            Preview = ex.Message;
        }
    }

    // ---- restore ----

    /// <summary>Tags every ticked commit <c>LOST_FOUND_&lt;id&gt;</c> (existing ones are kept) and returns how many tags exist now for them.</summary>
    [RelayCommand]
    public async Task<int> RestoreCheckedAsTagsAsync()
    {
        LostObjectItem[] items = [.. CheckedItems];
        GitModule? module = _context.Module;
        if (items.Length == 0 || module is null)
        {
            _context.ReportStatus("Tick the commits to restore first.");
            return 0;
        }

        IsBusy = true;
        try
        {
            int created = 0;
            List<string> failures = [];
            foreach (LostObjectItem item in items)
            {
                string name = TagPrefix + item.Hash;
                ExecutionResult exists = await _context.RunGitAsync(module, $"rev-parse --verify --quiet refs/tags/{name}");
                if (exists.ExitedSuccessfully)
                {
                    created++;
                    continue;
                }

                ExecutionResult result = await _context.RunGitAsync(module, $"tag {name} {item.Hash}");
                if (result.ExitedSuccessfully)
                {
                    created++;
                }
                else
                {
                    failures.Add(ToolsContext.Describe(result));
                }
            }

            if (failures.Count > 0)
            {
                _context.ReportError($"Could not tag {failures.Count} object(s): {failures[0]}");
            }

            StatusText = $"{created} tags {TagPrefix}<id> now point at restored commits. Delete them when you are finished.";
            _context.ReportStatus(StatusText);
            await _context.RefreshAsync();
            return created;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RequestCreateBranch() => OpenPrompt(LostObjectsPromptKind.CreateBranch, "recovered-" + SelectedItem?.ShortHash);

    [RelayCommand]
    private void RequestCreateTag() => OpenPrompt(LostObjectsPromptKind.CreateTag, TagPrefix + SelectedItem?.ShortHash);

    [RelayCommand]
    private void RequestPrune() => OpenPrompt(LostObjectsPromptKind.ConfirmPrune, string.Empty);

    [RelayCommand]
    private void RequestDeleteTags() => OpenPrompt(LostObjectsPromptKind.ConfirmDeleteTags, string.Empty);

    [RelayCommand]
    private void CancelPrompt() => PromptKind = LostObjectsPromptKind.None;

    [RelayCommand]
    private void Close() => _close();

    private void OpenPrompt(LostObjectsPromptKind kind, string name)
    {
        if (kind is LostObjectsPromptKind.CreateBranch or LostObjectsPromptKind.CreateTag && !CanRestore)
        {
            return;
        }

        PromptName = name;
        PromptKind = kind;
    }

    [RelayCommand]
    public async Task ConfirmPromptAsync()
    {
        switch (PromptKind)
        {
            case LostObjectsPromptKind.CreateBranch:
            case LostObjectsPromptKind.CreateTag:
                if (SelectedItem is { } item
                    && await CreateRefAsync(item, PromptName.Trim(), isTag: PromptKind == LostObjectsPromptKind.CreateTag))
                {
                    PromptKind = LostObjectsPromptKind.None;
                }

                break;
            case LostObjectsPromptKind.ConfirmPrune:
                PromptKind = LostObjectsPromptKind.None;
                await PruneAsync();
                break;
            case LostObjectsPromptKind.ConfirmDeleteTags:
                PromptKind = LostObjectsPromptKind.None;
                await DeleteLostFoundTagsAsync();
                break;
        }
    }

    /// <summary>Creates a branch or a tag at the lost commit. Returns false (with the reason reported) when git or the name rejects it.</summary>
    public async Task<bool> CreateRefAsync(LostObjectItem item, string name, bool isTag)
    {
        GitModule? module = _context.Module;
        if (module is null)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            string? problem = await Task.Run(() => RefNameValidator.Validate(module.GitExecutable, name, isTag));
            if (problem is not null)
            {
                _context.ReportError(problem);
                return false;
            }

            ExecutionResult result = await _context.RunGitAsync(module, new GitArgumentBuilder(isTag ? "tag" : "branch") { name.Quote(), item.Hash });
            if (!result.ExitedSuccessfully)
            {
                _context.ReportError($"git {(isTag ? "tag" : "branch")} failed: {ToolsContext.Describe(result)}");
                return false;
            }

            StatusText = $"Created {(isTag ? "tag" : "branch")} {name} at {item.ShortHash}.";
            _context.ReportStatus(StatusText);
            await _context.RefreshAsync();
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PruneAsync()
    {
        if (_context.Module is not { } module)
        {
            return;
        }

        ExecutionResult result = await _context.RunGitAsync(module, "prune");
        if (!result.ExitedSuccessfully)
        {
            _context.ReportError($"git prune failed: {ToolsContext.Describe(result)}");
            return;
        }

        _context.ReportStatus("Pruned the dangling objects.");
        await ScanAsync();
    }

    public async Task DeleteLostFoundTagsAsync()
    {
        if (_context.Module is not { } module)
        {
            return;
        }

        ExecutionResult list = await _context.RunGitAsync(module, $"for-each-ref --format=%(refname:short) refs/tags/{TagPrefix}*");
        string[] tags = list.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string tag in tags)
        {
            await _context.RunGitAsync(module, $"tag -d {tag}");
        }

        _context.ReportStatus($"Deleted {tags.Length} {TagPrefix} tags.");
        await _context.RefreshAsync();
        await ScanAsync();
    }
}
