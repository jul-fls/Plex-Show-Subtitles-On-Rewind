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

            // Event to signal application exit
            ManualResetEvent _exitEvent = new ManualResetEvent(false);

            // ------------ Apply launch parameters ------------
            bool runInBackground = LaunchArgs.Background.CheckIfMatchesInputArgs(args);
            if (!runInBackground)
            {
                OS_Handlers.InitializeConsole(args);
            }

            if (LaunchArgs.Debug.CheckIfMatchesInputArgs(args))
                debugMode = true;

            if (LaunchArgs.Help.CheckIfMatchesInputArgs(args) || LaunchArgs.HelpAlt.CheckIfMatchesInputArgs(args))
            {
                Console.WriteLine(MyStrings.LaunchArgsInfo + "\n\n");
                Console.WriteLine("Press Enter to exit.");
                Console.ReadLine();
                return;
            }
            // The normal launch message (only if not running background)
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
                (string, string)? resultTuple = AuthTokenHandler.LoadTokens();
                if (resultTuple == null)
                {
                    Console.WriteLine("Failed to load tokens. Exiting.");
                    if (!runInBackground) { Console.ReadLine(); }
                    return;
                }

                PLEX_APP_TOKEN = resultTuple.Value.Item1;
                PLEX_APP_IDENTIFIER = resultTuple.Value.Item2;

                config = SettingsHandler.LoadSettings(); // Assign loaded settings to the static config variable

                Console.WriteLine($"Connecting to Plex server at {config.ServerURL}\n");
                PlexServer.SetupPlexServer(config.ServerURL, PLEX_APP_TOKEN, PLEX_APP_IDENTIFIER);


                // --- Initial Connection Attempt ---
                bool initialConnectionSuccess = false;
                try
                {
                    // StartServerConnectionTestLoop will attempt connection and start monitoring if successful
                    initialConnectionSuccess = await PlexServer.StartServerConnectionTestLoop();
                }
                catch (Exception ex)
                {
                    WriteErrorSuper($"Fatal error during initial connection: {ex.Message}\n");
                    Console.WriteLine(ex.StackTrace);
                    if (!runInBackground) { Console.ReadLine(); }
                    return; // Exit on fatal initial error
                }

                //This below might not make sense to include because it only runs if the initial connection is successful and then fails a while later.It doesn't actually display right upon connection
                if (initialConnectionSuccess)
                {
                    Console.WriteLine("Initial connection successful. Monitoring started.");
                    // Now the program relies on the listener to detect disconnects and handle reconnections.

                    // Set up Ctrl+C handler to gracefully shut down
                    Console.CancelKeyPress += (sender, eventArgs) =>
                    {
                        Console.WriteLine("Ctrl+C detected. Shutting down...");
                        eventArgs.Cancel = true; // Prevent immediate process termination
                        _exitEvent.Set(); // Signal the main thread to exit
                    };

                    Console.WriteLine("Application running. Press Ctrl+C to exit.");
                    _exitEvent.WaitOne(); // Wait here until Ctrl+C is pressed or exit is signaled otherwise

                    // --- Application Shutdown ---
                    WriteWarning("Shutdown signal received.");
                }
                else
                {
                    WriteError("Initial connection failed. Please check settings and Plex server status. Application will exit.");
                    if (!runInBackground)
                    {
                        Console.WriteLine("Press Enter to exit.");
                        Console.ReadLine();
                    }
                }
            }
            catch (Exception ex)
            {
                WriteErrorSuper($"Fatal error in main execution: {ex.Message}\n\n");
                Console.WriteLine(ex.StackTrace);
                if (!runInBackground)
                {
                    Console.WriteLine("\n\nPress Enter to exit...");
                    Console.ReadKey();
                }
            }
            finally
            {
                // --- Ensure cleanup happens on exit ---
                PlexServer.StopServerConnectionTestLoop(); // Ensure any active test loop is stopped
                MonitorManager.StopAllMonitoring(); // Stop monitors and listener (safe to call even if not started)
                Console.WriteLine("Application exited.");
                _exitEvent.Dispose(); // Dispose the event handle
            }
        }

    }  // ---------------- End class Program ----------------

} // --------------- End namespace PlexShowSubtitlesOnRewind ---------------
