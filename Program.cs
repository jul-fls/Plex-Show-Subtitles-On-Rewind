using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

#nullable enable

namespace RewindSubtitleDisplayerForPlex
{
    static class Program
    {
        internal static string PLEX_APP_TOKEN = "";
        internal static string PLEX_APP_IDENTIFIER = "";
        public static Settings config = new();

        public static bool debugMode = false;
        public static bool verboseMode = false;
        public static bool KeepAlive { get; set; } = true; // Used to keep the program running until user decides to exit
        private static bool allowDuplicateInstance = true; // Used to allow duplicate instances of the program

        private static ConnectionWatchdog? _connectionWatchdog; // Instance of the watchdog

        // ManualResetEvent for Ctrl+C
        private static readonly ManualResetEvent _ctrlCExitEvent = new ManualResetEvent(false);
        // CancellationTokenSource for graceful shutdown propagation
        private static readonly CancellationTokenSource _appShutdownCts = new CancellationTokenSource();


        // ===========================================================================================

        static void Main(string[] args)
        {
            #if DEBUG
                debugMode = true;
            #endif

            // Enable verbose mode if debug mode is enabled
            if (debugMode == true)
            {
                verboseMode = true;
            }

            // ------------ Apply launch parameters ------------

            bool runBackgroundArg = LaunchArgs.Background.CheckIfMatchesInputArgs(args);

            if (LaunchArgs.Stop.CheckIfMatchesInputArgs(args))
            {
                InstanceCoordinator.SignalShutdown();
                // Clean up handles for this short-lived instance
                InstanceCoordinator.Cleanup();
                return; // Exit after signaling
            }

            // ------------ Initialize Coordination Handles ------------
            if (!InstanceCoordinator.InitializeHandles())
            {
                // Error already logged by InitializeHandles
                if (!runBackgroundArg) { Console.WriteLine("Press Enter to exit..."); Console.ReadKey(); }
                return; // Cannot proceed
            }

           
            OS_Handlers.HandleBackgroundArg(runBackgroundArg);

            if (LaunchArgs.Debug.CheckIfMatchesInputArgs(args))
                debugMode = true;

            if (LaunchArgs.TokenTemplate.CheckIfMatchesInputArgs(args))
            {
                AuthTokenHandler.CreateTemplateTokenFile(force:true);
                WriteGreen("\nToken template generated.");
            }

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
                    WriteYellow("Debug mode enabled.\n");
                Console.WriteLine(MyStrings.LaunchArgsInfo);
                WriteRed("\n" + MyStrings.RequirementEnableRemoteAccess + "\n");
                Console.WriteLine("------------------------------------------------------------------------\n");
            }

            config = SettingsHandler.LoadSettings(); // Assign loaded settings to the static config variable

            // ------------ NI Logic: Check for Duplicates ------------
            bool duplicateFound = InstanceCoordinator.CheckForDuplicateServersAsync(config.ServerURL, allowDuplicateInstance).Result;
            if (duplicateFound)
            {
                if (allowDuplicateInstance)
                {
                    LogWarning("Duplicate instance found but currently set to allow duplicates. Continuing...");
                }
                else
                {
                    // Error already logged by CheckForDuplicateServersAsync
                    LogError($"Exiting because another instance is already monitoring server: {config.ServerURL}");
                    if (!runBackgroundArg) { Console.WriteLine("\nPress Enter to exit..."); Console.ReadKey(); }
                    InstanceCoordinator.Cleanup(); // Cleanup handles
                    return; // Exit NI
                }
            }

            // ------------------ Start Main ------------------

            try
            {
                (string, string)? resultTuple = AuthTokenHandler.LoadTokens();
                if (resultTuple == null)
                {
                    Console.WriteLine("\nFailed to load tokens. Exiting.");
                    if (!runBackgroundArg) { Console.ReadLine(); }
                    return;
                }

                PLEX_APP_TOKEN = resultTuple.Value.Item1;
                PLEX_APP_IDENTIFIER = resultTuple.Value.Item2;

                Console.WriteLine($"Using Plex server at {config.ServerURL}");
                PlexServer.SetupPlexServer(config.ServerURL, PLEX_APP_TOKEN, PLEX_APP_IDENTIFIER);

                // --- Instantiate and Start Watchdog ---
                _connectionWatchdog = new ConnectionWatchdog(config.ServerURL, PLEX_APP_TOKEN, PLEX_APP_IDENTIFIER);

                // Subscribe MonitorManager to the watchdog's event
                _connectionWatchdog.PlayingNotificationReceived += MonitorManager.HandlePlayingNotificationReceived; // Static handler now


                // Set up Ctrl+C handler. This doesn't run now, it just gets registered.
                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    WriteYellow("\n***** Ctrl+C detected. Initiating shutdown... *****\n");
                    eventArgs.Cancel = true; // Prevent immediate process termination
                    _ctrlCExitEvent.Set(); // Signal the legacy Ctrl+C event
                    _appShutdownCts.Cancel(); // Signal cancellation token for newer async waits
                };


                // Start the watchdog - it will handle connection and listener internally
                _connectionWatchdog.Start();

                // --- Start EI Listener Task (This instance now acts as an EI too) ---
                InstanceCoordinator.StartExistingInstanceListener(config.ServerURL, _appShutdownCts.Token);

                var shutdownHandle = InstanceCoordinator.GetShutdownWaitHandle();
                WaitHandle[] waitHandles = [_ctrlCExitEvent, shutdownHandle, _appShutdownCts.Token.WaitHandle];

                int signaledHandleIndex = WaitHandle.WaitAny(waitHandles);

                // Determine reason for exit
                string exitReason = signaledHandleIndex switch
                {
                    0 => "Ctrl+C detected",
                    1 => "External shutdown signal received",
                    2 => "Application cancellation token triggered",
                    _ => "WaitAny returned an unexpected index"
                };
                LogInfo($"Exit signal received ({exitReason}). Shutting down (this might take several seconds)...", ConsoleColor.Yellow);

                // Ensure cancellation is signaled if not already
                if (!_appShutdownCts.IsCancellationRequested)
                {
                    _appShutdownCts.Cancel();
                }

                Console.WriteLine("Application running. Press Ctrl+C to exit.");

                // --- Wait for Exit Signal ---
                _ctrlCExitEvent.WaitOne(); // Block main thread until Ctrl+C or other exit signal

                LogInfo("Exit signal received. Shutting down (this might take several seconds)...", Yellow);

            }
            catch (Exception ex) // Catch errors during initial setup
            {
                WriteErrorSuper($"Fatal error during startup: {ex.Message}\n");
                Console.WriteLine(ex.StackTrace);
                if (!runBackgroundArg)
                {
                    Console.WriteLine("\nPress Enter to exit...");
                    Console.ReadKey();
                }
            }
            finally
            {
                // --- Cleanup ---
                LogDebug("Performing final cleanup...");
                _connectionWatchdog?.Stop(); // Stop the watchdog first
                _connectionWatchdog?.Dispose(); // Dispose the watchdog
                //MonitorManager.RemoveAllMonitors(); // Not needed if we're just exiting the app, plus should be already handled
                LogInfo("    Application exited.");
                _ctrlCExitEvent.Dispose();
            }
        }

    }  // ---------------- End class Program ----------------

} // --------------- End namespace PlexShowSubtitlesOnRewind ---------------
