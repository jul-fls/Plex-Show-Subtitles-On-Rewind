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

                Console.WriteLine($"Attempting to connect to Plex server at {config.ServerURL}...");
                PlexServer.SetupPlexServer(config.ServerURL, PLEX_APP_TOKEN, PLEX_APP_IDENTIFIER);

                // --- Initiate Connection and Monitoring ---
                // Start the connection/monitoring process. StartServerConnectionTestLoop
                // will handle the initial test and enter the retry loop if necessary.
                // We don't need to await it here or check its return value for program flow.
                // Run it in the background (fire-and-forget style for the main thread)
                // or await if you want Main to wait until the *first* connection attempt
                // (including potential retries) is resolved before proceeding.
                // Let's await it to ensure setup is attempted before waiting for exit.
                try
                {
                    await PlexServer.StartServerConnectionTestLoop();
                    // If it returns false (failed initial connection after retries),
                    // the ServerConnectionTestLoop within StartServerConnectionTestLoop
                    // should have already logged the failure. The app will continue waiting.
                    // If it returns true, monitoring was started successfully.
                }
                catch (Exception ex)
                {
                    // Catch errors during the initial startup attempt
                    WriteErrorSuper($"Fatal error during initial connection/monitoring setup: {ex.Message}\n");
                    Console.WriteLine(ex.StackTrace);
                    // Decide if the app should exit here or still proceed to wait state
                    // For robustness, let's allow it to proceed to wait state, maybe the listener can recover later.
                    WriteWarning("Proceeding to wait state despite initial setup error.");
                }


                // --- Wait for Shutdown Signal ---
                // Set up Ctrl+C handler to gracefully shut down
                Console.CancelKeyPress += (sender, eventArgs) => {
                    Console.WriteLine("Ctrl+C detected. Initiating shutdown...");
                    eventArgs.Cancel = true; // Prevent immediate process termination
                    _exitEvent.Set(); // Signal the main thread to exit
                };

                Console.WriteLine("Application running. Press Ctrl+C to exit.");
                _exitEvent.WaitOne(); // Wait here until Ctrl+C is pressed or exit is signaled otherwise

                // --- Application Shutdown ---
                WriteWarning("Shutdown signal received.");

            }
            catch (Exception ex) // Catch errors during token loading, settings, etc.
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
                WriteWarning("Performing final cleanup...");
                PlexServer.StopServerConnectionTestLoop(); // Ensure any active test loop is stopped
                MonitorManager.StopAllMonitoring(); // Stop monitors and listener (safe to call even if not started)
                Console.WriteLine("Application exited.");
                _exitEvent.Dispose(); // Dispose the event handle
            }
        }

    }  // ---------------- End class Program ----------------

} // --------------- End namespace PlexShowSubtitlesOnRewind ---------------
