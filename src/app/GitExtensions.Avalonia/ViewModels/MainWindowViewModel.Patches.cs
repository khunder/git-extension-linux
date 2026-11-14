using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

// Format patch, apply patch and view patch file (Commands menu; "Format patch" is also in the commit context menu).
public sealed partial class MainWindowViewModel
{
    private FormatPatchViewModel? _formatPatch;
    private ApplyPatchViewModel? _applyPatch;
    private ViewPatchViewModel? _viewPatch;
    private IFilePicker? _patchFilePicker;

    public FormatPatchViewModel FormatPatch => _formatPatch ??= new FormatPatchViewModel(() => _module, _folderPicker, message => Status = message);

    public ApplyPatchViewModel ApplyPatch => _applyPatch ??= new ApplyPatchViewModel(() => _module, () => OpenAsync(RepositoryPath), message => Status = message)
    {
        FolderPicker = _folderPicker,
        FilePicker = _patchFilePicker,
    };

    public ViewPatchViewModel ViewPatch => _viewPatch ??= new ViewPatchViewModel { FilePicker = _patchFilePicker };

    /// <summary>The window's file picker; attached by the view once it is in the visual tree.</summary>
    public IFilePicker? PatchFilePicker
    {
        get => _patchFilePicker;
        set
        {
            _patchFilePicker = value;
            ApplyPatch.FilePicker = value;
            ViewPatch.FilePicker = value;
        }
    }

    [RelayCommand]
    private void ShowFormatPatch() => FormatPatch.Open(SelectedCommit?.ObjectId.ToString());

    /// <summary>The commit context menu: format the patch for exactly that commit.</summary>
    [RelayCommand]
    private void ShowFormatPatchFor(CommitItemViewModel? commit) => FormatPatch.Open(commit?.ObjectId.ToString());

    [RelayCommand]
    private Task ShowApplyPatchAsync() => ApplyPatch.OpenAsync();

    [RelayCommand]
    private void ShowViewPatch() => ViewPatch.Open();
}
