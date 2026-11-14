using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class AskPassTests
{
    private const string Secret = "S3cr3t-Passw0rd!";

    private string? _globalConfig;
    private string? _oldGlobal;
    private string? _oldNoSystem;

    [OneTimeSetUp]
    public void IsolateGit()
    {
        // The user's credential helpers must never take part in these tests.
        _globalConfig = Path.Combine(Path.GetTempPath(), "ge-askpass-gitconfig-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(_globalConfig, string.Empty);
        _oldGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        _oldNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _globalConfig);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
    }

    [OneTimeTearDown]
    public void RestoreGit()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _oldGlobal);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", _oldNoSystem);
        File.Delete(_globalConfig!);
    }

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    /// <summary>
    ///  Answers 401 until the right Basic credentials arrive, then serves an empty smart-HTTP advertisement.
    /// </summary>
    private sealed class AuthHttpServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public AuthHttpServer(string user, string password)
        {
            int port;
            using (TcpListener probe = new(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }

            Url = $"http://127.0.0.1:{port}/repo.git";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));
            _loop = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                    {
                        return;
                    }

                    string? authorization = context.Request.Headers["Authorization"];
                    if (authorization != expected)
                    {
                        if (authorization is not null)
                        {
                            Interlocked.Increment(ref _rejected);
                        }

                        context.Response.StatusCode = 401;
                        context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"test\"");
                    }
                    else
                    {
                        Interlocked.Increment(ref _accepted);
                        context.Response.ContentType = "application/x-git-upload-pack-advertisement";
                        string body = PktLine("# service=git-upload-pack\n") + "0000"
                            + PktLine("0000000000000000000000000000000000000000 capabilities^{}\0side-band-64k\n") + "0000";
                        byte[] bytes = Encoding.ASCII.GetBytes(body);
                        context.Response.OutputStream.Write(bytes);
                    }

                    context.Response.Close();
                }
            });
        }

        private int _accepted;
        private int _rejected;

        public string Url { get; }

        public int Accepted => _accepted;

        public int Rejected => _rejected;

        private static string PktLine(string text) => (text.Length + 4).ToString("x4") + text;

        public void Dispose()
        {
            _listener.Close();
            _loop.GetAwaiter().OnCompleted(static () => { });
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 20_000)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition())
        {
            watch.ElapsedMilliseconds.Should().BeLessThan(timeoutMs, "the condition should become true");
            await Task.Delay(20);
        }
    }

    private static (int ExitCode, string Stdout) RunHelper(string endpoint, string? token, string prompt)
    {
        ProcessStartInfo startInfo = new(AskPassService.LocateHelper()!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        startInfo.ArgumentList.Add(prompt);
        startInfo.Environment["GE_ASKPASS_ENDPOINT"] = endpoint;
        if (token is not null)
        {
            startInfo.Environment["GE_ASKPASS_TOKEN"] = token;
        }

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [TestCase("Username for 'https://github.com': ", AskPassPromptKind.Username, false, "https://github.com")]
    [TestCase("Password for 'https://u@github.com': ", AskPassPromptKind.Password, true, "https://u@github.com")]
    [TestCase("Enter passphrase for key '/home/u/.ssh/id_ed25519': ", AskPassPromptKind.Passphrase, true, "/home/u/.ssh/id_ed25519")]
    [TestCase("Enter passphrase for key '/home/u/.ssh/password_key': ", AskPassPromptKind.Passphrase, true, "/home/u/.ssh/password_key")]
    [TestCase("u@host's password: ", AskPassPromptKind.Password, true, null)]
    [TestCase("Enter PIN for 'PIV Card Holder pin (PIV_II)': ", AskPassPromptKind.Password, true, "PIV Card Holder pin (PIV_II)")]
    [TestCase("Are you sure you want to continue connecting (yes/no/[fingerprint])? ", AskPassPromptKind.Confirmation, false, null)]
    [TestCase("The authenticity of host 'h (1.2.3.4)' can't be established.\nED25519 key fingerprint is SHA256:abc.\nAre you sure you want to continue connecting (yes/no/[fingerprint])?", AskPassPromptKind.Confirmation, false, "h (1.2.3.4)")]
    [TestCase("Something unexpected", AskPassPromptKind.Other, true, null)]
    public void Classifier_recognizes_typical_prompts(string prompt, AskPassPromptKind kind, bool secret, string? target)
    {
        AskPassPrompt result = AskPassPromptClassifier.Classify(prompt);
        result.Kind.Should().Be(kind);
        result.IsSecret.Should().Be(secret);
        result.Target.Should().Be(target);
        result.Text.Should().Be(prompt.Trim());
    }

    [Test]
    public async Task Server_round_trip_with_the_helper_returns_the_reply()
    {
        AskPassService.LocateHelper().Should().NotBeNull("the helper is deployed next to the app");
        List<AskPassPrompt> seen = [];
        using AskPassServer server = new(
            (prompt, _) =>
            {
                seen.Add(prompt);
                return Task.FromResult(new AskPassResponse("pässwörd ✓"));
            },
            AskPassService.LocateHelper()!);
        using AskPassOperation operation = server.BeginOperation(CancellationToken.None);

        (int exit, string stdout) = await Task.Run(() => RunHelper(server.Endpoint, operation.Environment["GE_ASKPASS_TOKEN"], "Password for 'https://u@h': "));

        exit.Should().Be(0);
        stdout.Should().Be("pässwörd ✓\n");
        seen.Should().ContainSingle().Which.Kind.Should().Be(AskPassPromptKind.Password);
        operation.UserCancelled.Should().BeFalse();
        operation.Environment.Should().ContainKey("GIT_ASKPASS").And.ContainKey("SSH_ASKPASS").And.ContainKey("SSH_ASKPASS_REQUIRE");
    }

    [Test]
    public async Task Server_cancelled_response_makes_the_helper_exit_with_failure()
    {
        using AskPassServer server = new((_, _) => Task.FromResult(AskPassResponse.Cancelled), AskPassService.LocateHelper()!);
        using AskPassOperation operation = server.BeginOperation(CancellationToken.None);

        (int exit, string stdout) = await Task.Run(() => RunHelper(server.Endpoint, operation.Environment["GE_ASKPASS_TOKEN"], "Username: "));

        exit.Should().Be(1);
        stdout.Should().BeEmpty();
        operation.UserCancelled.Should().BeTrue();
    }

    [Test]
    public async Task Server_refuses_wrong_unknown_and_disposed_tokens()
    {
        int handled = 0;
        using AskPassServer server = new(
            (_, _) =>
            {
                Interlocked.Increment(ref handled);
                return Task.FromResult(new AskPassResponse("x"));
            },
            AskPassService.LocateHelper()!);
        AskPassOperation operation = server.BeginOperation(CancellationToken.None);
        string good = operation.Environment["GE_ASKPASS_TOKEN"];

        (await Task.Run(() => RunHelper(server.Endpoint, "00" + good[2..], "Username: "))).ExitCode.Should().Be(1);
        (await Task.Run(() => RunHelper(server.Endpoint, string.Empty, "Username: "))).ExitCode.Should().Be(2, "no token at all: not started by Git Extensions");

        operation.Dispose();
        (await Task.Run(() => RunHelper(server.Endpoint, good, "Username: "))).ExitCode.Should().Be(1, "a finished operation's token is dead");
        handled.Should().Be(0);

        if (!OperatingSystem.IsWindows())
        {
            // A raw client sees an explicit refusal; the socket lives in a directory only the current user can enter.
            using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(server.Endpoint));
            await socket.SendAsync(Encoding.UTF8.GetBytes("nope\n" + Convert.ToBase64String("p"u8.ToArray()) + "\n"));
            byte[] buffer = new byte[64];
            int read = await socket.ReceiveAsync(buffer);
            Encoding.ASCII.GetString(buffer, 0, read).Should().Be("DENIED\n");

            string directory = Path.GetDirectoryName(server.Endpoint)!;
            File.GetUnixFileMode(directory).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.GetUnixFileMode(server.Endpoint).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Test]
    public void Server_removes_its_private_directory_on_dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix domain socket directory only.");
        }

        AskPassServer server = new((_, _) => Task.FromResult(AskPassResponse.Cancelled), "/bin/true");
        server.Start();
        string directory = Path.GetDirectoryName(server.Endpoint)!;
        Directory.Exists(directory).Should().BeTrue();
        server.Dispose();
        Directory.Exists(directory).Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Prompt_view_model_shows_one_prompt_at_a_time_and_remembers_for_the_session()
    {
        CredentialPromptViewModel prompt = new();
        AskPassPrompt password = AskPassPromptClassifier.Classify("Password for 'https://u@h': ");

        Task<AskPassResponse> first = prompt.RequestAsync(password, CancellationToken.None);
        await WaitUntilAsync(() => prompt.IsOpen);
        prompt.PasswordChar.Should().NotBe('\0');
        prompt.ShowRemember.Should().BeTrue();
        Task<AskPassResponse> second = prompt.RequestAsync(AskPassPromptClassifier.Classify("Username for 'https://h': "), CancellationToken.None);
        await Task.Delay(100);
        second.IsCompleted.Should().BeFalse("prompts are shown one after the other");

        prompt.InputText = Secret;
        prompt.RememberForSession = true;
        prompt.ConfirmCommand.Execute(null);
        (await first).Reply.Should().Be(Secret);
        prompt.InputText.Should().BeEmpty("the secret is cleared as soon as the prompt closes");

        await WaitUntilAsync(() => prompt.IsOpen && prompt.Kind == AskPassPromptKind.Username);
        prompt.PasswordChar.Should().Be('\0');
        prompt.CancelCommand.Execute(null);
        (await second).Reply.Should().BeNull();

        // Remembered: the same prompt text is answered without showing the card again.
        (await prompt.RequestAsync(password, CancellationToken.None)).Reply.Should().Be(Secret);
        prompt.IsOpen.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Prompt_view_model_closes_when_the_operation_is_cancelled()
    {
        CredentialPromptViewModel prompt = new();
        using CancellationTokenSource cancellation = new();
        Task<AskPassResponse> request = prompt.RequestAsync(AskPassPromptClassifier.Classify("Username for 'https://h': "), cancellation.Token);
        await WaitUntilAsync(() => prompt.IsOpen);

        await cancellation.CancelAsync();

        (await request).Reply.Should().BeNull();
        await WaitUntilAsync(() => !prompt.IsOpen);
    }

    [AvaloniaTest]
    public async Task Git_uses_the_typed_credentials_through_the_in_window_prompt()
    {
        using AuthHttpServer http = new("alice", Secret);
        using TestRepository repository = new();
        CredentialPromptViewModel prompt = new();
        using AskPassService askPass = new(prompt);
        GitEnvironment git = GitEnvironment.Initialize();
        StringBuilder output = new();

        using CancellationTokenSource cancellation = new();
        using AskPassOperation operation = askPass.Begin(cancellation.Token)!;
        Task<int> run = Task.Run(() => GitProcessRunner.RunAsync(
            git.OpenRepository(repository.Path)!.GitExecutable,
            ["-c", "credential.helper=", "ls-remote", http.Url],
            chunk =>
            {
                lock (output)
                {
                    output.Append(chunk);
                }
            },
            cancellation.Token,
            operation));

        await WaitUntilAsync(() => prompt.IsOpen);
        prompt.Kind.Should().Be(AskPassPromptKind.Username);
        prompt.PromptText.Should().StartWith("Username for 'http://127.0.0.1:");
        prompt.PasswordChar.Should().Be('\0');
        prompt.InputText = "alice";
        prompt.ConfirmCommand.Execute(null);

        await WaitUntilAsync(() => prompt.IsOpen);
        prompt.Kind.Should().Be(AskPassPromptKind.Password);
        prompt.PasswordChar.Should().NotBe('\0');
        prompt.InputText = Secret;
        prompt.ConfirmCommand.Execute(null);

        (await run).Should().Be(0);
        http.Accepted.Should().BeGreaterThan(0);
        http.Rejected.Should().Be(0);
        lock (output)
        {
            output.ToString().Should().NotContain(Secret);
        }
    }

    [AvaloniaTest]
    public async Task Cancelling_the_prompt_makes_git_fail_cleanly_with_a_clear_message()
    {
        using AuthHttpServer http = new("alice", Secret);
        using TestRepository repository = new();
        repository.GitOutput("config", "credential.helper", string.Empty);
        repository.GitOutput("remote", "add", "origin", http.Url);
        CredentialPromptViewModel prompt = new();
        using AskPassService askPass = new(prompt);
        GitEnvironment git = GitEnvironment.Initialize();
        List<string> errors = [];
        GitCommands.GitModule module = git.OpenRepository(repository.Path)!;
        RemoteOperationsViewModel remote = new(() => module, _ => { }, new Recording(errors), null, askPass);
        await remote.ReloadStateAsync();

        Task fetch = remote.FetchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => prompt.IsOpen);
        prompt.CancelCommand.Execute(null);
        await fetch;

        remote.IsRunning.Should().BeFalse();
        remote.OutputText.Should().Contain("Fetch failed").And.Contain("credential prompt was cancelled");
        errors.Should().ContainSingle().Which.Should().Contain("cancelled");
        prompt.IsOpen.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Cancelling_the_operation_closes_the_open_prompt()
    {
        using AuthHttpServer http = new("alice", Secret);
        using TestRepository repository = new();
        repository.GitOutput("config", "credential.helper", string.Empty);
        repository.GitOutput("remote", "add", "origin", http.Url);
        CredentialPromptViewModel prompt = new();
        using AskPassService askPass = new(prompt);
        GitEnvironment git = GitEnvironment.Initialize();
        GitCommands.GitModule module = git.OpenRepository(repository.Path)!;
        RemoteOperationsViewModel remote = new(() => module, _ => { }, new Recording([]), null, askPass);
        await remote.ReloadStateAsync();

        Task fetch = remote.FetchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => prompt.IsOpen);
        remote.CancelCommand.Execute(null);
        await fetch;

        prompt.IsOpen.Should().BeFalse();
        remote.IsRunning.Should().BeFalse();
        remote.OutputText.Should().Contain("Fetch cancelled.");
    }

    [AvaloniaTest]
    public async Task A_wrong_password_fails_with_an_authentication_error_and_never_reaches_the_output()
    {
        using AuthHttpServer http = new("alice", Secret);
        using TestRepository repository = new();
        repository.GitOutput("config", "credential.helper", string.Empty);
        repository.GitOutput("remote", "add", "origin", http.Url);
        CredentialPromptViewModel prompt = new();
        using AskPassService askPass = new(prompt);
        GitEnvironment git = GitEnvironment.Initialize();
        GitCommands.GitModule module = git.OpenRepository(repository.Path)!;
        RemoteOperationsViewModel remote = new(() => module, _ => { }, new Recording([]), null, askPass);
        await remote.ReloadStateAsync();

        const string wrong = "WrongPassw0rd-xyz";
        Task fetch = remote.FetchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => prompt.IsOpen);
        prompt.InputText = "alice";
        prompt.ConfirmCommand.Execute(null);
        await WaitUntilAsync(() => prompt.IsOpen && prompt.Kind == AskPassPromptKind.Password);
        prompt.InputText = wrong;
        prompt.ConfirmCommand.Execute(null);
        await fetch;

        http.Rejected.Should().BeGreaterThan(0);
        remote.OutputText.Should().Contain("Authentication failed").And.Contain("check the credentials").And.NotContain(wrong).And.NotContain(Secret);
    }

    [AvaloniaTest]
    public async Task Credential_overlay_renders_with_a_masked_field_in_light_and_dark()
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1100, Height = 700 };
        window.Show();
        Task<AskPassResponse> request = viewModel.Credentials.RequestAsync(AskPassPromptClassifier.Classify("Password for 'https://octocat@github.com': "), CancellationToken.None);
        await WaitUntilAsync(() => viewModel.Credentials.IsOpen);
        viewModel.Credentials.InputText = "hunter2-hunter2";

        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        foreach ((ThemeVariant variant, string name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            window.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            WriteableBitmap? frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            string path = Path.Combine(directory, $"credential-prompt-{name}.png");
            frame!.Save(path);
            TestContext.AddTestAttachment(path);
        }

        TextBox input = window.FindControl<CredentialPromptView>("CredentialOverlay")!.FindControl<TextBox>("InputBox")!;
        input.PasswordChar.Should().Be('●');

        viewModel.Credentials.CancelCommand.Execute(null);
        (await request).Reply.Should().BeNull();
        window.Close();
    }

    private sealed class Recording(List<string> errors) : IMessageDialogs
    {
        public void ShowError(object? owner, string text, string? caption = null) => errors.Add($"{caption}: {text}");

        public void ShowWarning(object? owner, string text, string caption) => errors.Add($"{caption}: {text}");
    }
}
