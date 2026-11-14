using System.Globalization;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Desktop.ViewModels;

public sealed class CommitItemViewModel
{
    public CommitItemViewModel(GitRevision revision, string refs)
    {
        Revision = revision;
        ShortHash = revision.ObjectId.ToShortString();
        Subject = revision.Subject;
        Author = revision.Author ?? string.Empty;
        Date = revision.AuthorDate.ToString("g", CultureInfo.CurrentCulture);
        Refs = refs;
    }

    public GitRevision Revision { get; }

    /// <summary>Graph cell data, set when the commit is added to the history.</summary>
    public GitExtensions.Desktop.History.CommitGraphRow? Graph { get; set; }

    /// <summary>The context menu operations (shared by all rows).</summary>
    public CommitActionsViewModel? Actions { get; set; }

    public ObjectId ObjectId => Revision.ObjectId;

    public string ShortHash { get; }

    public string Subject { get; }

    public string Author { get; }

    public string Date { get; }

    /// <summary>
    ///  Branches and tags pointing at this commit.
    /// </summary>
    public string Refs { get; }

    public bool HasRefs => Refs.Length > 0;
}
