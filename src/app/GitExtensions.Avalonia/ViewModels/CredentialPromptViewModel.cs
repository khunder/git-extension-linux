using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The in-window credential / passphrase / host key prompt. Prompts arriving together are shown one after the other.
///  Secrets are never logged; <see cref="InputText"/> is cleared as soon as the prompt closes.
/// </summary>
public sealed partial class CredentialPromptViewModel : ObservableObject
{
    // Captured on the creating (UI) thread: callers run on pool threads, and touching the static Dispatcher.UIThread there
    // can create a bogus dispatcher while a headless test session is being set up.
    private readonly Avalonia.Threading.Dispatcher _dispatcher = Avalonia.Threading.Dispatcher.UIThread;

    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly Dictionary<string, string> _remembered = new(StringComparer.Ordinal);
    private TaskCompletionSource<string?>? _pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTextInput))]
    [NotifyPropertyChangedFor(nameof(ShowRemember))]
    [NotifyPropertyChangedFor(nameof(ConfirmText))]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _promptText = string.Empty;

    [ObservableProperty]
    private string? _target;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _rememberForSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTextInput))]
    [NotifyPropertyChangedFor(nameof(ShowRemember))]
    [NotifyPropertyChangedFor(nameof(ConfirmText))]
    private AskPassPromptKind _kind;

    [ObservableProperty]
    private char _passwordChar;

    public bool ShowTextInput => Kind != AskPassPromptKind.Confirmation;

    public bool ShowRemember => Kind != AskPassPromptKind.Confirmation;

    public string ConfirmText => Kind == AskPassPromptKind.Confirmation ? "Yes, continue" : "OK";

    /// <summary>
    ///  Shows the prompt (on the UI thread) and completes with the answer, or a cancelled response if the user cancels
    ///  or <paramref name="cancellationToken"/> fires (the operation was cancelled). Call from any thread.
    /// </summary>
    public async Task<AskPassResponse> RequestAsync(AskPassPrompt prompt, CancellationToken cancellationToken)
    {
        await _queue.WaitAsync(cancellationToken);
        try
        {
            string? reply = await _dispatcher.InvokeAsync(() => ShowAsync(prompt, cancellationToken));
            return new AskPassResponse(reply);
        }
        finally
        {
            _queue.Release();
        }
    }

    private async Task<string?> ShowAsync(AskPassPrompt prompt, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (_remembered.TryGetValue(prompt.Text, out string? known))
        {
            return known;
        }

        TaskCompletionSource<string?> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        Title = prompt.Title;
        PromptText = prompt.Text;
        Target = prompt.Target;
        Kind = prompt.Kind;
        PasswordChar = prompt.IsSecret ? '●' : '\0';
        InputText = string.Empty;
        RememberForSession = false;
        IsOpen = true;

        using CancellationTokenRegistration registration = cancellationToken.Register(() => _dispatcher.Post(() => Complete(pending, null)));
        string? answer = await pending.Task;
        return answer;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (_pending is null)
        {
            return;
        }

        string answer = Kind == AskPassPromptKind.Confirmation ? "yes" : InputText;
        if (RememberForSession && Kind != AskPassPromptKind.Confirmation)
        {
            _remembered[PromptText] = answer;
        }

        Complete(_pending, answer);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_pending is not null)
        {
            // A cancelled answer makes the askpass helper exit non-zero, which makes git/ssh fail cleanly.
            Complete(_pending, null);
        }
    }

    /// <summary>
    ///  Forgets answers remembered for this session.
    /// </summary>
    public void ForgetRemembered() => _remembered.Clear();

    private void Complete(TaskCompletionSource<string?> pending, string? answer)
    {
        if (!ReferenceEquals(_pending, pending))
        {
            return;
        }

        _pending = null;
        IsOpen = false;
        InputText = string.Empty;
        PromptText = string.Empty;
        pending.TrySetResult(answer);
    }
}
