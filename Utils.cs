using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace PlexShowSubtitlesOnRewind;
internal class Utils
{
    // Function that compares two strings using regex but allows user to use asterisks as wildcards
    public static bool CompareStringsWithWildcards(string stringToCheckWithWildcard, string stringToCheckAgainst)
    {
        // Replace asterisks with regex equivalent
        string pattern = "^" + Regex.Escape(stringToCheckWithWildcard).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(stringToCheckAgainst, pattern);
    }
}
