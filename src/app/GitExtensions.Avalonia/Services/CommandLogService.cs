using GitCommands.Logging;

namespace GitExtensions.Desktop.Services;

/// <summary>One git command in the console tab. Updated in place while the command runs.</summary>
public sealed class CommandLogItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string _text = string.Empty;
    private bool _isError;

    public CommandLogItem(CommandLogEntry entry)
    {
        Entry = entry;
        Update();
    }

    public CommandLogEntry Entry { get; }

    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public bool IsError
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    /// <summary>Formats an entry as one console line: time, duration, exit code, command and working directory.</summary>
    public static string Format(CommandLogEntry entry)
    {
        string duration = entry.Duration is { } elapsed ? $"{elapsed.TotalMilliseconds:0} ms" : "running";
        string exit = entry.ExitCode is { } code ? $"exit {code}" : entry.Exception is not null ? "failed" : string.Empty;
        string command = entry.FileName.EndsWith("git", StringComparison.OrdinalIgnoreCase) || entry.FileName.EndsWith("git.exe", StringComparison.OrdinalIgnoreCase)
            ? $"git {CommandLogEntry.GetGitArgumentsWithoutConfiguration(entry.Arguments)}"
            : entry.CommandLine;
        string error = entry.Exception is not null ? $"  {entry.Exception.GetType().Name}: {entry.Exception.Message}" : string.Empty;
        return $"{entry.StartedAt:HH:mm:ss.fff}  {duration,8}  {exit,-7}  {command}{error}  [{entry.WorkingDir}]";
    }

    public void Update()
    {
        Text = Format(Entry);
        IsError = Entry.ExitCode is not (null or 0) || Entry.Exception is not null;
    }
}

/// <summary>
///  The live log of the git commands the app ran, fed by the portable <see cref="CommandLog"/>.
///  The log raises its change event from whichever thread runs git, often: changes are coalesced into one UI refresh.
/// </summary>
public sealed class CommandLogService : IDisposable
{
    /// <summary>The most entries kept (the ring buffer of <see cref="CommandLog"/> is bounded as well).</summary>
    public const int MaxEntries = 500;

    private readonly Dictionary<CommandLogEntry, CommandLogItem> _items = [];
    private int _refreshQueued;
    private bool _disposed;

    public CommandLogService()
    {
        CommandLog.CommandsChanged += OnCommandsChanged;
        Refresh();
    }

    /// <summary>The entries to show, oldest first. Only touched on the UI thread.</summary>
    public ViewModels.RangeObservableCollection<CommandLogItem> Items { get; } = [];

    /// <summary>Empties the log (and the portable log).</summary>
    public void Clear()
    {
        CommandLog.Clear();
        Refresh();
    }

    /// <summary>Everything shown, as text lines.</summary>
    public string GetText() => string.Join(Environment.NewLine, Items.Select(item => item.Text));

    /// <summary>Synchronises <see cref="Items"/> with the log. UI thread only.</summary>
    public void Refresh()
    {
        Volatile.Write(ref _refreshQueued, 0);
        if (_disposed)
        {
            return;
        }

        CommandLogEntry[] entries = [.. CommandLog.Commands];
        if (entries.Length > MaxEntries)
        {
            entries = entries[^MaxEntries..];
        }

        HashSet<CommandLogEntry> current = [.. entries];
        bool removed = _items.Keys.Any(entry => !current.Contains(entry));
        List<CommandLogItem> added = [];
        foreach (CommandLogEntry entry in entries)
        {
            if (_items.TryGetValue(entry, out CommandLogItem? item))
            {
                item.Update();
            }
            else
            {
                item = new CommandLogItem(entry);
                _items[entry] = item;
                added.Add(item);
            }
        }

        if (removed)
        {
            foreach (CommandLogEntry entry in _items.Keys.Where(entry => !current.Contains(entry)).ToList())
            {
                _items.Remove(entry);
            }

            Items.ReplaceAll(entries.Select(entry => _items[entry]));
        }
        else
        {
            Items.AddRange(added);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CommandLog.CommandsChanged -= OnCommandsChanged;
    }

    private void OnCommandsChanged()
    {
        // Many events arrive during a burst of git calls: one queued refresh covers all of them.
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Refresh, Avalonia.Threading.DispatcherPriority.Background);
        }
    }
}
