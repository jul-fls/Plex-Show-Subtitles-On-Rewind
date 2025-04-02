using System.Reflection;
using System.Text.RegularExpressions;

namespace PlexShowSubtitlesOnRewind;
internal class Utils
{
    // Function that compares two strings using regex but allows user to use asterisks as wildcards
    public static bool CompareStringsWithWildcards(string? stringToCheckWithWildcard, string? stringToCheckAgainst)
    {
        if (stringToCheckWithWildcard == null || stringToCheckAgainst == null)
        {
            return false;
        }

        // Replace asterisks with regex equivalent
        string pattern = "^" + Regex.Escape(stringToCheckWithWildcard).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(stringToCheckAgainst, pattern);
    }

    /// <summary>
    /// Returns a query string (used for HTTP URLs) where only the value is URL encoded.
    /// Example return value: '?genre=action&type=1337'.
    /// </summary>
    /// <param name="args">Arguments to include in query string.</param>
    /// <returns>A query string with URL-encoded values.</returns>
    public static string JoinArgs(Dictionary<string, object> args)
    {
        if (args == null || args.Count == 0)
        {
            return string.Empty;
        }

        List<string> argList = [];
        foreach (string? key in args.Keys.OrderBy(k => k.ToLower()))
        {
            string value = args[key]?.ToString() ?? string.Empty;
            argList.Add($"{key}={System.Web.HttpUtility.UrlEncode(value)}");
        }

        return $"?{string.Join("&", argList)}";
    }

    public static class Version
    {
        public static string? GetInformationalVersion() => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        public static string? GetFileVersion() => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

        public static string GetVersion()
        {
            string? version = GetFileVersion();
            if (!string.IsNullOrEmpty(version))
            {
                // Get only the first 3 parts of the version number, unless the last part is not 0
                List<string> versionParts = version.Split('.').ToList();

                // If 4th part is 0, remove it
                if (versionParts.Count > 3 && versionParts[3] == "0")
                    versionParts = versionParts.GetRange(0, 3);

                // Reconstruct the version string from the parts
                version = string.Join(".", versionParts);

            }
            else
            {
                version = "Unknown";
            }
            return version;
        }

    }

    public static HttpClient AddHttpClientHeaders(HttpClient client, Dictionary<string, string> headers)
    {
        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                client.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        }
        return client;
    }

    public static HttpRequestMessage AddHttpRequestHeaders(HttpRequestMessage request, Dictionary<string, string> headers)
    {
        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }
        }
        return request;
    }

    public static void WriteError(string message)
    {
        WriteColor(message: message, foreground: ConsoleColor.Red);
    }

    public static void WriteErrorSuper(string message)
    {
        WriteColor(message: message, foreground: ConsoleColor.White, background: ConsoleColor.Red);
    }

    public static void WriteWarning(string message)
    {
        WriteColor(message: message, foreground: ConsoleColor.Yellow);
    }

    public static void WriteGreen(string message)
    {
        WriteColor(message: message, foreground: ConsoleColor.Green);
    }

    public static void WriteColor(string message, ConsoleColor foreground, ConsoleColor? background = null)
    {
        Console.ForegroundColor = foreground;
        if (background != null)
            Console.BackgroundColor = background.Value;

        Console.WriteLine(message);
        Console.ResetColor();
    }


} // ---------- End of Utils Class -----------
