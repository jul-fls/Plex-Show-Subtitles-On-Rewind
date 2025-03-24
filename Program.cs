namespace PlexShowSubtitlesOnRewind
{
    static class Program
    {
        private static string PLEX_APP_TOKEN = "";
        private static string PLEX_APP_IDENTIFIER = "";
        public static Settings config = new();

        public static bool debugMode = false;

        static async Task Main(string[] args)
        {
            #if DEBUG
                debugMode = true;
            #endif

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
                PlexServer plexServer = new PlexServer(config.ServerURL, PLEX_APP_TOKEN);

                // Test connection to Plex server by connecting to the base api endpoint
                if (!await plexServer.TestConnectionAsync())
                {
                    Console.WriteLine("\nFailed to connect to Plex server. Exiting...");
                    return;
                }

                // Load active sessions and start monitoring
                try
                {
                    Console.WriteLine("Loading active sessions...");
                    List<ActiveSession> activeSessionList = await SessionManager.ClearAndLoadActiveSessionsAsync(plexServer);

                    if (debugMode)
                        SessionManager.PrintSubtitles();

                    MonitorManager.CreateAllMonitoringAllSessions(activeSessionList, printDebugAll:debugMode);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error refreshing sessions: {ex.Message}");
                }

                // Keep program running
                Console.WriteLine("Monitoring active Plex sessions for rewinding. Press any key to exit...");
                Console.ReadKey();

                // Clean up when exiting
                Console.WriteLine("Shutting down...");

                MonitorManager.StopAllMonitors();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        

    }  // ---------------- End class Program ----------------

    

} // --------------- End namespace PlexShowSubtitlesOnRewind ---------------
