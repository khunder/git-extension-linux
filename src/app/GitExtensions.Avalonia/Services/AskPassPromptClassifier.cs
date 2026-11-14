using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.Services;

public enum AskPassPromptKind
{
    /// <summary>A user name (shown in clear text).</summary>
    Username,

    /// <summary>A password or PIN (masked).</summary>
    Password,

    /// <summary>An SSH key passphrase (masked).</summary>
    Passphrase,

    /// <summary>A yes/no question such as the SSH host key confirmation.</summary>
    Confirmation,

    /// <summary>Anything else; treated as secret so nothing sensitive is ever shown.</summary>
    Other,
}

/// <summary>
///  What git or ssh asked for, in a form the credential prompt can present.
/// </summary>
/// <param name="Kind">The classification.</param>
/// <param name="Title">Short card title.</param>
/// <param name="Text">The prompt as git/ssh wrote it, trimmed.</param>
/// <param name="Target">The quoted host, url or key path in the prompt, if any.</param>
public sealed record AskPassPrompt(AskPassPromptKind Kind, string Title, string Text, string? Target)
{
    /// <summary>Gets a value indicating whether the answer must be masked while typing.</summary>
    public bool IsSecret => Kind is AskPassPromptKind.Password or AskPassPromptKind.Passphrase or AskPassPromptKind.Other;
}

/// <summary>
///  Classifies the prompt text passed to GIT_ASKPASS / SSH_ASKPASS.
/// </summary>
public static partial class AskPassPromptClassifier
{
    public static AskPassPrompt Classify(string prompt)
    {
        string text = (prompt ?? string.Empty).Trim();
        string? target = QuotedTarget(text);

        // Order matters: "Enter passphrase" must win over a quoted key path that happens to contain "password".
        if (text.Contains("yes/no", StringComparison.OrdinalIgnoreCase) || text.Contains("continue connecting", StringComparison.OrdinalIgnoreCase))
        {
            return new AskPassPrompt(AskPassPromptKind.Confirmation, "Confirm SSH host", text, target);
        }

        if (text.Contains("passphrase", StringComparison.OrdinalIgnoreCase))
        {
            return new AskPassPrompt(AskPassPromptKind.Passphrase, "SSH key passphrase", text, target);
        }

        if (text.Contains("username", StringComparison.OrdinalIgnoreCase) || text.StartsWith("login", StringComparison.OrdinalIgnoreCase))
        {
            return new AskPassPrompt(AskPassPromptKind.Username, "Git credentials", text, target);
        }

        if (text.Contains("password", StringComparison.OrdinalIgnoreCase) || PinPattern().IsMatch(text))
        {
            return new AskPassPrompt(AskPassPromptKind.Password, "Git credentials", text, target);
        }

        return new AskPassPrompt(AskPassPromptKind.Other, "Authentication required", text, target);
    }

    private static string? QuotedTarget(string text)
    {
        Match match = QuotedPattern().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"\bPIN\b", RegexOptions.IgnoreCase)]
    private static partial Regex PinPattern();

    [GeneratedRegex(@"'([^']+)'")]
    private static partial Regex QuotedPattern();
}
