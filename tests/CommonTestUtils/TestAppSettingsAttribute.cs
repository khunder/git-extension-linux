using GitCommands;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace CommonTestUtils;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TestAppSettingsAttribute : Attribute, ITestAction
{
    private const string SerializerName = "GitExtensionsTestAssemblySerializer";

    // Named semaphores are only supported on Windows; elsewhere an exclusive lock file serializes test assemblies.
    private readonly Semaphore? _semaphore = OperatingSystem.IsWindows() ? new(initialCount: 1, maximumCount: 1, SerializerName) : null;
    private FileStream? _lockFile;

    public ActionTargets Targets => ActionTargets.Suite;

    public void BeforeTest(ITest test)
    {
        Acquire();

        // A test host may run under the shared dotnet runtime rather than a native testhost.exe apphost (e.g. on the
        // arm64 CI runner, which has no native testhost.exe). In that case Application.ExecutablePath — and therefore
        // AppSettings.GetGitExtensionsDirectory() — points at the dotnet install directory instead of the test output,
        // so app-relative content such as the Themes folder cannot be found and theme loading throws (and, worse, pops
        // a modal error dialog that hangs the headless run). Pin the path to the test's own directory so it resolves.
        AppSettings.GetTestAccessor().ApplicationExecutablePath = Path.Combine(AppContext.BaseDirectory, "GitExtensions.exe");

        File.Delete(AppSettings.SettingsContainer.SettingsCache.SettingsFilePath);
        AppSettings.SettingsContainer.SettingsCache.Load();

        AppSettings.CheckForUpdates = false;
        AppSettings.ShowAvailableDiffTools = false;

        // Create the settings file so that the SettingsCache does not think it should reload the file again and again
        AppSettings.SettingsContainer.SettingsCache.Save();
    }

    public void AfterTest(ITest test)
    {
        AppSettings.SettingsContainer.SettingsCache.Dispose();

        Release();
    }

    private void Acquire()
    {
        if (_semaphore is not null)
        {
            _semaphore.WaitOne();
            return;
        }

        string lockPath = Path.Combine(Path.GetTempPath(), SerializerName + ".lock");
        while (true)
        {
            try
            {
                _lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private void Release()
    {
        if (_semaphore is not null)
        {
            _semaphore.Release();
            return;
        }

        _lockFile?.Dispose();
        _lockFile = null;
    }
}
