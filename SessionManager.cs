namespace PlexShowSubtitlesOnRewind
{
    public static class SessionManager
    {
        private static List<ActiveSession> _activeSessionList = new List<ActiveSession>();
        private static readonly object _lockObject = new object();

        public static async Task<List<ActiveSession>> LoadActiveSessionsAsync(PlexServer plexServer)
        {
            List<PlexSession> sessionsList = await plexServer.GetSessionsAsync();
            List<ActiveSession> newActiveSessionList = new List<ActiveSession>();

            foreach (PlexSession session in sessionsList)
            {
                string deviceName = session.Player.Title;
                string machineID = session.Player.MachineIdentifier;

                // Get active subtitles directly from the session Media
                List<SubtitleStream> activeSubs = GetActiveSubtitlesFromMedia(session);

                // Get ALL available subtitles with a separate metadata call
                List<SubtitleStream> availableSubs = await GetAllAvailableSubtitlesAsync(session, plexServer);

                string mediaTitle = session.GrandparentTitle ?? session.Title;

                newActiveSessionList.Add(new ActiveSession(
                    session: session,
                    availableSubtitles: availableSubs,
                    activeSubtitles: activeSubs,
                    deviceName: deviceName,
                    machineID: machineID,
                    mediaTitle: mediaTitle
                ));
            }

            lock (_lockObject)
            {
                _activeSessionList.Clear();
                _activeSessionList.AddRange(newActiveSessionList);
            }

            await ClientManager.LoadClientsAsync(plexServer);
            MonitorManager.StartMonitoringAllSessions(_activeSessionList);

            Console.WriteLine($"Loaded {_activeSessionList.Count} active sessions");
            PrintSubtitles(); // Print initial subtitle status

            return _activeSessionList;
        }

        public static List<ActiveSession> Get()
        {
            lock (_lockObject)
            {
                return new List<ActiveSession>(_activeSessionList);
            }
        }

        private static async Task<List<SubtitleStream>> GetAllAvailableSubtitlesAsync(PlexSession session, PlexServer plexServer)
        {
            try
            {
                // Make a separate call to get the full media metadata including all subtitle tracks
                string mediaKey = session.Key; // Like '/library/metadata/20884'
                PlexMediaItem mediaItem = await plexServer.FetchItemAsync(mediaKey);

                // Get all subtitle streams from the media item
                List<SubtitleStream> subtitles = mediaItem.GetSubtitleStreams();

                Console.WriteLine($"Found {subtitles.Count} available subtitle tracks for {session.Title}");
                return subtitles;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting available subtitles: {ex.Message}");
                return new List<SubtitleStream>();
            }
        }

        public static async Task<List<SubtitleStream>> GetAvailableSubtitlesAsync(PlexSession session, PlexServer plexServer)
        {
            try
            {
                // First check if we already have this information in the session
                List<SubtitleStream> subsFromMedia = GetAvailableSubtitlesFromMedia(session);
                if (subsFromMedia.Count > 0)
                {
                    return subsFromMedia;
                }

                // Otherwise fetch all available subtitles from the server
                string mediaKey = session.Key; // Like '/library/metadata/20884'
                return await plexServer.GetAvailableSubtitlesForMediaAsync(mediaKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting available subtitles: {ex.Message}");
                return new List<SubtitleStream>();
            }
        }

        private static List<SubtitleStream> GetAvailableSubtitlesFromMedia(PlexSession session)
        {
            List<SubtitleStream> result = new List<SubtitleStream>();

            if (session.Media != null && session.Media.Count > 0)
            {
                foreach (Media media in session.Media)
                {
                    if (media.Parts != null && media.Parts.Count > 0)
                    {
                        foreach (MediaPart part in media.Parts)
                        {
                            result.AddRange(part.Subtitles);
                        }
                    }
                }
            }

            return result;
        }

        private static List<SubtitleStream> GetActiveSubtitlesFromMedia(PlexSession session)
        {
            List<SubtitleStream> result = new List<SubtitleStream>();

            if (session.Media != null && session.Media.Count > 0)
            {
                foreach (Media media in session.Media)
                {
                    if (media.Parts != null && media.Parts.Count > 0)
                    {
                        foreach (MediaPart part in media.Parts)
                        {
                            // Only add subtitles that are marked as selected
                            result.AddRange(part.Subtitles.Where(s => s.Selected));
                        }
                    }
                }
            }

            return result;
        }

        public static async Task<List<SubtitleStream>> GetActiveSubtitlesAsync(PlexSession session, PlexServer plexServer)
        {
            try
            {
                // First check if we already have this information in the session
                List<SubtitleStream> subsFromMedia = GetActiveSubtitlesFromMedia(session);
                if (subsFromMedia.Count > 0)
                {
                    return subsFromMedia;
                }

                // Otherwise fetch from the server
                if (session.Media != null && session.Media.Count > 0)
                {
                    Media media = session.Media[0];
                    if (media.Parts != null && media.Parts.Count > 0)
                    {
                        return media.Parts[0].Subtitles.Where(s => s.Selected).ToList();
                    }
                }

                return new List<SubtitleStream>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting active subtitles: {ex.Message}");
                return new List<SubtitleStream>();
            }
        }

        public static void PrintSubtitles()
        {
            foreach (ActiveSession activeSession in _activeSessionList)
            {
                List<SubtitleStream> activeSubtitles = activeSession.ActiveSubtitles;
                List<SubtitleStream> availableSubtitles = activeSession.AvailableSubtitles;
                string deviceName = activeSession.DeviceName;
                string mediaTitle = activeSession.MediaTitle;

                Console.WriteLine("\n-------------------------------------");
                Console.WriteLine($"Active Subtitles for {mediaTitle} on {deviceName}:");
                if (activeSubtitles.Count == 0)
                {
                    Console.WriteLine("[None]");
                }
                else
                {
                    foreach (SubtitleStream subtitle in activeSubtitles)
                    {
                        Console.WriteLine(subtitle.ExtendedDisplayTitle);
                    }
                }

                Console.WriteLine($"\nAvailable Subtitles for {mediaTitle} on {deviceName}:");
                if (availableSubtitles.Count == 0)
                {
                    Console.WriteLine("[None]");
                }
                else
                {
                    foreach (SubtitleStream subtitle in availableSubtitles)
                    {
                        Console.WriteLine(subtitle.ExtendedDisplayTitle);
                    }
                }
            }
        }
    }
}