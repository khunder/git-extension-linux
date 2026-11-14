using Avalonia.Headless.NUnit;
using GitExtensions.Desktop.ViewModels;
using static GitExtensionsDesktopTests.ToolsTestSupport;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class ReflogTests
{
    [AvaloniaTest]
    public async Task Real_reflog_lists_commits_reset_and_checkout_newest_first()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        string c3 = repository.Commit("c.txt", "3", "Third");
        repository.RunGit("reset", "--hard", "HEAD~1");
        repository.RunGit("checkout", "--quiet", "--detach", "HEAD~1");
        Context context = CreateContext(repository);
        ReflogViewModel vm = new(context.Tools);

        await vm.LoadAsync();

        vm.Entries.Select(e => e.Selector).Should().StartWith("HEAD@{0}", "HEAD@{1}");
        vm.Entries[0].Message.Should().StartWith("checkout: moving from");
        vm.Entries[1].Message.Should().StartWith("reset: moving to");
        vm.Entries[2].Hash.Should().Be(c3);
        vm.Entries[2].Message.Should().Be("commit: Third");
        vm.Entries[2].Date.Should().NotBeNull();
        vm.Entries.Select(e => e.Message).Should().Contain("commit (initial): Add a.txt");
        vm.Refs.Should().Contain(["HEAD", "main"]);
        context.Errors.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Selecting_an_entry_loads_the_commit_details()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        repository.Commit("c.txt", "3", "Third");
        Context context = CreateContext(repository);
        ReflogViewModel vm = new(context.Tools);
        await vm.LoadAsync();

        vm.SelectedEntry = vm.Entries[0];
        await WaitAsync(() => vm.Details.HasCommit);

        vm.Details.Message.Should().Be("Third");
        vm.Details.Files.Select(f => f.Name).Should().Equal("c.txt");
    }

    [AvaloniaTest]
    public async Task Hard_reset_to_an_older_reflog_entry_recovers_lost_commits_after_confirmation()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        repository.Commit("c.txt", "3", "Third");
        string lost = repository.Commit("d.txt", "4", "Fourth");
        repository.RunGit("reset", "--hard", "HEAD~2");
        File.Exists(Path.Combine(repository.Path, "d.txt")).Should().BeFalse();
        Context context = CreateContext(repository);
        ReflogViewModel vm = new(context.Tools);
        await vm.LoadAsync();
        ReflogEntry target = vm.Entries.Single(e => e.Selector == "HEAD@{1}");
        target.Hash.Should().Be(lost);
        string before = repository.HeadId();

        // Without confirmation nothing happens, the prompt names the loss.
        (await vm.ResetAsync(target, CommitResetMode.Hard)).Should().BeFalse();
        repository.HeadId().Should().Be(before);
        vm.PromptKind.Should().Be(ReflogPromptKind.ConfirmHardReset);
        vm.PromptText.Should().Contain("HEAD@{1}").And.Contain("lost");

        await vm.ConfirmPromptCommand.ExecuteAsync(null);

        repository.HeadId().Should().Be(lost);
        File.Exists(Path.Combine(repository.Path, "d.txt")).Should().BeTrue();
        vm.PromptKind.Should().Be(ReflogPromptKind.None);
        context.Refreshes.Should().BeGreaterThan(0);
        vm.Entries[0].Message.Should().StartWith("reset: moving to");
    }

    [AvaloniaTest]
    public async Task Cancelling_the_hard_reset_prompt_keeps_everything()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        repository.Commit("c.txt", "3", "Third");
        Context context = CreateContext(repository);
        ReflogViewModel vm = new(context.Tools);
        await vm.LoadAsync();
        string before = repository.HeadId();

        vm.RequestHardResetCommand.Execute(vm.Entries[^1]);
        vm.IsPromptVisible.Should().BeTrue();
        vm.CancelPromptCommand.Execute(null);

        vm.IsPromptVisible.Should().BeFalse();
        repository.HeadId().Should().Be(before);
    }

    [AvaloniaTest]
    public async Task Soft_and_mixed_reset_keep_the_changes()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        string first = repository.HeadId();
        repository.Commit("c.txt", "3", "Third");
        Context context = CreateContext(repository);
        ReflogViewModel vm = new(context.Tools);
        await vm.LoadAsync();
        ReflogEntry older = vm.Entries.First(e => e.Hash == first);

        (await vm.ResetAsync(older, CommitResetMode.Soft)).Should().BeTrue();
        repository.HeadId().Should().Be(first);
        repository.GitOutput("status", "--porcelain").Should().Contain("A  c.txt");

        (await vm.ResetAsync(older, CommitResetMode.Mixed)).Should().BeTrue();
        repository.GitOutput("status", "--porcelain").Should().Contain("?? c.txt");
        File.Exists(Path.Combine(repository.Path, "c.txt")).Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Checkout_detaches_head_at_the_entry()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        string first = repository.HeadId();
        repository.Commit("c.txt", "3", "Third");
        Context context = CreateContext(repository);
        ReflogViewModel vm = new(context.Tools);
        await vm.LoadAsync();

        (await vm.CheckoutAsync(vm.Entries.First(e => e.Hash == first))).Should().BeTrue();

        repository.HeadId().Should().Be(first);
        repository.GitOutput("symbolic-ref", "--quiet", "HEAD").Should().BeEmpty("HEAD is detached");
        vm.Entries[0].Message.Should().StartWith("checkout: moving from main");
    }

    [AvaloniaTest]
    public async Task Create_branch_from_an_entry_uses_the_inline_prompt()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        string first = repository.HeadId();
        repository.Commit("c.txt", "3", "Third");
        Context context = CreateContext(repository);
        ReflogViewModel vm = new(context.Tools);
        await vm.LoadAsync();
        ReflogEntry entry = vm.Entries.First(e => e.Hash == first);

        vm.RequestCreateBranchCommand.Execute(entry);
        vm.NeedsInput.Should().BeTrue();
        vm.BranchName = "bad name";
        await vm.ConfirmPromptCommand.ExecuteAsync(null);
        context.Errors.Should().ContainSingle().Which.Should().Contain("not a valid branch name");
        vm.IsPromptVisible.Should().BeTrue("the prompt stays open for a correction");

        vm.BranchName = "rescue";
        await vm.ConfirmPromptCommand.ExecuteAsync(null);

        vm.IsPromptVisible.Should().BeFalse();
        repository.GitOutput("rev-parse", "rescue").Trim().Should().Be(first);
        vm.Refs.Should().Contain("rescue");
    }

    [AvaloniaTest]
    public async Task Branch_selector_shows_the_reflog_of_a_local_branch()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        repository.RunGit("branch", "topic");
        repository.RunGit("checkout", "--quiet", "topic");
        repository.Commit("t.txt", "t", "On topic");
        repository.RunGit("checkout", "--quiet", "main");
        Context context = CreateContext(repository);
        ReflogViewModel vm = new(context.Tools);
        await vm.LoadAsync();

        vm.SelectedRef = "topic";
        await WaitAsync(() => vm.Entries.Count > 0 && vm.Entries[0].Selector.StartsWith("topic@", StringComparison.Ordinal));

        vm.Entries.Select(e => e.Message).Should().Contain("commit: On topic");
        vm.Entries.Select(e => e.Message).Should().Contain(m => m.StartsWith("branch: Created from", StringComparison.Ordinal));
    }

    [AvaloniaTest]
    public async Task Copy_hash_uses_the_clipboard()
    {
        using TestRepository repository = new();
        Context context = CreateContext(repository);
        FakeClipboard clipboard = new();
        context.Tools.Clipboard = clipboard;
        ReflogViewModel vm = new(context.Tools);
        await vm.LoadAsync();

        await vm.CopyHashCommand.ExecuteAsync(vm.Entries[0]);

        clipboard.Text.Should().Be(vm.Entries[0].Hash);
    }

    internal static async Task WaitAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        condition().Should().BeTrue("the condition should become true in time");
    }

    private sealed class FakeClipboard : GitExtensions.Desktop.Services.IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }
}
