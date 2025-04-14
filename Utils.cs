using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace RewindSubtitleDisplayerForPlex;
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

    public static Dictionary<object, object> AppendDictionaryToAnother(Dictionary<object, object> dict, Dictionary<object, object> dictToAppend)
    {
        if (dictToAppend != null)
        {
            foreach (KeyValuePair<object, object> kvp in dictToAppend)
            {
                if (!dict.ContainsKey(kvp.Key))
                {
                    dict.Add(kvp.Key, kvp.Value);
                }
            }
        }
        return dict;
    }

    /// <summary>
    /// Waits for the user to press Enter or for a specified timeout.
    /// </summary>
    /// <param name="seconds">The maximum time to wait in seconds.</param>
    /// <returns>True if Enter was pressed within the time limit, false otherwise.</returns>
    public static bool TimedWaitForEnterKey(int seconds, string verb)
    {
        // If running in background mode, don't wait for user input.
        if (Program.isBackgroundMode)
        {
            return true;
        }

        WriteYellow($"\nPress Enter to {verb} now. Will {verb} automatically in {seconds} seconds...");

        bool enterWasPressed = false;

        using var cts = new CancellationTokenSource();
        // Automatically request cancellation after the timeout period.
        cts.CancelAfter(seconds * 1000);

        // Task to wait for Console.ReadLine()
        Task inputTask = Task.Run(() =>
        {
            ReadlineSafe(); // Blocks this background thread until Enter is pressed.
            enterWasPressed = true; // If ReadLine completes, set the flag to true.
        });

        try
        {
            // Wait for either the input task or the timeout.
            Task.WaitAny(inputTask, Task.Delay(Timeout.Infinite, cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Expected exception when the token is canceled.
            // No additional action needed here.
        }

        return enterWasPressed;
    }

    public static void SayPressEnterIfConsoleAttached()
    {
        // If running in background mode, don't wait for user input.
        if (OS_Handlers.isConsoleAttached)
        {
            Console.WriteLine("\nPress Enter to Exit...");
            //ReadlineSafe(); // This isn't actually needed for some reason
        }
    }

    public static string? ReadlineSafe()
    {
        // If running in background mode, don't wait for user input.
        if (!Program.isBackgroundMode)
        {
            string? message = Console.ReadLine();
            return message;
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// Whether to add the integer to the end of the file name stem or the end of the extension
    /// </summary>
    public enum FileNameIterationLocation
    {
        Stem,
        Extension
    }

    public static string GetAvailableFileName(string filePath, bool returnFullPath = true, FileNameIterationLocation mode = FileNameIterationLocation.Stem)
    {
        // Convert to absolute path if not already. Assumes the path is relative to the current working directory.
        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.GetFullPath(filePath);
        }

        string fileName = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);
        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        int counter = 1;

        while (File.Exists(filePath))
        {
            // If the mode is Stem, add the counter to the end of the file name stem (like file-1.txt, file-2.txt, etc)
            if (mode == FileNameIterationLocation.Stem)
                filePath = Path.Combine(directory, $"{fileName}-{counter}{extension}");
            // If the mode is Extension, add the counter to the end of the extension (like .bak1, .bak2, etc)
            else
                filePath = Path.Combine(directory, $"{fileName}{extension}{counter}");

            counter++;
        }

        // Return depending on parameter
        if (returnFullPath == true)
        {
            return Path.GetFullPath(filePath); // Should already be a full path but just in case
        }
        else
        {
            return Path.GetFileName(filePath);
        }
    }

} // ---------- End of Utils Class -----------
