using GitExtensions.Desktop.Diff;

namespace GitExtensions.Desktop.Revision;

/// <summary>One numbered line of a file shown in the file tree tab.</summary>
public sealed record ContentLine(int Number, string Text)
{
    public string DisplayText => DiffLimits.ToDisplayText(Text);
}
