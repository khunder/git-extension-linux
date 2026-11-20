namespace GitExtensionsDesktopTests;

/// <summary>
///  Redirects user settings (AppSettings, recent repositories, window state) to a temp folder
///  before anything touches them, so tests never read or write the real user's configuration.
/// </summary>
[SetUpFixture]
public sealed class IsolatedConfig
{
    private string? _directory;

    public static string? Directory { get; private set; }

    [OneTimeSetUp]
    public void RedirectConfig()
    {
        _directory = System.IO.Directory.CreateTempSubdirectory("ge-avalonia-config-").FullName;
        Directory = _directory;
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _directory);
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        try
        {
            TestFiles.DeleteTree(_directory!);
        }
        catch (IOException)
        {
        }
    }
}
