using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  The answer to an askpass request. <see langword="null"/> means the user cancelled.
/// </summary>
public sealed record AskPassResponse(string? Reply)
{
    public static AskPassResponse Cancelled { get; } = new((string?)null);
}

/// <summary>
///  One running git command's registration with the <see cref="AskPassServer"/>. It owns a random token that only that
///  command's environment knows, so a prompt can be tied to (and closed with) its operation.
/// </summary>
public sealed class AskPassOperation : IDisposable
{
    private readonly AskPassServer _server;
    private readonly CancellationTokenSource _cancellation;
    private int _userCancelled;

    internal AskPassOperation(AskPassServer server, string token, CancellationToken cancellationToken)
    {
        _server = server;
        Token = token;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    internal string Token { get; }

    internal CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>Gets a value indicating whether the user dismissed a prompt of this operation.</summary>
    public bool UserCancelled => Volatile.Read(ref _userCancelled) != 0;

    /// <summary>Gets the environment variables to give the git process.</summary>
    public IReadOnlyDictionary<string, string> Environment => _server.CreateEnvironment(this);

    internal void MarkUserCancelled() => Volatile.Write(ref _userCancelled, 1);

    public void Dispose()
    {
        _server.Unregister(this);
        _cancellation.Dispose();
    }
}

/// <summary>
///  Per-session local server behind GIT_ASKPASS / SSH_ASKPASS. Listens on a unix domain socket inside a private
///  (mode 0700) temp directory, or on a current-user-only named pipe on Windows. A request is accepted only with the
///  token of a registered, still running operation; the handler (the in-window prompt) produces the answer.
/// </summary>
public sealed class AskPassServer : IDisposable
{
    private const int MaxLineLength = 16 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<AskPassPrompt, CancellationToken, Task<AskPassResponse>> _handler;
    private readonly string _helperPath;
    private readonly ConcurrentDictionary<string, AskPassOperation> _operations = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private readonly string? _directory;
    private Socket? _listener;
    private Task? _acceptLoop;

    /// <param name="handler">Answers a classified prompt; may be called concurrently from background threads.</param>
    /// <param name="helperPath">The askpass helper executable git should run.</param>
    public AskPassServer(Func<AskPassPrompt, CancellationToken, Task<AskPassResponse>> handler, string helperPath)
    {
        _handler = handler;
        _helperPath = helperPath;
        if (OperatingSystem.IsWindows())
        {
            Endpoint = "GitExtensions.AskPass." + RandomToken();
        }
        else
        {
            _directory = Directory.CreateTempSubdirectory("ge-askpass-").FullName;
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Endpoint = Path.Combine(_directory, "s");
        }
    }

    /// <summary>Gets the socket path (unix) or pipe name (Windows) the helper connects to.</summary>
    public string Endpoint { get; }

    public static string RandomToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public void Start()
    {
        if (_acceptLoop is not null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(Endpoint));
            File.SetUnixFileMode(Endpoint, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _listener.Listen(16);
        }

        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>
    ///  Registers a running command. Dispose the result when the command ends.
    /// </summary>
    public AskPassOperation BeginOperation(CancellationToken cancellationToken)
    {
        Start();
        AskPassOperation operation = new(this, RandomToken(), cancellationToken);
        _operations[operation.Token] = operation;
        return operation;
    }

    internal IReadOnlyDictionary<string, string> CreateEnvironment(AskPassOperation operation)
        => new Dictionary<string, string>
        {
            ["GIT_ASKPASS"] = _helperPath,
            ["SSH_ASKPASS"] = _helperPath,
            ["SSH_ASKPASS_REQUIRE"] = "force",
            ["GE_ASKPASS_ENDPOINT"] = Endpoint,
            ["GE_ASKPASS_TOKEN"] = operation.Token,
        };

    internal void Unregister(AskPassOperation operation) => _operations.TryRemove(operation.Token, out _);

    public void Dispose()
    {
        _stop.Cancel();
        foreach (AskPassOperation operation in _operations.Values)
        {
            _operations.TryRemove(operation.Token, out _);
        }

        _listener?.Dispose();
        try
        {
#pragma warning disable VSTHRD002 // Shutdown path: bounded wait for the accept loop.
            _acceptLoop?.Wait(TimeSpan.FromSeconds(2));
#pragma warning restore VSTHRD002
        }
        catch (AggregateException)
        {
        }

        _stop.Dispose();
        if (_directory is not null)
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Stream? stream = null;
            try
            {
                stream = await AcceptAsync();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException or SocketException or IOException)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            _ = Task.Run(() => HandleAsync(stream));
        }
    }

    private async Task<Stream> AcceptAsync()
    {
        if (_listener is not null)
        {
            Socket client = await _listener.AcceptAsync(_stop.Token);
            return new NetworkStream(client, ownsSocket: true);
        }

        NamedPipeServerStream pipe = new(Endpoint, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.WaitForConnectionAsync(_stop.Token);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    private async Task HandleAsync(Stream stream)
    {
        await using (stream)
        {
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(RequestTimeout);
                string? token = await ReadLineAsync(stream, timeout.Token);
                string? encodedPrompt = await ReadLineAsync(stream, timeout.Token);
                if (token is null || encodedPrompt is null || !_operations.TryGetValue(token, out AskPassOperation? operation) || !FixedEquals(token, operation.Token))
                {
                    await WriteAsync(stream, "DENIED\n");
                    return;
                }

                string prompt = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPrompt));
                AskPassResponse response;
                try
                {
                    response = await _handler(AskPassPromptClassifier.Classify(prompt), operation.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    response = AskPassResponse.Cancelled;
                }

                if (response.Reply is null)
                {
                    if (!operation.CancellationToken.IsCancellationRequested)
                    {
                        operation.MarkUserCancelled();
                    }

                    await WriteAsync(stream, "CANCEL\n");
                    return;
                }

                byte[] reply = Encoding.UTF8.GetBytes(response.Reply);
                byte[] line = Encoding.ASCII.GetBytes("OK " + Convert.ToBase64String(reply) + "\n");
                try
                {
                    await stream.WriteAsync(line, _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                }
                finally
                {
                    Array.Clear(reply);
                    Array.Clear(line);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or FormatException or ObjectDisposedException or DecoderFallbackException)
            {
                // Misbehaving or vanished client: drop the connection.
            }
        }
    }

    private static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static Task WriteAsync(Stream stream, string text) => stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        List<byte> bytes = [];
        byte[] one = new byte[1];
        while (await stream.ReadAsync(one, cancellationToken) == 1)
        {
            if (one[0] == '\n')
            {
                return Encoding.UTF8.GetString([.. bytes]);
            }

            if (bytes.Count >= MaxLineLength)
            {
                return null;
            }

            bytes.Add(one[0]);
        }

        return null;
    }
}
