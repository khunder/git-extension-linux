using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.Diff;

public enum PatchOperation
{
    /// <summary>Apply the selection to the index (<c>git apply --cached</c>).</summary>
    Stage,

    /// <summary>Remove the selection from the index (<c>git apply --cached -R</c>).</summary>
    Unstage,

    /// <summary>Remove the selection from the working tree (<c>git apply -R</c>).</summary>
    Discard
}

/// <summary>
///  Builds a partial patch (whole hunks or selected lines) from the raw text of a single file's <c>git diff</c>.
///  Works on raw lines (line endings are preserved, so CRLF content round-trips) and, like <c>git add -p</c>,
///  turns unselected lines into context lines or drops them so the patch applies with plain context.
/// </summary>
public static partial class HunkPatchBuilder
{
    [GeneratedRegex(@"^@@ -(?<os>\d+)(,(?<oc>\d+))? \+(?<ns>\d+)(,(?<nc>\d+))? @@(?<rest>.*)$", RegexOptions.Singleline)]
    private static partial Regex HunkHeader();

    /// <summary>
    ///  Splits diff text at <c>\n</c> only (a <c>\r</c> stays part of the line); the final newline adds no empty line.
    ///  The result lines correspond one-to-one to the lines <see cref="UnifiedDiffParser.Parse"/> yields for the same text.
    /// </summary>
    public static string[] SplitRaw(string text)
    {
        string[] lines = text.Split('\n');
        return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    /// <summary>
    ///  Builds the patch for the changed lines whose indices are in <paramref name="selected"/> (indices into
    ///  <paramref name="lines"/>/<paramref name="raw"/>), or <see langword="null"/> when nothing applicable is selected.
    /// </summary>
    public static string? Build(IReadOnlyList<string> raw, IReadOnlyList<DiffLine> lines, IReadOnlySet<int> selected, PatchOperation operation)
    {
        if (raw.Count != lines.Count)
        {
            return null;
        }

        int firstHunk = -1;
        int totalChanged = 0;
        int selectedChanged = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (firstHunk < 0 && lines[i].Kind == DiffLineKind.Hunk)
            {
                firstHunk = i;
            }

            if (lines[i].IsChange)
            {
                totalChanged++;
                if (selected.Contains(i))
                {
                    selectedChanged++;
                }
            }
        }

        if (firstHunk < 0 || selectedChanged == 0)
        {
            return null;
        }

        bool reverse = operation != PatchOperation.Stage;
        StringBuilder patch = new();
        foreach (string headerLine in RewriteHeader(raw.Take(firstHunk).ToList(), partial: selectedChanged < totalChanged))
        {
            patch.Append(headerLine).Append('\n');
        }

        int delta = 0;
        int index = firstHunk;
        while (index < lines.Count)
        {
            if (lines[index].Kind != DiffLineKind.Hunk)
            {
                index++;
                continue;
            }

            int end = index + 1;
            while (end < lines.Count && lines[end].Kind is not (DiffLineKind.Hunk or DiffLineKind.Header))
            {
                end++;
            }

            if (BuildHunk(raw, lines, selected, reverse, index, end, delta) is { } hunk)
            {
                patch.Append(hunk.Text);
                delta += hunk.NewCount - hunk.OldCount;
            }

            index = end;
        }

        return patch.ToString();
    }

