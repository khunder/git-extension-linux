using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CredentialPromptViewModel? _credentials;
    private AskPassService? _askPass;

    /// <summary>
    ///  The in-window prompt for credentials, passphrases and host key confirmations requested by git and ssh.
    /// </summary>
    public CredentialPromptViewModel Credentials => _credentials ??= new CredentialPromptViewModel();

    /// <summary>
    ///  Gives background git commands the askpass environment so they prompt in the window instead of failing.
    /// </summary>
    public IAskPassProvider AskPass
    {
        get
        {
            if (_askPass is null)
            {
                _askPass = new AskPassService(Credentials);
                AppDomain.CurrentDomain.ProcessExit += (_, _) => _askPass.Dispose();
            }

            return _askPass;
        }
    }
}
