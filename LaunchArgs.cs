using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RewindSubtitleDisplayerForPlex;

// Class to define the launch arguments for the application, and to provide help information about them
public static class LaunchArgs
{
    // ---------------- Argument Definitions ------------------------
    public static readonly Argument Background =      new("background",       "Windows Only: The program runs in the background without showing a console.");
    public static readonly Argument TokenTemplate =   new ("token-template",  "Generate an example token config file.", advanced:true);
    public static readonly Argument Debug =           new("debug",            "Enables debug mode to show the highest detail of logging output.");
    public static readonly Argument Verbose =         new("verbose",          "Enables verbose mode to show additional logging output.");
    public static readonly Argument Help =            new("help",             "Display help message with info including launch parameters.");
    public static readonly Argument HelpAlt =         new("?",                Help.Description);
    public static readonly Argument Stop =            new("stop",             "Stop all currently running instances of the app.");
    public static readonly Argument AllowDuplicateInstance = new("allow-duplicate-instance", "New app instance will not close if it detects another is already connected to the same server.", advanced:true);

    // ------------------ Argument Info Display Strings ------------------
    public static readonly string StandardLaunchArgsInfo = $"""
            Optional Launch Parameters:
                -{LaunchArgs.Background}: {t}{LaunchArgs.Background.Description}
                -{LaunchArgs.Stop}: {tt}{LaunchArgs.Stop.Description}
                -{LaunchArgs.Verbose}: {tt}{LaunchArgs.Verbose.Description}
                -{LaunchArgs.Debug}: {tt}{LaunchArgs.Debug.Description}
                -{LaunchArgs.Help} or -{LaunchArgs.HelpAlt}: {t}{LaunchArgs.Help.Description}
            """;

    // Advanced launch args are only shown when using -help or -?. It appends to the standard args info string.
    public static readonly string AdvancedHelpInfo =
        HeadingTitle + "\n" 
        + StandardLaunchArgsInfo + "\n\n"
        + $"""
            Advanced Optional Launch parameters:
                -{LaunchArgs.TokenTemplate}: {tt}{LaunchArgs.TokenTemplate.Description}
                -{LaunchArgs.AllowDuplicateInstance}: {t}{LaunchArgs.AllowDuplicateInstance.Description}
            """;

    const string t = "\t";
    const string tt = "\t\t";

    // ------------------------- Methods ------------------------------
    // Get version starting with either hyphen or forward slash
    private static List<string> GetVariations(string arg)
    {
        List <string> variations = [];
        variations.Add("-" + arg);
        variations.Add("/" + arg);

        return variations;
    }

    // ========================= Argument Class Type =========================
    public class Argument(string arg, string description, bool advanced = false)
    {
        public string Arg { get; } = arg;
        public string Description { get; } = description;
        public bool IsAdvanced { get; set; } = advanced; // Only show advanced arguments when specifically using -help or -? (helpAlt)
        public List<string> Variations { get; } = GetVariations(arg);

        // ------------------ Methods ------------------
        // Checks if the current argument matches any of the input arguments supplied in the parameter array
        public bool Check(string[] allInputArgs)
        {
            return allInputArgs.Any(a => this.Variations.Contains(a));
        }

        // ------------------ Implicit conversions ------------------
        public static implicit operator List<string>(Argument info) => info.Variations;
        public static implicit operator string(Argument info) => info.Arg;
        public override string ToString() => Arg; // Ensure argument string is returned properly when used in string interpolation
    }    
}
