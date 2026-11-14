using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GitExtensions.Extensibility;

/// <summary>
///  Set of DEBUG-only helpers.
/// </summary>
public static class DebugHelpers
{
    [Conditional("DEBUG")]
    public static void Assert([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            Fail(message);
        }
    }

    [Conditional("DEBUG")]
    public static void Fail(string message)
    {
        if (Debugger.IsAttached || IsTestRunning)
        {
            Debug.Fail(message);
        }
        else
        {
            Debugger.Launch();

            if (!Debugger.IsAttached)
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    [Conditional("DEBUG")]
    public static void Trace(string message, [CallerMemberName] string caller = "")
    {
        // colon and noBreakSpace are used to detect such messages in order to show them in the Output History
        const char noBreakSpace = '\u00a0';
        Debug.WriteLine($"{caller}:{noBreakSpace}{message}");
    }

    [Conditional("DEBUG")]
    public static void TraceIf(bool condition, string message, [CallerMemberName] string caller = "")
    {
        if (condition)
        {
            Trace(message, caller);
        }
    }

    private static bool IsTestRunning { get; } = DetectTestHost();

    private static bool DetectTestHost()
    {
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) is "testhost" or "testhost.x86")
        {
            return true;
        }

        // A test host may run under the shared dotnet runtime ("dotnet testhost.dll") rather than a native apphost,
        // for example on arm64 CI runners and on non-Windows platforms.
        return Assembly.GetEntryAssembly()?.GetName().Name is "testhost" or "testhost.x86";
    }
}
