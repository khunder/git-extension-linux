using Avalonia.Input;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  One entry of the application's command registry: shown in the palette and the shortcuts help, and bound to gestures.
/// </summary>
/// <param name="Id">Stable, unique identifier (for example <c>remote.fetch</c>).</param>
/// <param name="Title">What the palette and the help show.</param>
/// <param name="Group">Help section.</param>
/// <param name="Execute">Runs the command (on the UI thread).</param>
/// <param name="CanExecute">Whether the command can run right now.</param>
/// <param name="Gestures">The key gestures, the first one is displayed.</param>
/// <param name="FocusScope">When set, the gesture only fires while the keyboard focus is inside the control with this name.</param>
/// <param name="ShowInPalette">False for the commands that only make sense as a shortcut.</param>
public sealed record AppCommand(
    string Id,
    string Title,
    string Group,
    Action Execute,
    Func<bool> CanExecute,
    IReadOnlyList<CommandGesture> Gestures,
    string? FocusScope = null,
    bool ShowInPalette = true)
{
    public bool IsEnabled => CanExecute();

    /// <summary>The display text of all gestures, for example "Ctrl+Shift+P, Ctrl+P".</summary>
    public string GestureText => string.Join(", ", Gestures.Select(gesture => gesture.Text));

    /// <summary>The first gesture, for compact display in the palette.</summary>
    public string PrimaryGestureText => Gestures.Count > 0 ? Gestures[0].Text : string.Empty;
}

/// <summary>
///  A key plus modifiers, with a display text. Plain keys (no Ctrl/Alt/Meta, such as J or ?) are not delivered while typing.
/// </summary>
public sealed record CommandGesture(Key Key, KeyModifiers Modifiers, string Text)
{
    /// <summary>
    ///  Whether the gesture must not fire while a text box has the focus: printable keys would type a character.
    /// </summary>
    public bool IsTypedCharacter
        => (Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0
            && (Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or Key.OemQuestion or Key.OemComma or Key.OemPeriod or Key.Space);

    public static CommandGesture Of(Key key, KeyModifiers modifiers = KeyModifiers.None, string? text = null)
        => new(key, modifiers, text ?? Format(key, modifiers));

    public bool Matches(KeyEventArgs e)
        => e.Key == Key && NormalizedModifiers(e.KeyModifiers) == Modifiers;

    private static KeyModifiers NormalizedModifiers(KeyModifiers modifiers)
        => modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta);

    private static string Format(Key key, KeyModifiers modifiers)
    {
        List<string> parts = [];
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(key switch
        {
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "?",
            Key.Return => "Enter",
            Key.Back => "Backspace",
            _ => key.ToString(),
        });
        return string.Join("+", parts);
    }
}
