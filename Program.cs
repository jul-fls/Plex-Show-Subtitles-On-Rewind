using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

#nullable enable

namespace PlexShowSubtitlesOnRewind
{
    static class Program
    {
        internal static string PLEX_APP_TOKEN = "";
        internal static string PLEX_APP_IDENTIFIER = "";
        public static Settings config = new();

        public static bool debugMode = false;
        public static bool KeepAlive { get; set; } = true; // Used to keep the program running until user decides to exit

        // ===========================================================================================

        static async Task Main(string[] args)
        {
            #if DEBUG
                debugMode = true;
            #endif

            // ------------ Apply launch parameters ------------
            if (!LaunchArgs.Background.CheckIfMatchesInputArgs(args))
            {
                OS_Handlers.InitializeConsole(args);
            }

            if (LaunchArgs.Debug.CheckIfMatchesInputArgs(args))
                debugMode = true;

            if (LaunchArgs.Help.CheckIfMatchesInputArgs(args) || LaunchArgs.HelpAlt.CheckIfMatchesInputArgs(args))
            {
                Console.WriteLine(MyStrings.LaunchArgsInfo + "\n\n");
                // Later might add more details here, but for now it just shows the same info about launch parameters as the regular launch message
                Console.ReadLine();
                return;
            }
            // The normal launch message
            else
            {
                WriteGreen(MyStrings.HeadingTitle);
                if (debugMode)
                    WriteWarning("Debug mode enabled.\n");
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

                Settings config = SettingsHandler.LoadSettings();

                Console.WriteLine($"Connecting to Plex server at {config.ServerURL}\n");
                PlexServer plexServer = new PlexServer(config.ServerURL, PLEX_APP_TOKEN, PLEX_APP_IDENTIFIER);

                while (KeepAlive)
                {
                    // Test connection to Plex server by connecting to the base api endpoint
                    bool connected = await plexServer.StartServerConnectionTestLoop();
                }

                //// Test connection to Plex server by connecting to the base api endpoint
                //bool connected = await plexServer.StartServerConnectionTestLoop();

                //// Clean up when exiting. At this point the main refresh loop would have stopped for whatever reason
                //WriteWarning("Shutting down...");

                //MonitorManager.StopAllMonitoring();

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

} // --------------- End namespace PlexShowSubtitlesOnRewind ---------------
