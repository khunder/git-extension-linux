using GitExtensions.Desktop.Services;

namespace GitExtensionsDesktopTests;

/// <summary>Keeps the tests from reading or writing the user's real view options.</summary>
[SetUpFixture]
public class ViewOptionsIsolation
{
    private ViewOptionsStore _previous = null!;
    private string _file = null!;

    [OneTimeSetUp]
    public void Isolate()
    {
        _previous = ViewOptionsStore.Current;
        _file = Path.Combine(Path.GetTempPath(), "ge-avalonia-view-options-" + Guid.NewGuid().ToString("N") + ".json");
        ViewOptionsStore.Current = new ViewOptionsStore(_file);
    }

    [OneTimeTearDown]
    public void Restore()
    {
        ViewOptionsStore.Current = _previous;
        File.Delete(_file);
    }
}
