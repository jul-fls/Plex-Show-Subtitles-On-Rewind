using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RewindSubtitleDisplayerForPlex;

// Class to define the launch arguments for the application, and to provide help information about them
public static class LaunchArgs
{
    // ---------------- Argument Definitions ------------------------
    public static readonly Argument Background =      new("background",       "Windows Only: The program runs in the background without showing a console.");
    public static readonly Argument Stop =            new("stop",             "Stop all currently running instances of the app.");
    public static readonly Argument ConfigTemplate =  new("settings-template",  "Generate a default settings config file.");
    public static readonly Argument Help =            new("help",             "Display help message with info including launch parameters.");
    // --- Advanced ---
    public static readonly Argument ForceNoDebug =    new("no-force-debug",   "Force the program to not use debug mode even if launching from a debugger.", advanced:true);
    public static readonly Argument TokenTemplate =   new ("token-template",  "Generate an example token config file.", advanced:true);
    public static readonly Argument AllowDuplicateInstance = new("allow-duplicate-instance", "New app instance will not close if it detects another is already connected to the same server.", advanced:true);
    public static readonly Argument UpdateSettings=   new("update-settings-file", "Update your old settings file to include missing settings, if any. A backup will be created.", advanced:true);
    public static readonly Argument TestSettings =    new("test-settings",   "Load the settings file and show which values are valid, and which are not and therefore will use default values.", advanced:true);
    public static readonly Argument NoInstanceCheck = new("no-instance-check", "This instance will not respond to -stop or other checks by other instances. Mainly for use when containerized.", advanced:true);
    public static readonly Argument AuthDeviceName =  new("auth-device-name", "Specify a device name when authorizing in non-interactive mode. Mainly for use when containerized. Include the name after this argument.", advanced:true, hasValue:true);
    // ---------------------------------------------------------------
    public static List<Argument> GetAllArgs() {
        return [
            Background, Stop, ConfigTemplate, Help, // Standard
            ForceNoDebug, TokenTemplate, AllowDuplicateInstance, UpdateSettings, TestSettings, NoInstanceCheck, AuthDeviceName // Advanced
        ];
    }

    // ------------------ Argument Info Display Strings ------------------
    public static readonly string StandardLaunchArgsInfo = $"""
            Optional Launch Parameters:
                -{LaunchArgs.Background} {t}{LaunchArgs.Background.Description}
                -{LaunchArgs.Stop} {tt}{LaunchArgs.Stop.Description}
                -{LaunchArgs.ConfigTemplate} {t}{LaunchArgs.ConfigTemplate.Description}
                -{LaunchArgs.Help} {tt}{LaunchArgs.Help.Description}
            """;

    // Advanced launch args are only shown when using -help or -?. It appends to the standard args info string.
    public static readonly string AdvancedLaunchArgsInfo = $"""
            Advanced Optional Launch parameters:
                -{LaunchArgs.ForceNoDebug} {tt}{LaunchArgs.ForceNoDebug.Description}
                -{LaunchArgs.TokenTemplate} {tt}{LaunchArgs.TokenTemplate.Description}
                -{LaunchArgs.AllowDuplicateInstance} {t}{LaunchArgs.AllowDuplicateInstance.Description}
                -{LaunchArgs.UpdateSettings} {t}{LaunchArgs.UpdateSettings.Description}
                -{LaunchArgs.TestSettings} {tt}{LaunchArgs.TestSettings.Description}
                -{LaunchArgs.NoInstanceCheck} {tt}{LaunchArgs.NoInstanceCheck.Description}
                -{LaunchArgs.AuthDeviceName} {tt}{LaunchArgs.AuthDeviceName.Description}
            """;

    public static readonly string AllLaunchArgsInfo = StandardLaunchArgsInfo + "\n\n" + AdvancedLaunchArgsInfo;

    public static readonly string AdvancedHelpInfo =
        HeadingTitle + "\n"
        + StandardLaunchArgsInfo + "\n\n"
        + AdvancedLaunchArgsInfo;

    const string t = "\t";
    const string tt = "\t\t";
    const string ttt = "\t\t\t";

    // Static constructor to add more properties in a more convenient place
    static LaunchArgs()
    {
        Background.Alts = ["b"];
        Help.Alts = ["h", "?"];
        Stop.Alts = ["s"];
        UpdateSettings.Alts = ["u"];
        TestSettings.Alts = ["t"];
        ForceNoDebug.Alts = ["nd"];
        ConfigTemplate.Alts = ["ct", "st"];
    }

    // ------------------------- Methods ------------------------------

