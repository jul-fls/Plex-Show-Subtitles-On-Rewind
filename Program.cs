using System.Runtime.InteropServices;

namespace PlexShowSubtitlesOnRewind
{
    static class Program
    {
        // Replace with your Plex server details
        private const string PLEX_URL = "http://192.168.1.103:32400";
        private static string PLEX_APP_TOKEN = "";

        private static int _serverHeartbeatInterval = 30; // seconds

        static async Task Main(string[] args)
        {
            try
            {
                LoadTokens(); // If tokens not found, will create empty template file, display message, and exit
                //TODO: Add a flow to generate a token automatically and create the file

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

        static void LoadTokens()
        {
            string tokenFilePath = "tokens.config";

            if (!File.Exists(tokenFilePath))
            {
                // Create tokens file if it doesn't exist
                CreateTokenFile();
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
                    {
                        string rawToken = line.Substring("AppToken=".Length);
                        string? validatedToken = validateToken(rawToken);
                        if (validatedToken != null)
                        {
                            PLEX_APP_TOKEN = validatedToken;
                        }
                        else
                        {
                            Console.WriteLine("Exiting...");
                            Environment.Exit(0);
                        }
                    }
                }
            }

            // Local function to validate the token
            static string? validateToken(string token)
            {
                // Trim whitespace and check length
                token = token.Trim();

                if (string.IsNullOrWhiteSpace(token))
                {
                    Console.WriteLine("Auth token is empty or not found. Update tokens.config.");
                    return null;
                }
                else if (token == MyStrings.TokenPlaceholder)
                {
                    Console.WriteLine("Update tokens.config to use your actual auth token for your plex server.");
                    return null;
                }
                else
                {
                    return token;
                }
            }
        }

        static void CreateTokenFile(string token = MyStrings.TokenPlaceholder)
        {
            string tokenFilePath = "tokens.config";
            File.WriteAllText(tokenFilePath, $"AppToken={token}\n");
        }

    }  // ---------------- End class Program ----------------

    // Various Enums / Pseudo Enums
    public static class MyStrings
    {
        public const string AppName = "Plex-Show-Subtitles-On-Rewind";
        public const string TokenPlaceholder = "whatever_your_app_token_is";
    }

    } // --------------- End namespace PlexShowSubtitlesOnRewind ---------------