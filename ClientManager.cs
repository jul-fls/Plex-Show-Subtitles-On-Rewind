
namespace PlexShowSubtitlesOnRewind
{
    public static class ClientManager
    {
        private static readonly List<PlexClient> _clientList = [];
        private static readonly Lock _lockObject = new Lock();

        public static async Task<List<PlexClient>> LoadClientsAsync(PlexServer plexServer)
        {
            List<PlexClient> clientList = [];
            try
            {
                clientList = await plexServer.GetClientsAsync();

                lock (_lockObject)
                {
                    _clientList.Clear();
                    foreach (PlexClient client in clientList)
                    {
                        _clientList.Add(client);
                    }
                }

                Console.WriteLine($"Loaded {_clientList.Count} Plex clients");
                return _clientList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading clients: {ex.Message}");
                return clientList;
            }
        }

        public static List<PlexClient> Get()
        {
            lock (_lockObject)
            {
                return _clientList;
            }
        }

        //public static async Task EnableSubtitlesAsync(PlexClient client, int subtitleStreamID)
        //{
        //    await client.SetSubtitleStreamAsync(subtitleStreamID);
        //}

        //public static async Task DisableSubtitlesAsync(PlexClient client)
        //{
        //    await client.SetSubtitleStreamAsync(0);
        //}

        public static PlexClient? GetClient(object inputObj)
        {
            string machineID;

            if (inputObj is ActiveSession activeSession)
            {
                machineID = activeSession.MachineID;
            }
            else if (inputObj is PlexSession plexSession)
            {
                machineID = plexSession.Player.MachineIdentifier;
            }
            else
            {
                throw new Exception("Invalid input object type into ClientManager.GetClient().");
            }

            lock (_lockObject)
            {
                return _clientList.FirstOrDefault(client => client.MachineIdentifier == machineID);
            }
        }

        //public static async Task DisableSubtitlesBySessionAsync(object session)
        //{
        //    PlexClient? client = GetClient(session);
        //    if (client != null)
        //    {
        //        await DisableSubtitlesAsync(client);
        //    }
        //    else
        //    {
        //        Console.WriteLine("No client found for this session");
        //    }
        //}

        // For simplicity, also providing non-async versions
        //public static void DisableSubtitlesBySession(object session)
        //{
        //    DisableSubtitlesBySessionAsync(session).Wait();
        //}

        //public static async Task EnableSubtitlesBySessionAsync(
        //    object session,
        //    int? subtitleStreamID = null,
        //    SubtitleStream? subtitleStream = null)
        //{
        //    PlexClient? client = GetClient(session);
        //    if (client == null)
        //    {
        //        Console.WriteLine("No client found for this session");
        //        return;
        //    }

        //    if (subtitleStreamID.HasValue)
        //    {
        //        await EnableSubtitlesAsync(client, subtitleStreamID.Value);
        //    }
        //    else if (subtitleStream != null)
        //    {
        //        await EnableSubtitlesAsync(client, subtitleStream.Id);
        //    }
        //    else if (session is ActiveSession activeSession && activeSession.AvailableSubtitles.Count > 0)
        //    {
        //        await EnableSubtitlesAsync(client, activeSession.AvailableSubtitles[0].Id); // Assume first available subtitle stream
        //    }
        //    else
        //    {
        //        Console.WriteLine("No subtitle stream provided.");
        //    }
        //}

        // For simplicity, also providing non-async versions
        //public static void EnableSubtitlesBySession(
        //    object session,
        //    int? subtitleStreamID = null,
        //    SubtitleStream? subtitleStream = null)
        //{
        //    EnableSubtitlesBySessionAsync(session, subtitleStreamID, subtitleStream).Wait();
        //}
    }
}