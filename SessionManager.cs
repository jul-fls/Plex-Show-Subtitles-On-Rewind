
namespace PlexShowSubtitlesOnRewind
{
    public static class SessionManager
    {
        private readonly static List<ActiveSession> _activeSessionList = [];
        private static readonly Lock _lockObject = new Lock();
        private static PlexServer? _plexServer = null;

        // This not only fetches the sessions, but also gets both active and available subtitles
        public static async Task<List<ActiveSession>> ProcessActiveSessions(List<PlexSession> sessionsList, PlexServer plexServer)
        {
            List<ActiveSession> newActiveSessionList = [];

            foreach (PlexSession session in sessionsList)
            {
                // Get active subtitles directly from the session Media
                List<SubtitleStream> activeSubs = GetActiveSubtitlesForSession(session);

                // Get ALL available subtitles with a separate metadata call
                List<SubtitleStream> availableSubs = await GetAllAvailableSubtitlesAsync(session, plexServer);

                newActiveSessionList.Add(new ActiveSession(
                    session: session,
                    availableSubtitles: availableSubs,
                    activeSubtitles: activeSubs,
                    plexServer: plexServer
                ));
            }

            //DEBUG
            TimelineMediaContainer? test = newActiveSessionList[0].GetTimelineContainer();

            return newActiveSessionList;
        }

        public static async Task<List<ActiveSession>> ClearAndLoadActiveSessionsAsync(PlexServer plexServer)
        {
            _plexServer = plexServer;
            List<PlexSession> sessionsList = await _plexServer.GetSessionsAsync();
            List <ActiveSession> activeSessions = await ProcessActiveSessions(sessionsList, plexServer);

            lock (_lockObject)
            {
                _activeSessionList.Clear();
                _activeSessionList.AddRange(activeSessions);
            }

            return _activeSessionList;
        }

        public static async Task<List<ActiveSession>> RefreshExistingActiveSessionsAsync()
        {
            // Assume _plexServer is already set. Show error if not
            if (_plexServer is not PlexServer plexServer)
            {
                Console.WriteLine("Error: PlexServer instance is null. Cannot refresh sessions. Must load sessions first.");
                return _activeSessionList;
            }
            // -----------------------------------

            List<PlexSession> sessionsList = await plexServer.GetSessionsAsync();

            foreach (PlexSession fetchedSession in sessionsList)
            {
                // We'll need the active subs in any case
                List<SubtitleStream> activeSubtitles = GetActiveSubtitlesForSession(fetchedSession);

                // Check if the session already exists in the active session list, and update in place if so
                ActiveSession? existingSession = _activeSessionList.FirstOrDefault(s => s.SessionID == fetchedSession.SessionId);
                if (existingSession != null)
                {
                    existingSession.Refresh(fetchedSession, activeSubtitles);
                }
                else
                {
                    // If the session is not found in the existing list, add it as a new session
                    // First need to get available subs
                    List<SubtitleStream> availableSubs = await GetAllAvailableSubtitlesAsync(fetchedSession, plexServer);

                    ActiveSession newSession = new ActiveSession(
                        session: fetchedSession,
                        availableSubtitles: availableSubs,
                        activeSubtitles: activeSubtitles,
                        plexServer: plexServer
                    );
                    _activeSessionList.Add(newSession);

                    // Create a new monitor for the newly found session. The method will automatically check for duplicates
                    MonitorManager.CreateMonitorForSession(
                        activeSession: newSession,
                        activeFrequency: MonitorManager.DefaultActiveFrequency,
                        idleFrequency: MonitorManager.DefaultIdleFrequency,
                        maxRewindAmount: MonitorManager.DefaultMaxRewindAmount,
                        printDebug: false // Set to true if you want to debug this session
                    );
                }
            }

            return _activeSessionList;
        }

        public static List<ActiveSession> Get()
        {
            lock (_lockObject)
            {
                return _activeSessionList;
            }
        }

        private static async Task<List<SubtitleStream>> GetAllAvailableSubtitlesAsync(PlexSession session, PlexServer plexServer)
        {
            List<SubtitleStream> subtitles = [];
            try
            {
                // Make a separate call to get the full media metadata including all subtitle tracks
                string mediaKey = session.Key; // Like '/library/metadata/20884'
                PlexMediaItem mediaItem = await plexServer.FetchItemAsync(mediaKey);

                // Get all subtitle streams from the media item
                subtitles = mediaItem.GetSubtitleStreams();

                Console.WriteLine($"Found {subtitles.Count} available subtitle tracks for {session.Title}");
                return subtitles;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting available subtitles: {ex.Message}");
                return subtitles;
            }
        }

        private static List<SubtitleStream> GetActiveSubtitlesForSession(PlexSession session)
        {
            List<SubtitleStream> result = [];

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