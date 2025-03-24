namespace PlexShowSubtitlesOnRewind
{
    static class Program
    {
        // Replace with your Plex server details
        private const string PLEX_URL = "http://192.168.1.103:32400";
        private static string PLEX_APP_TOKEN = "";
        private static string PLEX_APP_IDENTIFIER = "";

        static async Task Main(string[] args)
        {
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

                Console.WriteLine($"Connecting to Plex server at {PLEX_URL}");
                PlexServer plexServer = new PlexServer(PLEX_URL, PLEX_APP_TOKEN);

                try
                {
                    Console.WriteLine("Loading active sessions...");
                    List<ActiveSession> activeSessionList = await SessionManager.ClearAndLoadActiveSessionsAsync(plexServer);
                    await ClientManager.LoadClientsAsync(plexServer);
                    MonitorManager.CreateAllMonitoringAllSessions(activeSessionList);
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
