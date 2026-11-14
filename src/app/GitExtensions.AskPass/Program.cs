using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;

namespace GitExtensions.AskPass;

/// <summary>
///  Protocol (UTF-8 lines): request "TOKEN\nBASE64(prompt)\n"; response "OK BASE64(reply)\n", "CANCEL\n" or "DENIED\n".
///  Exit code 0 prints the reply on stdout; 1 means the user cancelled or the request was refused.
/// </summary>
internal static class Program
{
    private const string EndpointVariable = "GE_ASKPASS_ENDPOINT";
    private const string TokenVariable = "GE_ASKPASS_TOKEN";

    private static int Main(string[] args)
    {
        string? endpoint = Environment.GetEnvironmentVariable(EndpointVariable);
        string? token = Environment.GetEnvironmentVariable(TokenVariable);
        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("Git Extensions askpass: not started by Git Extensions.");
            return 2;
        }

        string prompt = args.Length > 0 ? args[0] : string.Empty;
        try
        {
            using Stream stream = Connect(endpoint);
            byte[] request = Encoding.UTF8.GetBytes(token + "\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(prompt)) + "\n");
            stream.Write(request);
            stream.Flush();

            string? response = ReadLine(stream);
            if (response is not null && response.StartsWith("OK ", StringComparison.Ordinal))
            {
                byte[] reply = Convert.FromBase64String(response[3..]);
                using Stream stdout = Console.OpenStandardOutput();
                stdout.Write(reply);
                stdout.Write("\n"u8);
                stdout.Flush();
                Array.Clear(reply);
                return 0;
            }

            return 1;
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException or FormatException or TimeoutException)
        {
            Console.Error.WriteLine("Git Extensions askpass: " + ex.Message);
            return 1;
        }
    }

    private static Stream Connect(string endpoint)
    {
        if (OperatingSystem.IsWindows())
        {
            NamedPipeClientStream pipe = new(".", endpoint, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(10_000);
            return pipe;
        }

        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(endpoint));
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string? ReadLine(Stream stream)
    {
        List<byte> bytes = [];
        int value;
        while ((value = stream.ReadByte()) >= 0)
        {
            if (value == '\n')
            {
                break;
            }

            bytes.Add((byte)value);
        }

        return bytes.Count == 0 && value < 0 ? null : Encoding.UTF8.GetString([.. bytes]);
    }
}
