using System.Runtime.InteropServices;
using static PlexShowSubtitlesOnRewind.Program;
using static PlexShowSubtitlesOnRewind.Program.LaunchArgs;

namespace PlexShowSubtitlesOnRewind
{
    static class Program
    {
        internal static string PLEX_APP_TOKEN = "";
        internal static string PLEX_APP_IDENTIFIER = "";
        public static Settings config = new();

        public static bool debugMode = false;

        // Import AllocConsole from Windows API to create a console window for debugging
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        public static class LaunchArgs
        {
            public class Argument(string arg, string description)
            {
                public string Arg { get; } = arg;
                public string Description { get; } = description;
                public List<string> Variations { get; } = GetVariations(arg);

                public static implicit operator List<string>(Argument info) => info.Variations;
                public static implicit operator string(Argument info) => info.Arg;
                public override string ToString() => Arg; // Ensure argument string is returned properly when used in string interpolation
            }

            private static readonly Argument _background = new("background", "The program runs in the background without showing a console.");
            private static readonly Argument _debug =      new("debug",         "Enables debug mode to show additional output.");
            private static readonly Argument _help =       new("help",          "Display help message with info including launch parameters.");
            private static readonly Argument _helpAlt =    new("?",             _help.Description);

            // -------------------------------------------------
            public static Argument Background => _background;
            public static Argument Debug => _debug;
            public static Argument Help => _help;
            public static Argument HelpAlt => _helpAlt;

            // --------------------------------------------------------
            // Get version starting with either hyphen or forward slash
            private static List<string> GetVariations(string arg)
            {
                List <string> variations = [];
                variations.Add("-" + arg);
                variations.Add("/" + arg);

                return variations;
            }
        }

        public static bool CheckArgMatch(string[] inputArgs, Argument argToCheck)
        {
            return inputArgs.Any(a => argToCheck.Variations.Contains(a));
        }

        // ===========================================================================================

        static async Task Main(string[] args)
        {
            #if DEBUG
                debugMode = true;
            #endif

            // ------------ Apply launch parameters ------------
            //if (!args.Any(arg => LaunchArgs.Background.Variations.Contains(arg))) // Unless background mode is specified, show console window
            if (!CheckArgMatch(args, LaunchArgs.Background))
                AllocConsole();

            //if (args.Any(arg => LaunchArgs.Debug.Variations.Contains(arg)))
            if (CheckArgMatch(args, LaunchArgs.Debug))
                debugMode = true;

            WriteGreen(MyStrings.HeadingTitle);
            if (debugMode)
                WriteWarning("Debug mode enabled.\n");

            //if (args.Any(arg => LaunchArgs.Help.Variations.Contains(arg) || LaunchArgs.HelpAlt.Variations.Contains(arg)))
            if (CheckArgMatch(args, LaunchArgs.Help) || CheckArgMatch(args, LaunchArgs.HelpAlt))
            {
                Console.WriteLine(MyStrings.LaunchArgsInfo + "\n\n");
                // Later might add more details here, but for now it just shows the same info about launch parameters as the regular launch message
                Console.ReadLine();
                return;
            }
            else
            {
                Console.WriteLine(MyStrings.LaunchArgsInfo);
                Console.WriteLine("------------------------------------------------------------------------\n");
            }

            // ------------------ Start Main ------------------

            try
            {
                (string, string)? resultTuple = AuthTokenHandler.LoadTokens(); // If tokens not found, will create empty template file, display message, and exit

                if (resultTuple != null)
                {
                    string authToken = resultTuple.Value.Item1;
                    string clientIdentifier = resultTuple.Value.Item2;

                    PLEX_APP_TOKEN = authToken;
                    PLEX_APP_IDENTIFIER = clientIdentifier;
                }
                else
                {
                    // Messages and errors are already displayed within the LoadTokens method
                    return;
                }

                if (args.Length > 0)
                {
                    //TODO maybe - Add command line arguments
                }

                Settings config = SettingsHandler.LoadSettings();

                Console.WriteLine($"Connecting to Plex server at {config.ServerURL}\n");
                PlexServer plexServer = new PlexServer(config.ServerURL, PLEX_APP_TOKEN, PLEX_APP_IDENTIFIER);

                // Test connection to Plex server by connecting to the base api endpoint
                if (!await plexServer.TestConnectionAsync())
                {
                    WriteError("\nFailed to connect to Plex server. Exiting...");
                    return;
                }

                // Load active sessions and start monitoring
                try
                {
                    if (debugMode)
                        Console.WriteLine("Loading active sessions...");

                    List<ActiveSession> activeSessionList = await SessionHandler.ClearAndLoadActiveSessionsAsync(plexServer);

                    if (debugMode)
                        SessionHandler.PrintSubtitles();

                    MonitorManager.CreatePlexListener(config.ServerURL, PLEX_APP_TOKEN);

                    Console.WriteLine($"Found {activeSessionList.Count} active session(s). Future sessions will be added. Beginning monitoring...\n");
                    MonitorManager.CreateAllMonitoringAllSessions(
                        activeSessionList,
                        activeFrequency: config.ActiveMonitorFrequency,
                        idleFrequency: config.IdleMonitorFrequency,
                        printDebugAll: debugMode);
                }
                catch (Exception ex)
                {
                    WriteError($"Error getting sessions: {ex.Message}");
                }

                // Clean up when exiting. At this point the main refresh loop would have stopped for whatever reason
                WriteWarning("Shutting down...");

                MonitorManager.StopAllMonitors();

            }
            catch (Exception ex)
            {
                WriteErrorSuper($"Fatal error: {ex.Message}\n\n");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine("\n\nPress any key to exit...");
                Console.ReadKey();
            }
        }

    }  // ---------------- End class Program ----------------

    public static class MyStrings
    {
        public const string AppNameDashed = "Show-Rewind-Subtitles-For-Plex";
        public const string AppName = "Show Rewind Subtitles For Plex";
        public static string LaunchArgsInfo = $"""
            Optional Launch parameters:
                -{LaunchArgs.Background}: {LaunchArgs.Background.Description}
                -{LaunchArgs.Debug}: {LaunchArgs.Debug.Description}
                -{LaunchArgs.Help} or -{LaunchArgs.HelpAlt}: {LaunchArgs.Help.Description}
            """;
        public static string HeadingTitle = $"\n----------- {AppName} -----------\n";
    }

} // --------------- End namespace PlexShowSubtitlesOnRewind ---------------
