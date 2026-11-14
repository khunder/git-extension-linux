using GitCommands;
using GitExtensions.Extensibility;

namespace GitCommandsTests;

[TestFixture]
public class ExecutableStandardErrorTests
{
    // Regression test: the Exited event can fire before the stderr pipe is drained, which used to make fast-failing
    // commands report an empty error text some of the time (seen on Linux).
    [Test]
    public void Execute_should_always_capture_the_error_text_of_a_fast_failing_command()
    {
        string directory = Directory.CreateTempSubdirectory("ge-stderr-").FullName;
        try
        {
            Executable git = new("git", directory);
            git.Execute("init --quiet", throwOnErrorExit: false).ExitedSuccessfully.Should().BeTrue();

            int missingErrors = 0;
            for (int i = 0; i < 150; i++)
            {
                ExecutionResult result = git.Execute("rev-parse --verify definitely-not-a-ref", throwOnErrorExit: false);

                result.ExitedSuccessfully.Should().BeFalse();
                if (string.IsNullOrWhiteSpace(result.StandardError))
                {
                    missingErrors++;
                }
            }

            missingErrors.Should().Be(0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
