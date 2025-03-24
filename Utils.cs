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


} // ---------- End of Utils Class -----------
