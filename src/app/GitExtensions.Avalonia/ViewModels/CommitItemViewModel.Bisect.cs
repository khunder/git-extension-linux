using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExtensions.Desktop.ViewModels;

// The bisect mark of a grid row (set while a bisect session runs).
public sealed partial class CommitItemViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBisectGood), nameof(IsBisectBad), nameof(IsBisectSkip), nameof(HasBisectMark), nameof(BisectText))]
    private BisectMark _bisectMark;

    public bool IsBisectGood => BisectMark == BisectMark.Good;

    public bool IsBisectBad => BisectMark == BisectMark.Bad;

    public bool IsBisectSkip => BisectMark == BisectMark.Skip;

    public bool HasBisectMark => BisectMark != BisectMark.None;

    public string BisectText => BisectMark switch
    {
        BisectMark.Good => "good",
        BisectMark.Bad => "bad",
        BisectMark.Skip => "skip",
        _ => string.Empty,
    };
}
