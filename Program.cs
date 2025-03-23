namespace PlexShowSubtitlesOnRewind
{
    static class Program
    {
        // Replace with your Plex server details
        private const string PLEX_URL = "http://192.168.1.103:32400";
        private static string PLEX_APP_TOKEN = "";

        static async Task Main(string[] args)
        {
            try
            {
                string? tokenResult = AuthTokenHandler.LoadTokens(); // If tokens not found, will create empty template file, display message, and exit
                if (tokenResult != null)
                {
                    PLEX_APP_TOKEN = tokenResult;
                }
                else
                {
                    // Messages and errors are already displayed within the LoadTokens method
                    return;
                }

                //TODO: Add a flow to generate a token automatically and create the file

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
