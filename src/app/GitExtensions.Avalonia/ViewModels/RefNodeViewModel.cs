using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

public enum RefNodeKind
{
    /// <summary>One of the three top level groups: local branches, remotes, tags.</summary>
    Group,

    /// <summary>A remote name or a slash separated folder such as <c>feature</c>.</summary>
    Folder,
    LocalBranch,
    RemoteBranch,
    Tag,
    Submodule,
    Stash,
}

/// <summary>
///  A node of the branches/remotes/tags tree.
/// </summary>
public sealed partial class RefNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;

    public RefNodeViewModel(string name, RefNodeKind kind, string fullName = "", ObjectId? objectId = null, bool isCurrent = false)
    {
        Name = name;
        Kind = kind;
        FullName = fullName;
        ObjectId = objectId;
        IsCurrent = isCurrent;
    }

    /// <summary>The (last segment of the) name shown in the tree.</summary>
    public string Name { get; }

    public RefNodeKind Kind { get; }

    /// <summary>The name to hand to git, e.g. <c>feature/x</c>, <c>origin/main</c> or <c>v1.0</c>. Empty for groups and folders.</summary>
    public string FullName { get; }

    public ObjectId? ObjectId { get; }

    public bool IsCurrent { get; }

    /// <summary>For a group: the number of entries it holds (shown in the header, e.g. "Branches (72)").</summary>
    public int? Count { get; init; }

    /// <summary>The text shown in the tree: the name, with the count for groups that have one.</summary>
    public string Header => Count is int count ? $"{Name} ({count})" : Name;

    public ObservableCollection<RefNodeViewModel> Children { get; } = [];

    public bool IsLeaf => Kind is RefNodeKind.LocalBranch or RefNodeKind.RemoteBranch or RefNodeKind.Tag;

    public bool CanCheckout => Kind is RefNodeKind.LocalBranch or RefNodeKind.Tag && !IsCurrent;

    public string Marker => IsCurrent ? "●" : string.Empty;

    public FontWeight FontWeight => IsCurrent || Kind is RefNodeKind.Group ? FontWeight.Bold : FontWeight.Normal;
}
