using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>Which git configuration file a settings page edits.</summary>
public enum SettingsSource
{
    Global,
    Local,
    Effective,
}

/// <summary>
///  Reads and writes one scope of the git configuration through <c>git config</c>. Edits are staged with <see cref="Set"/>
///  and written by <see cref="FlushAsync"/>. The effective scope is read-only.
/// </summary>
public sealed class GitConfigStore(Func<string> gitPath, Func<string?> repositoryDirectory)
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);

    public SettingsSource Source { get; set; }

    public bool IsReadOnly => Source == SettingsSource.Effective;

    public string Get(string key) => _values.TryGetValue(Normalize(key), out string? value) ? value : string.Empty;

    /// <summary>The staged value if there is one, else the loaded one.</summary>
    public string Current(string key) => _pending.TryGetValue(Normalize(key), out string? value) ? value : Get(key);

    public void Set(string key, string value)
    {
        if (!IsReadOnly)
        {
            _pending[Normalize(key)] = value.Trim();
        }
    }

    /// <summary>Reloads the values of <see cref="Source"/> and drops staged edits.</summary>
    public async Task LoadAsync()
    {
        _pending.Clear();
        _values.Clear();
        string? repository = repositoryDirectory();
        if (Source != SettingsSource.Global && repository is null)
        {
            return;
        }

        string scope = Source switch { SettingsSource.Global => "--global", SettingsSource.Local => "--local", _ => "--includes" };
        GitCli.Result result = await GitCli.RunAsync(gitPath(), Source == SettingsSource.Global ? null : repository, "config", scope, "--list", "-z");

        // A missing file is not an error: exit code 1 with "unable to read config file".
        if (!result.Succeeded)
        {
            return;
        }

        foreach (string entry in result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int newline = entry.IndexOf('\n');
            string key = newline < 0 ? entry : entry[..newline];
            _values[key] = newline < 0 ? "true" : entry[(newline + 1)..];
        }
    }

    public async Task FlushAsync()
    {
        if (_pending.Count == 0 || IsReadOnly)
        {
            return;
        }

        string? repository = repositoryDirectory();
        string scope = Source == SettingsSource.Global ? "--global" : "--local";
        string? workingDir = Source == SettingsSource.Global ? null : repository;
        foreach ((string key, string value) in _pending.ToArray())
        {
            if (string.Equals(Get(key), value, StringComparison.Ordinal))
            {
                continue;
            }

            GitCli.Result result = value.Length == 0
                ? await GitCli.RunAsync(gitPath(), workingDir, "config", scope, "--unset-all", key)
                : await GitCli.RunAsync(gitPath(), workingDir, "config", scope, key, value);

            // Exit code 5: unsetting a key that is not set.
            if (!result.Succeeded && !(value.Length == 0 && result.ExitCode == 5))
            {
                throw new InvalidOperationException($"git config {key} failed: {result.Error}");
            }

            _values[key] = value;
            if (value.Length == 0)
            {
                _values.Remove(key);
            }
        }

        _pending.Clear();
    }

    // Section and variable names are case-insensitive and stored lower-case by git; subsections keep their case.
    private static string Normalize(string key)
    {
        int first = key.IndexOf('.');
        int last = key.LastIndexOf('.');
        if (first < 0)
        {
            return key.ToLowerInvariant();
        }

        return first == last
            ? key.ToLowerInvariant()
            : key[..first].ToLowerInvariant() + key[first..last] + key[last..].ToLowerInvariant();
    }
}
