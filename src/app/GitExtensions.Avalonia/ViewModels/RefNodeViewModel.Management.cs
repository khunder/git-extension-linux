namespace GitExtensions.Desktop.ViewModels;

// Branch/tag management: upstream marker and context menu visibility (kept out of the main file).
public sealed partial class RefNodeViewModel
{
    /// <summary>Set for the node of a remote (e.g. <c>origin</c>) below the Remotes group.</summary>
    public bool IsRemoteRoot { get; init; }

    /// <summary>For a remote: the hosting service its URL points to ("github", "gitlab"), or null; picks the icon.</summary>
    public string? RemoteHost { get; init; }

    /// <summary>Relation to the upstream branch; <see langword="null"/> for branches without one.</summary>
    public UpstreamTrack? Track { get; init; }

    public bool HasTrack => Kind == RefNodeKind.LocalBranch && Track is not null;

    public string TrackText => Track?.Text ?? string.Empty;

    /// <summary>The counts shown after the name like in the original ("(0↑↓)"); empty for nodes without an upstream relation.</summary>
    public string CountsText => Track is not null && Kind is RefNodeKind.LocalBranch or RefNodeKind.RemoteBranch ? Track.CountsText(remoteSide: Kind == RefNodeKind.RemoteBranch) : string.Empty;

    public bool HasCounts => CountsText.Length > 0;

    public string TrackToolTip => Track?.ToolTip ?? string.Empty;

    public bool ShowTrack => HasTrack && !TrackIsGone;

    public bool ShowGone => HasTrack && TrackIsGone;

    public bool TrackIsGone => Track?.Gone == true;

    public bool IsLocalBranch => Kind == RefNodeKind.LocalBranch;

    public bool IsRemoteBranch => Kind == RefNodeKind.RemoteBranch;

    public bool IsTag => Kind == RefNodeKind.Tag;

    public bool IsTagsGroup => Kind == RefNodeKind.Group && Name == "Tags";

    public bool HasUpstream => Track is not null;

    /// <summary>The remote a remote branch belongs to, or the remote node itself.</summary>
    public string? RemoteName => Kind == RefNodeKind.RemoteBranch && FullName.Contains('/') ? FullName[..FullName.IndexOf('/')]
        : IsRemoteRoot ? Name
        : null;

    public bool CanFetchRemote => RemoteName is not null;
}
