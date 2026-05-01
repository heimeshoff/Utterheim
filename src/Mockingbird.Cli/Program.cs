// Mockingbird-Speak CLI — thin wrapper over POST /speak.
// Per ADR 0003: Claude hooks should be able to invoke
//   mockingbird-speak --voice alba "task done"
// without remembering curl flags.
using System.Net.Http.Json;
using System.Text.Json;

namespace Mockingbird.Cli;

internal static class Program
{
    private const string DefaultEndpoint = "http://127.0.0.1:7223";

    public static async Task<int> Main(string[] args)
    {
        var parsed = ParseArgs(args);
        if (parsed is null)
        {
            PrintUsage();
            return 1;
        }

        var endpoint = Environment.GetEnvironmentVariable("MOCKINGBIRD_ENDPOINT") ?? DefaultEndpoint;
        var url = $"{endpoint.TrimEnd('/')}/speak";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            var response = await http.PostAsJsonAsync(url, new
            {
                text = parsed.Value.Text,
                voice = parsed.Value.Voice,
            });

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                await Console.Error.WriteLineAsync($"mockingbird-speak: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                if (!string.IsNullOrWhiteSpace(body)) await Console.Error.WriteLineAsync(body);
                return 2;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("requestId", out var idElement))
            {
                Console.WriteLine(idElement.GetString());
            }
            return 0;
        }
        catch (HttpRequestException ex)
        {
            await Console.Error.WriteLineAsync($"mockingbird-speak: cannot reach {url} — is mockingbird running? ({ex.Message})");
            return 3;
        }
    }

    private static (string Text, string Voice)? ParseArgs(string[] args)
    {
        string? voice = null;
        string? text = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--voice":
                case "-v":
                    if (i + 1 >= args.Length) return null;
                    voice = args[++i];
                    break;
                case "--help":
                case "-h":
                case "/?":
                    return null;
                default:
                    // First non-flag positional argument is the text.
                    if (text is null) text = args[i];
                    else text = text + " " + args[i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(text)) return null;
        return (text!, voice ?? "test-voice");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: mockingbird-speak [--voice <id>] \"<text>\"");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --voice <id>   Voice id (default: test-voice)");
        Console.WriteLine("  --help         Show this message");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  MOCKINGBIRD_ENDPOINT  Override base URL (default: http://127.0.0.1:7223)");
    }
}
