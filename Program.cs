namespace PlexShowSubtitlesOnRewind
{
    class Program
    {
        // Replace with your Plex server details
        private const string PLEX_URL = "http://192.168.1.103:32400";
        private static string PLEX_APP_TOKEN;
        private static string PLEX_PERSONAL_TOKEN;

        static async Task Main(string[] args)
        {
            try
            {
                LoadTokens();

                Console.WriteLine($"Connecting to Plex server at {PLEX_URL}");
                PlexServer plexServer = new PlexServer(PLEX_URL, PLEX_APP_TOKEN);

                // Setup periodic session refresh
                CancellationTokenSource tokenSource = new CancellationTokenSource();
                Task refreshTask = Task.Run(async () =>
                {
                    while (!tokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            Console.WriteLine("Loading active sessions...");
                            await SessionManager.LoadActiveSessionsAsync(plexServer);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error refreshing sessions: {ex.Message}");
                        }
                        
                        // Wait 30 seconds before refreshing again
                        await Task.Delay(TimeSpan.FromSeconds(30), tokenSource.Token);
                    }
                }, tokenSource.Token);

                // Keep program running
                Console.WriteLine("Monitoring active Plex sessions for rewinding. Press any key to exit...");
                Console.ReadKey();

                // Clean up when exiting
                Console.WriteLine("Shutting down...");
                tokenSource.Cancel();
                MonitorManager.StopAllMonitors();

                try
                {
                    await refreshTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation token is triggered
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        static void LoadTokens()
        {
            string tokenFilePath = "tokens.config";

            if (!File.Exists(tokenFilePath))
            {
                // Create tokens file if it doesn't exist
                File.WriteAllText(tokenFilePath, "AppToken=whatever_your_app_token_is\nPersonalToken=whatever_your_personal_token_is");
                Console.WriteLine("Please edit the tokens.config file with your Plex app and/or personal tokens.");
                Console.WriteLine("Exiting...");
                Environment.Exit(0);
            }
            else
            {
                // Read tokens from file
                string[] lines = File.ReadAllLines(tokenFilePath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("AppToken="))
                        PLEX_APP_TOKEN = line.Substring("AppToken=".Length);
                    else if (line.StartsWith("PersonalToken="))
                        PLEX_PERSONAL_TOKEN = line.Substring("PersonalToken=".Length);
                }
            }
        }


    }  // ---------------- End class Program ----------------


} // --------------- End namespace PlexShowSubtitlesOnRewind ---------------