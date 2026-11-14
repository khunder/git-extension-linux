using Avalonia.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>Window-level places the registry commands can ask the view to focus or toggle.</summary>
public enum UiTarget
{
    PathBox,
    SidebarFilter,
    CommitMessage,
    ToggleSidebar,
    ShowRecent,
    ShowStash,
}

// The command registry, the command palette and the shortcuts help (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<AppCommand>? _commandList;
    private CommandPaletteViewModel? _palette;
    private ShortcutsHelpViewModel? _shortcutsHelp;

    /// <summary>Raised when a command needs the view (focus a box, toggle the sidebar, open a flyout).</summary>
    public event Action<UiTarget>? UiRequested;

    public CommandPaletteViewModel Palette => _palette ??= new CommandPaletteViewModel(() => Commands, Run);

    public ShortcutsHelpViewModel ShortcutsHelp => _shortcutsHelp ??= new ShortcutsHelpViewModel(() => Commands);

    /// <summary>The single list of commands behind the shortcuts, the palette and the help.</summary>
    public IReadOnlyList<AppCommand> Commands => _commandList ??= BuildCommands();

    /// <summary>True while a modal overlay (settings, file history, palette, help, credentials) has the window's input.</summary>
    public bool IsOverlayOpen => Settings.IsOpen || FileHistory.IsOpen || Palette.IsOpen || ShortcutsHelp.IsOpen || Credentials.IsOpen;

    /// <summary>Raised just before a registry command runs from a shortcut or the palette.</summary>
    public event Action<AppCommand>? CommandInvoked;

    /// <summary>Runs a registry command (the caller checked <see cref="AppCommand.CanExecute"/>).</summary>
    public void Run(AppCommand command)
    {
        CommandInvoked?.Invoke(command);
        command.Execute();
    }

    /// <summary>Selects the next (older) commit in the history list.</summary>
    public void SelectNextCommit() => StepCommit(1);

    /// <summary>Selects the previous (newer) commit in the history list.</summary>
    public void SelectPreviousCommit() => StepCommit(-1);

    private void StepCommit(int delta)
    {
        if (Commits.Count == 0)
        {
            return;
        }

        int index = SelectedCommit is null ? (delta > 0 ? -1 : Commits.Count) : Commits.IndexOf(SelectedCommit);
        SelectedCommit = Commits[Math.Clamp(index + delta, 0, Commits.Count - 1)];
    }

    // The palette and the help never show at the same time.
    private void TogglePalette()
    {
        ShortcutsHelp.Close();
        Palette.Toggle();
    }

    private void ToggleHelp()
    {
        Palette.Close();
        ShortcutsHelp.Toggle();
    }

    private static void CycleTheme()
    {
        ThemeChoice next = ThemeService.Current.Choice switch
        {
            ThemeChoice.System => ThemeChoice.Light,
            ThemeChoice.Light => ThemeChoice.Dark,
            _ => ThemeChoice.System,
        };
        ThemeService.Current.Set(next);
    }

    private IReadOnlyList<AppCommand> BuildCommands()
    {
        const KeyModifiers Ctrl = KeyModifiers.Control;
        const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;

        static IReadOnlyList<CommandGesture> Keys(params CommandGesture[] gestures) => gestures;

        bool HasRepo() => HasOpenedRepository;
        bool Idle() => HasOpenedRepository && !IsBusy;

        AppCommand Make(string id, string title, string group, Action execute, Func<bool>? can = null, IReadOnlyList<CommandGesture>? gestures = null, string? scope = null, bool palette = true)
            => new(id, title, group, execute, can ?? (() => true), gestures ?? [], scope, palette);

        AppCommand FromCommand(string id, string title, string group, Func<System.Windows.Input.ICommand?> command, Func<bool>? extra = null, IReadOnlyList<CommandGesture>? gestures = null, string? scope = null)
            => Make(
                id,
                title,
                group,
                () =>
                {
                    if (command() is { } c && c.CanExecute(null))
                    {
                        c.Execute(null);
                    }
                },
                () => (extra?.Invoke() ?? true) && command() is { } c && c.CanExecute(null),
                gestures,
                scope);

        return
        [
            FromCommand("repo.open", "Open repository…", "Repository", () => BrowseCommand, gestures: Keys(CommandGesture.Of(Key.O, Ctrl))),
            Make("repo.recent", "Show recent repositories", "Repository", () => UiRequested?.Invoke(UiTarget.ShowRecent), gestures: Keys(CommandGesture.Of(Key.R, Ctrl))),
            Make("repo.refresh", "Refresh", "Repository", () => _ = OpenAsync(RepositoryPath), Idle, Keys(CommandGesture.Of(Key.F5))),
            FromCommand("remote.fetch", "Fetch", "Repository", () => Remote.FetchCommand, HasRepo, Keys(CommandGesture.Of(Key.F, CtrlShift))),
            FromCommand("remote.pull", "Pull", "Repository", () => Remote.PullCommand, HasRepo, Keys(CommandGesture.Of(Key.L, CtrlShift))),
            FromCommand("remote.push", "Push", "Repository", () => Remote.PushCommand, HasRepo, Keys(CommandGesture.Of(Key.K, CtrlShift))),
            Make("stash.show", "Stash changes…", "Repository", () => UiRequested?.Invoke(UiTarget.ShowStash), HasRepo, Keys(CommandGesture.Of(Key.T, CtrlShift))),

            // Changes
            FromCommand("index.stageAll", "Stage all changes", "Changes", () => Diff?.StageAllCommand, HasRepo, Keys(CommandGesture.Of(Key.S, CtrlShift))),
            FromCommand("index.unstageAll", "Unstage all changes", "Changes", () => Diff?.UnstageAllCommand, HasRepo, Keys(CommandGesture.Of(Key.U, CtrlShift))),
            FromCommand("commit.commit", "Commit", "Changes", () => Commit?.CommitCommand, HasRepo, Keys(CommandGesture.Of(Key.Return, Ctrl)), scope: "CommitPanel"),
            Make("commit.focusMessage", "Focus commit message", "Changes", () => UiRequested?.Invoke(UiTarget.CommitMessage), HasRepo, Keys(CommandGesture.Of(Key.M, Ctrl))),
            FromCommand("file.history", "File history of the selected file", "Changes", () => ShowFileHistoryCommand.CanExecute(SelectedChangedFile) ? new BoundCommand(ShowFileHistoryCommand, SelectedChangedFile) : null, gestures: Keys(CommandGesture.Of(Key.H, Ctrl))),
            FromCommand("file.blame", "Blame the selected file", "Changes", () => ShowFileBlameCommand.CanExecute(SelectedChangedFile) ? new BoundCommand(ShowFileBlameCommand, SelectedChangedFile) : null, gestures: Keys(CommandGesture.Of(Key.B, CtrlShift))),

            // Operations in progress
            FromCommand("operation.continue", "Continue the merge or rebase", "Operations", () => Operation.IsActive ? Operation.ContinueCommand : null, gestures: Keys(CommandGesture.Of(Key.Return, CtrlShift))),
            FromCommand("operation.abort", "Abort the merge or rebase…", "Operations", () => Operation.IsActive ? Operation.RequestAbortCommand : null, gestures: Keys(CommandGesture.Of(Key.Back, CtrlShift))),

            // History
            Make("history.next", "Select next commit", "History", SelectNextCommit, () => Commits.Count > 0, Keys(CommandGesture.Of(Key.J))),
            Make("history.previous", "Select previous commit", "History", SelectPreviousCommit, () => Commits.Count > 0, Keys(CommandGesture.Of(Key.K))),

            // View
            Make("view.sidebar", "Toggle branches sidebar", "View", () => UiRequested?.Invoke(UiTarget.ToggleSidebar), HasRepo, Keys(CommandGesture.Of(Key.B, Ctrl))),
            FromCommand("view.output", "Toggle Output panel", "View", () => Remote.ToggleOutputCommand, HasRepo, Keys(CommandGesture.Of(Key.O, CtrlShift))),
            Make("view.theme", "Cycle theme (system, light, dark)", "View", CycleTheme, gestures: Keys(CommandGesture.Of(Key.D, CtrlShift))),
            Make("view.settings", "Settings", "View", () => _ = Settings.OpenAsync(), gestures: Keys(CommandGesture.Of(Key.OemComma, Ctrl))),

            // Navigation
            Make("focus.filter", "Filter branches and tags", "Navigation", () => UiRequested?.Invoke(UiTarget.SidebarFilter), HasRepo, Keys(CommandGesture.Of(Key.F, Ctrl))),
            Make("focus.path", "Focus the repository path", "Navigation", () => UiRequested?.Invoke(UiTarget.PathBox), gestures: Keys(CommandGesture.Of(Key.L, Ctrl))),

            // Help
            Make("palette.show", "Command palette", "Help", TogglePalette, gestures: Keys(CommandGesture.Of(Key.P, CtrlShift), CommandGesture.Of(Key.P, Ctrl)), palette: false),
            Make("help.shortcuts", "Keyboard shortcuts", "Help", ToggleHelp, gestures: Keys(CommandGesture.Of(Key.F1), CommandGesture.Of(Key.OemQuestion, KeyModifiers.Shift, "?"))),
        ];
    }

    // A command with a fixed parameter, adapted to ICommand for the registry.
    private sealed class BoundCommand(CommunityToolkit.Mvvm.Input.IRelayCommand inner, object? parameter) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? commandParameter) => inner.CanExecute(parameter);

        public void Execute(object? commandParameter) => inner.Execute(parameter);
    }
}