    private static List<string> RewriteHeader(List<string> header, bool partial)
    {
        if (!partial)
        {
            return header;
        }

        // A partial change of a new/deleted file is a plain modification of the file.
        string? oldPath = header.FirstOrDefault(l => l.StartsWith("--- a/", StringComparison.Ordinal))?[4..];
        string? newPath = header.FirstOrDefault(l => l.StartsWith("+++ b/", StringComparison.Ordinal))?[4..];
        List<string> result = [];
        foreach (string line in header)
        {
            if (line.StartsWith("new file mode", StringComparison.Ordinal) || line.StartsWith("deleted file mode", StringComparison.Ordinal))
            {
                continue;
            }

            if (line == "--- /dev/null" && newPath is not null)
            {
                result.Add("--- a/" + newPath[2..]);
            }
            else if (line == "+++ /dev/null" && oldPath is not null)
            {
                result.Add("+++ b/" + oldPath[2..]);
            }
            else
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static (string Text, int OldCount, int NewCount)? BuildHunk(
        IReadOnlyList<string> raw, IReadOnlyList<DiffLine> lines, IReadOnlySet<int> selected, bool reverse, int headerIndex, int end, int delta)
    {
        // Lines of the hunk body with their "\ No newline" markers attached.
        List<(DiffLineKind Kind, string Text, bool Selected, List<string> Markers)> items = [];
        for (int i = headerIndex + 1; i < end; i++)
        {
            if (lines[i].Kind == DiffLineKind.Info)
            {
                if (items.Count > 0)
                {
                    items[^1].Markers.Add(raw[i]);
                }
            }
            else
            {
                items.Add((lines[i].Kind, raw[i], selected.Contains(i), []));
            }
        }

        StringBuilder body = new();
        int oldCount = 0;
        int newCount = 0;
        bool anySelected = false;

        void Emit(char prefix, (DiffLineKind Kind, string Text, bool Selected, List<string> Markers) item)
        {
            body.Append(prefix).Append(item.Text.Length > 0 ? item.Text[1..] : string.Empty).Append('\n');
            foreach (string marker in item.Markers)
            {
                body.Append(marker).Append('\n');
            }

            oldCount += prefix is ' ' or '-' ? 1 : 0;
            newCount += prefix is ' ' or '+' ? 1 : 0;
        }

        int index = 0;
        while (index < items.Count)
        {
            if (items[index].Kind == DiffLineKind.Context)
            {
                Emit(' ', items[index]);
                index++;
                continue;
            }

            // A block of changes: removed lines first, then added lines (as git emits them).
            int blockEnd = index;
            while (blockEnd < items.Count && items[blockEnd].Kind != DiffLineKind.Context)
            {
                blockEnd++;
            }

            var removed = items.Skip(index).Take(blockEnd - index).Where(i => i.Kind == DiffLineKind.Removed).ToList();
            var added = items.Skip(index).Take(blockEnd - index).Where(i => i.Kind == DiffLineKind.Added).ToList();
            anySelected |= removed.Any(i => i.Selected) || added.Any(i => i.Selected);
            if (!reverse)
            {
                // Target is the old side: unselected removals stay (as context), unselected additions never existed there.
                foreach (var item in removed)
                {
                    Emit(item.Selected ? '-' : ' ', item);
                }

                foreach (var item in added.Where(i => i.Selected))
                {
                    Emit('+', item);
                }
            }
            else
            {
                // Target is the new side: unselected additions stay (as context), unselected removals are not restored.
                // Restored lines go right before the first selected addition so replaced lines stay paired.
                int firstSelectedAdd = added.FindIndex(i => i.Selected);
                int restoreAt = firstSelectedAdd < 0 ? 0 : firstSelectedAdd;
                for (int i = 0; i < added.Count; i++)
                {
                    if (i == restoreAt)
                    {
                        foreach (var item in removed.Where(r => r.Selected))
                        {
                            Emit('-', item);
                        }
                    }

                    Emit(added[i].Selected ? '+' : ' ', added[i]);
                }

                if (restoreAt >= added.Count)
                {
                    foreach (var item in removed.Where(r => r.Selected))
                    {
                        Emit('-', item);
                    }
                }
            }

            index = blockEnd;
        }

        if (!anySelected)
        {
            return null;
        }

        Match match = HunkHeader().Match(raw[headerIndex]);
        int origOldStart = int.Parse(match.Groups["os"].Value, CultureInfo.InvariantCulture);
        int origNewStart = int.Parse(match.Groups["ns"].Value, CultureInfo.InvariantCulture);
        int origOldCount = match.Groups["oc"].Success ? int.Parse(match.Groups["oc"].Value, CultureInfo.InvariantCulture) : 1;
        int origNewCount = match.Groups["nc"].Success ? int.Parse(match.Groups["nc"].Value, CultureInfo.InvariantCulture) : 1;

        // A zero count means "after line N"; work with the first affected line and convert back.
        int effectiveOld = origOldCount == 0 ? origOldStart + 1 : origOldStart;
        int effectiveNew = origNewCount == 0 ? origNewStart + 1 : origNewStart;
        if (reverse)
        {
            // The target is the new side (all hunks applied); earlier included hunks shift the old coordinates.
            effectiveOld = effectiveNew - delta;
        }
        else
        {
            effectiveNew = effectiveOld + delta;
        }

        int oldStart = oldCount == 0 ? effectiveOld - 1 : effectiveOld;
        int newStart = newCount == 0 ? effectiveNew - 1 : effectiveNew;
        string header = string.Create(CultureInfo.InvariantCulture, $"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@{match.Groups["rest"].Value}\n");
        return (header + body, oldCount, newCount);
    }
}
