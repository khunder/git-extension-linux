using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Hands a running git command the askpass environment.
/// </summary>
public interface IAskPassProvider
{
    /// <summary>
    ///  Registers a command that is about to start, or returns <see langword="null"/> if prompting is unavailable.
    ///  Dispose the result when the command ends.
    /// </summary>
    AskPassOperation? Begin(CancellationToken cancellationToken);
}

/// <summary>
///  The application's askpass bridge: a lazily started <see cref="AskPassServer"/> whose prompts are shown by a <see cref="CredentialPromptViewModel"/>.
/// </summary>
public sealed class AskPassService : IAskPassProvider, IDisposable
{
    private readonly CredentialPromptViewModel _prompt;
    private readonly string? _helperPath;
    private readonly object _lock = new();
    private AskPassServer? _server;

    public AskPassService(CredentialPromptViewModel prompt, string? helperPath = null)
    {
        _prompt = prompt;
        _helperPath = helperPath ?? LocateHelper();
    }

    /// <summary>
    ///  Gets the helper executable next to the application, or <see langword="null"/> if it was not deployed.
    /// </summary>
    public static string? LocateHelper()
    {
        string path = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "GitExtensions.AskPass.exe" : "GitExtensions.AskPass");
        return File.Exists(path) ? path : null;
    }

    public AskPassOperation? Begin(CancellationToken cancellationToken)
    {
        if (_helperPath is null)
        {
            return null;
        }

        try
        {
            lock (_lock)
            {
                _server ??= new AskPassServer(_prompt.RequestAsync, _helperPath);
                return _server.BeginOperation(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
        {
            // No private channel available: behave as before (git fails fast without prompting).
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _server?.Dispose();
            _server = null;
        }
    }
}
