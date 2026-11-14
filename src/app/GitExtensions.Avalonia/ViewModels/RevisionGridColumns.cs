using CommunityToolkit.Mvvm.ComponentModel;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The widths and visibility of the revision grid columns, shared by the header and every row so they line up.
///  The "Message" column has no width of its own: it fills what the others leave.
/// </summary>
public sealed partial class RevisionGridColumns : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarColumnWidth))]
    private double _avatarWidth = new GridColumnSettings().AvatarWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AuthorColumnWidth))]
    private double _authorWidth = new GridColumnSettings().AuthorWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateColumnWidth))]
    private double _dateWidth = new GridColumnSettings().DateWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HashColumnWidth))]
    private double _hashWidth = new GridColumnSettings().HashWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusColumnWidth))]
    private double _statusWidth = new GridColumnSettings().StatusWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarColumnWidth), nameof(AuthorColumnWidth))]
    private bool _showAuthor = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateColumnWidth))]
    private bool _showDate = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HashColumnWidth))]
    private bool _showHash = true;

    /// <summary>The build status column is empty (and collapsed) until some commit has a build status.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusColumnWidth))]
    private bool _showStatus;

    // Effective widths: 0 for a hidden column.
    public double AvatarColumnWidth => ShowAuthor ? AvatarWidth : 0;

    public double AuthorColumnWidth => ShowAuthor ? AuthorWidth : 0;

    public double DateColumnWidth => ShowDate ? DateWidth : 0;

    public double HashColumnWidth => ShowHash ? HashWidth : 0;

    public double StatusColumnWidth => ShowStatus ? StatusWidth : 0;

    public GridColumnSettings ToSettings() => new(AvatarWidth, AuthorWidth, DateWidth, HashWidth, StatusWidth);

    public void Apply(GridColumnSettings settings)
    {
        GridColumnSettings s = settings.Clamp();
        AvatarWidth = s.AvatarWidth;
        AuthorWidth = s.AuthorWidth;
        DateWidth = s.DateWidth;
        HashWidth = s.HashWidth;
        StatusWidth = s.StatusWidth;
    }

    /// <summary>Resizes a column by dragging its left edge: dragging left (negative delta) widens it.</summary>
    public void Resize(string column, double delta)
    {
        double Next(double width) => Math.Clamp(width - delta, GridColumnSettings.MinWidth, GridColumnSettings.MaxWidth);
        switch (column)
        {
            case "Avatar":
                AvatarWidth = Next(AvatarWidth);
                break;
            case "Author":
                AuthorWidth = Next(AuthorWidth);
                break;
            case "Date":
                DateWidth = Next(DateWidth);
                break;
            case "Hash":
                HashWidth = Next(HashWidth);
                break;
            case "Status":
                StatusWidth = Next(StatusWidth);
                break;
        }
    }
}
