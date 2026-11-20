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

        // The tests find the Diff tab's files in its flat list.
        DiffListLayout.UseFile(_file + ".diff-layout.txt");
        DiffListLayout.Grouping = DiffGrouping.None;
    }

    [OneTimeTearDown]
    public void Restore()
    {
        ViewOptionsStore.Current = _previous;
        File.Delete(_file);
        File.Delete(_file + ".diff-layout.txt");
    }
}