    // Validate the arguments passed to the program. Returns true if all arguments are valid, false if any are invalid.
    public static bool CheckForUnknownArgsAndValidate(string[] args)
    {
        List<string> unknownArgs = [];
        List<string> argsWithInvalidValues = [];

        List<Argument> allArgs = GetAllArgs();
        bool wasPreviousValidValueArg = false; // Flag to check if the previous argument was a valid value argument
        foreach (string inputArg in args)
        {
            int argIndex = Array.IndexOf(args, inputArg);

            bool isValid = false;
            // Check with the list of all arguments
            foreach (Argument arg in allArgs)
            {
                if (arg.Check(new string[] { inputArg }))
                {
                    isValid = true; // The argument is valid

                    if (arg.HasValue) {

                        // Check if the next argument is a value for this argument
                        if (argIndex + 1 < args.Length)
                        {
                            string value = args[argIndex + 1].Trim();
                            if (string.IsNullOrWhiteSpace(value) || LaunchArgs.CheckArgumentByString(value))
                            {
                                argsWithInvalidValues.Add(inputArg);
                            }
                            else
                            {
                                wasPreviousValidValueArg = true; // The previous argument was a valid value argument
                            }
                        }
                        else
                        {
                            argsWithInvalidValues.Add(inputArg); // Add the argument to unknown args
                        }
                    }
                    break; // Break the inner loop
                }
            }

            //if (wasPreviousValidValueArg == true && isValid) // Later handle this where no value is given

            // If possibly not valid, check if it's a value for an argument
            if (!isValid && wasPreviousValidValueArg)
            {
                // This is fine and we already validated it
            }
            else if (!isValid)
            {
               unknownArgs.Add(inputArg);
            }
        }
        // Remove duplicates
        unknownArgs = unknownArgs.Distinct().ToList();

        // If there are any unknown arguments, print them
        if (unknownArgs.Count > 0)
        {
            WriteRedSuper("\n ERROR - Unknown command-line argument(s): ", noNewline:true);
            WriteRed("  " + string.Join(", ", unknownArgs));
            return false;
        }
        else
        {
            return true; // All arguments are valid
        }
    }

    public static bool CheckArgumentByString(string inputArgName)
    {
        // Check if the argument is present in the input arguments
        foreach (Argument arg in GetAllArgs())
        {
            if (arg.Check(new string[] { inputArgName }))
            {
                return true; // The argument is valid
            }
        }
        return false; // The argument is not valid
    }

    // ========================= Argument Class Type =========================
    public class Argument(string arg, string description, bool advanced = false, bool hasValue = false)
    {
        public string Arg { get; } = arg;
        public string Description { get; } = description;
        public bool IsAdvanced { get; } = advanced; // Only show advanced arguments when specifically using -help or -? (helpAlt)
        public bool HasValue { get; } = hasValue; // Indicates if the argument has a value (e.g. -arg value)
        public List<string> Alts { get; set; } = []; // List of alternative names for the argument
        public List<string> Variations => GetVariations(this);

        // ------------------ Methods ------------------
        // Checks if the current argument matches any of the input arguments supplied in the parameter array
        public bool Check(string[] allInputArgs)
        {
            return allInputArgs.Any(a => this.Variations.Contains(a));
        }

        public int GetIndexInArgs(string[] allInputArgs)
        {
            // Check if the argument is present in the input arguments
            if (this.Check(allInputArgs))
            {
                foreach (string argVariation in this.Variations)
                {
                    // Get the index of the argument
                    int index = Array.IndexOf(allInputArgs, argVariation);

                    if (index == -1)
                        continue; // Argument not found, skip to the next variation
                    else
                        return index; // Return the index of the first found argument
                }
            }
            return -1; // Argument not found
        }

        // For arguments that are not simply switches, this method will return the value of the argument after it
        public string? GetArgValue(string[] allInputArgs)
        {
            if (!this.HasValue)
            {
                return null; // No value expected for this argument
            }

            // Check if the argument is present in the input arguments
            if (this.Check(allInputArgs))
            {
                // Get the index of the argument
                int index = this.GetIndexInArgs(allInputArgs);
                if (index == -1)
                {
                    Debug.WriteLine($"Couldn't get arg index despite finding arg valid.");
                    return null; // This shouldn't happen but just in case
                }

                // Check if there is a value after the argument
                if (index + 1 < allInputArgs.Length)
                {
                    string value = allInputArgs[index + 1].Trim();

                    if (string.IsNullOrWhiteSpace(value))
                        return null;
                    else
                        return value; // Return the value after the argument
                }
            }
            return null; // No value found
        }

        // Get version starting with either hyphen or forward slash
        private static List<string> GetVariations(Argument arg)
        {
            List <string> variations = [];
            // Alts
            foreach (string alt in arg.Alts)
            {
                variations.Add("-" + alt);
                variations.Add("/" + alt);
            }
            // The main argument
            variations.Add("-" + arg.Arg);
            variations.Add("/" + arg.Arg);

            return variations;
        }

        // ------------------ Implicit conversions ------------------
        public static implicit operator List<string>(Argument info) => info.Variations;
        public static implicit operator string(Argument info) => info.Arg;
        public override string ToString() => Arg; // Ensure argument string is returned properly when used in string interpolation
    }    
}
