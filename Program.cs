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
        private static bool instancesAreSetup = false; // Used to check if the instances are set up correctly

        private static ConnectionWatchdog? _connectionWatchdog; // Instance of the watchdog

        // ManualResetEvent for Ctrl+C
        private static readonly ManualResetEvent _ctrlCExitEvent = new ManualResetEvent(false);
        // CancellationTokenSource for graceful shutdown propagation
        private static readonly CancellationTokenSource _appShutdownCts = new CancellationTokenSource();


        // ===========================================================================================

        static void Main(string[] args)
        {
            // Early processing of launch args for Debug Mode and verbose mode
            if (LaunchArgs.Debug.Check(args))
                debugMode = true;
            if (LaunchArgs.Verbose.Check(args))
                verboseMode = true;
            if (debugMode == true)
                verboseMode = true;

            // Load Settings from file early on
            config = SettingsHandler.LoadSettings(); // Load settings early on but after debug mode is set by launch args if necessary
            if (!debugMode) { debugMode = config.DebugMode; } // Set debug mode from settings, but only if if not already set, as to not override the command line arg
            
            // -------------------
            #if DEBUG
                debugMode = true;
            #endif
            // -------------------

            // =======================================================================
            // ============== STARTUP LOGIC & LAUNCH ARGUMENTS HANDLING ==============
            // =======================================================================

            // Background mode - Config or Command Line
            bool runBackgroundMode = LaunchArgs.Background.Check(args)
                || (config.BackgroundMode.Value && !LaunchArgs.Stop.Check(args)); // Ignore background mode config setting if -stop is used
            OS_Handlers.HandleBackgroundMode(runBackgroundMode); // OS specific handling for background mode

            // Allow duplicate instances (Those that are set to connect to the same exact server. Mostly for testing.)
            if (LaunchArgs.AllowDuplicateInstance.Check(args))
                config.AllowDuplicateInstance.Value = true;

            // Token Template Generation
            if (LaunchArgs.TokenTemplate.Check(args))
            {
                bool tokenTemplateResult = AuthTokenHandler.CreateTemplateTokenFile(force: true);
                if (tokenTemplateResult)
                    WriteGreen("Token template file generated successfully.");
                else
                    WriteRed("Failed to generate token template file.");

                Console.WriteLine("\nPress Enter to exit.");
                Console.ReadLine();
                return;
            }

            // Config Template Generation
            if (LaunchArgs.ConfigTemplate.Check(args))
            {
                SettingsHandler.GenerateTemplateSettingsFile();
                return;
            }

            // Update Settings File
            if (LaunchArgs.UpdateSettings.Check(args))
            {
                SettingsHandler.UpdateSettingsFile(config);
                Console.WriteLine("\nPress Enter to exit.");
                Console.ReadLine();
                return;
            }

            // ------------ Instance Coordination ------------
            instancesAreSetup = InstanceCoordinator.InitializeHandles(); // Setup event wait handles and listeners for instance coordination
            if (!instancesAreSetup)
                WriteErrorSuper("ERROR: Failed to initialize coordination handles. Certain functions like the -stop parameter will not work.");

            // Stop other instances if requested
            if (LaunchArgs.Stop.Check(args))
            {
                InstanceCoordinator.SignalShutdown();
                // Clean up handles for this short-lived instance
                InstanceCoordinator.Cleanup();
                return; // Exit this instance itself after signaling others to shutdown
            }
            // ---------- End Instance Coordination -----------

            // Display startup message or help message (right now they are basically the same)
            if (LaunchArgs.Help.Check(args))
            {
                Console.WriteLine(LaunchArgs.AdvancedHelpInfo + "\n\n");
                Console.WriteLine("Press Enter to exit.");
                Console.ReadLine();
                return;
            }
            else // The normal launch message (only if not running background)
            {
                WriteGreen(MyStrings.HeadingTitle);
                if (debugMode)
                    WriteYellow("Debug mode enabled.\n");
                else if (verboseMode)
                    WriteYellow("Verbose mode enabled.\n");

                Console.WriteLine(LaunchArgs.StandardLaunchArgsInfo);
                WriteRed("\n" + MyStrings.RequirementEnableRemoteAccess + "\n");
                Console.WriteLine("------------------------------------------------------------------------\n");
            }

            // ------------ New Instance Logic: Check for Duplicates ------------
            if (instancesAreSetup)
            {
                bool duplicateFound = InstanceCoordinator.CheckForDuplicateServersAsync(config.ServerURL, config.AllowDuplicateInstance).Result;
                if (duplicateFound)
                {
                    if (config.AllowDuplicateInstance == true)
                    {
                        LogWarning("Duplicate instance found but currently set to allow duplicates. Continuing...");
                    }
                    else
                    {
                        // Error already logged by CheckForDuplicateServersAsync
                        LogError($"Exiting because another instance is already monitoring server: {config.ServerURL}");
                        if (!runBackgroundMode) { Console.WriteLine("\nPress Enter to exit..."); Console.ReadKey(); }
                        InstanceCoordinator.Cleanup(); // Cleanup handles
                        return; // Exit New Instance
                    }
                }
            }

            // =====================================================================
            // ======================== Start Main Logic ===========================
            // =====================================================================

            try
            {
                if (config.SkipAuth.Value == false) {
                    (string, string)? resultTuple = AuthTokenHandler.LoadTokens();
                    if (resultTuple == null)
                    {
                        Console.WriteLine("\nFailed to load tokens. Exiting.");
                        if (!runBackgroundMode) { Console.ReadLine(); }
                        return;
                    }

                    PLEX_APP_TOKEN = resultTuple.Value.Item1;
                    PLEX_APP_IDENTIFIER = resultTuple.Value.Item2;
                }
                else
                {
                    LogWarning("Skipping authentication because of config file setting.");
                    PLEX_APP_TOKEN = "";
                    PLEX_APP_IDENTIFIER = "";
                }

                Console.WriteLine($"Using Plex server at {config.ServerURL}");
                PlexServer.SetupPlexServer(config.ServerURL, PLEX_APP_TOKEN, PLEX_APP_IDENTIFIER);

                // --- Instantiate and Start Watchdog ---
                _connectionWatchdog = new ConnectionWatchdog(config.ServerURL, PLEX_APP_TOKEN, PLEX_APP_IDENTIFIER);

                // Subscribe MonitorManager to the watchdog's event, which will alert us to playing notifications
                _connectionWatchdog.ForwardPlayingNotificationReceived += MonitorManager.HandlePlayingNotificationReceived;


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

                // --- Start Existing Instance Listener Task (This instance now acts as an Existing Instance too) ---
                WaitHandle[] waitHandles;
                if (instancesAreSetup)
                {
                    InstanceCoordinator.StartExistingInstanceListener(config.ServerURL, _appShutdownCts.Token);
                    WaitHandle shutdownHandle = InstanceCoordinator.GetShutdownWaitHandle();
                    waitHandles = [_ctrlCExitEvent, _appShutdownCts.Token.WaitHandle, shutdownHandle];
                }
                else
                {
                    waitHandles = [_ctrlCExitEvent, _appShutdownCts.Token.WaitHandle];
                }

                Console.WriteLine("Application running. Press Ctrl+C to exit.");
                int signaledHandleIndex = WaitHandle.WaitAny(waitHandles);

                // Determine reason for exit
                string exitReason = signaledHandleIndex switch
                {
                    0 => "Ctrl+C detected",
                    1 => "Application cancellation token triggered",
                    2 => "External shutdown signal received", // This is last because it might not always be set
                    _ => "WaitAny returned an unexpected index"
                };
                LogInfo($"Exit signal received ({exitReason}). Shutting down (this might take several seconds)...", ConsoleColor.Yellow);

                // Ensure cancellation is signaled if not already
                if (!_appShutdownCts.IsCancellationRequested)
                {
                    _appShutdownCts.Cancel();
                }

            }
            catch (Exception ex) // Catch errors during initial setup
            {
                WriteErrorSuper($"Fatal error during startup: {ex.Message}\n");
                Console.WriteLine(ex.StackTrace);
                if (!runBackgroundMode)
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
