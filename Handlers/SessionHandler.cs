
namespace RewindSubtitleDisplayerForPlex
{
    public static class SessionHandler
    {
        private readonly static List<ActiveSession> _activeSessionList = [];
        private static readonly Lock _lockObject = new Lock();

        private readonly static List<ActiveSession> _deadSessionList = [];
        private const int _deadSessionGracePeriod = 60; // Seconds

        private static List<PlexResource> _knownDeviceResources = []; // Resources are info about connected devices to the server / players

        // This not only fetches the sessions, but also gets both active and available subtitles
        public static async Task<List<ActiveSession>> ProcessActiveSessions(List<PlexSession> sessionsList)
        {
            List<ActiveSession> newActiveSessionList = [];

            foreach (PlexSession session in sessionsList)
            {
                // Get active subtitles directly from the session Media
                List<SubtitleStream> activeSubs = GetOnlyActiveSubtitlesForSession(session);

                // Get ALL available subtitles with a separate metadata call
                List<SubtitleStream> availableSubs = await FetchAllAvailableSubtitles_ViaServerQuery_Async(session);

                newActiveSessionList.Add(new ActiveSession(
                    session: session,
                    availableSubtitles: availableSubs,
                    activeSubtitles: activeSubs
                ));
            }

            return newActiveSessionList;
        }

        public static List<PlexSession> UpdatePlayerPorts(List<PlexSession> sessionList)
        {

            foreach (PlexSession session in sessionList)
            {
                // Check the resources if any 'connections' -> 'address' matches the IP ('Address' property of Player object) of the session, if so get the 'connections' -> 'port'
                foreach (PlexResource resource in _knownDeviceResources)
                {
                    if (resource.Connections != null && resource.Connections.Count > 0)
                    {
                        // In this case 'client identifier' in the response list is the machine id of each
                        if (resource.ClientIdentifier == session.Player.MachineIdentifier)
                        {
                            session.Player.Port = resource.Connections[0].Port.ToString(); // Assume the first connection is the one we want
                            //session.Player.Address = resource.Connections[0].Address; // This is the IP address of the player
                            break;
                        }
                    }
                }
            }

            return sessionList;
        }

        public static async Task<List<ActiveSession>> ClearAndLoadActiveSessionsAsync()
        {
            List<PlexResource> resources = await PlexServer.GetResources();
            _knownDeviceResources = resources;

            List<PlexSession>? sessionsList = await PlexServer.GetSessionsAsync(shortTimeout:false);

            // It will only return null for an error
            if (sessionsList == null)
            {
                LogError("Error Occurred. See above messages. Will use existing session list if any.");
                return _activeSessionList;
            }
            else
            {
                LogDebug($"Fetched {sessionsList.Count} initial active sessions from server.");
            }

            List <ActiveSession> activeSessions = await ProcessActiveSessions(sessionsList);
            lock (_lockObject)
            {
                _activeSessionList.Clear();
                _activeSessionList.AddRange(activeSessions);
            }

            return _activeSessionList;
        }

        // Returns a list of updated active sessions
        public static async Task<List<ActiveSession>> RefreshExistingActiveSessionsAsync(MonitoringState currentState)
        {
            bool useShortTimeout = (currentState == MonitoringState.Idle);
            List<PlexSession>? fetchedSessionsList = await PlexServer.GetSessionsAsync(shortTimeout: useShortTimeout);

            if (fetchedSessionsList == null)
            {
                LogWarning("Problem occurred when fetching sessions. fetchedSessionsList returned null. Using existing session list.");
                return _activeSessionList;
            }

            if (fetchedSessionsList.Count == 0 && Program.config.ConsoleLogLevel >= LogLevel.Debug)
            {
                if (_activeSessionList.Count > 0)
                    LogDebug($"Server API returned 0 active sessions. {_activeSessionList.Count} were previously tracked.");
            }

            // Find any sessions in active sessions list that are not in the fetched list, by session ID
            List<ActiveSession> newlyDeadSessions = _activeSessionList.Where(s => !fetchedSessionsList.Any(fs => fs.PlaybackID == s.SessionID)).ToList();

            // Remove the dead sessions from the active session list and add them to the dead session list so they're separated
            foreach (ActiveSession deadSession in newlyDeadSessions)
            {
                if (!_deadSessionList.Contains(deadSession))
                {
                    _deadSessionList.Add(deadSession);
                    _activeSessionList.Remove(deadSession);

                    // Get the monitor associated with the session and remove it
                    MonitorManager.MarkMonitorDead(deadSession.SessionID);
                }
            }

            // Temporary lists to aid in managing dead sessions
            List<ActiveSession> newActiveSessionsOnly = [];

            // Process the fetched sessions
            List<Task> tasks = [];
            foreach (PlexSession fetchedSession in fetchedSessionsList)
            {
                tasks.Add(Task.Run(async () =>
                {
                    // We'll need the active subs in any case. Active subtitles are available from data we'll get from the session data anyway
                    List<SubtitleStream> activeSubtitles = GetOnlyActiveSubtitlesForSession(fetchedSession);

                    // Check if the session already exists in the active session list, and update in place if so
                    ActiveSession? existingSession = _activeSessionList.FirstOrDefault(s => s.PlaybackID == fetchedSession.PlaybackID);
                    if (existingSession != null)
                    {
                        existingSession.ApplyUpdatedData(fetchedSession, activeSubtitles);
                    }
                    else
                    {
                        // If the session is not found in the existing list, add it as a new session
                        // First need to get available subs by specifically querying the server for data about the media,
                        //      otherwise the session data doesn't include all available subs
                        List<SubtitleStream> availableSubs = await FetchAllAvailableSubtitles_ViaServerQuery_Async(fetchedSession);

                        ActiveSession newSession = new ActiveSession(
                            session: fetchedSession,
                            availableSubtitles: availableSubs,
                            activeSubtitles: activeSubtitles
                        );
                        lock (_lockObject)
                        {
                            _activeSessionList.Add(newSession);
                            newActiveSessionsOnly.Add(newSession);
                        }

                        // Create a new monitor for the newly found session. The method will automatically check for duplicates
                        MonitorManager.CreateMonitorForSession(
                            activeSession: newSession,
                            maxRewindAmountSec: Program.config.MaxRewindSec,
                            activeFrequencySec: Program.config.ActiveMonitorFrequencySec,
                            idleFrequencySec: Program.config.IdleMonitorFrequency,
                            smallestResolutionSec: newSession.SmallestResolutionExpected);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Check for dead sessions and remove them
            int removedSessionsCount = 0;
            if (_activeSessionList.Count > 0)
            {
                foreach (ActiveSession deadSession in _deadSessionList)
                {
                    // Get current epoch time in seconds
                    long currentTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                    long? lastSeenTime = deadSession.LastSeenTimeEpoch;
                    long? lastSeenTimeDiff;

                    // If the last seen time is not available, or if it's too old (beyond the grace period), remove the session
                    if (lastSeenTime != null)
                    {
                        lastSeenTimeDiff = currentTime - lastSeenTime;

                        if (lastSeenTimeDiff > _deadSessionGracePeriod)
                        {
                            RemoveSession(deadSession);
                            _deadSessionList.Remove(deadSession);
                            removedSessionsCount++;
                        }
                        else 
                        {
                            //LogDebug($"Missing {deadSession.DeviceName} session (Playback ID: {deadSession.SessionID}) is still within grace period. (Last seen {lastSeenTimeDiff}s ago)");
                        }
                    }
                    // This is the first check it was no longer seen, so set the last seen time to now
                    else
                    {
                        deadSession.LastSeenTimeEpoch = currentTime;
                        LogDebug($"{deadSession.DeviceName} session (Playback ID: {deadSession.SessionID}) no longer found. Beginning grace period.");
                    }

                    // Check if the dead session's playing media matches the current media on a newly added sessoin
                    // If so, we can have the new session take over the old session's monitor
                    // Compare using MachineID and RatingKey (A unique identifier for the media item. There's also media ID but I'm not sure how unique it is, seen some things it might correspond just to title)
                    ActiveSession? matchingNonDeadSession = newActiveSessionsOnly.FirstOrDefault(s => s.MachineID == deadSession.MachineID && s.Session.RatingKey == deadSession.Session.RatingKey);

                    if (matchingNonDeadSession != null) //&& matchingNonDeadSession.ContainsInheritedMonitor == false) // I don't think this 2nd check is necessary
                    {
                        matchingNonDeadSession = MonitorManager.TransferMonitorInheritance(deadSession, ref matchingNonDeadSession);

                        // If the monitor settings transfer was successful, we can remove the dead session now
                        if (matchingNonDeadSession != null)
                        {
                            RemoveSession(deadSession);
                            _deadSessionList.Remove(deadSession);
                            removedSessionsCount++;
                        }
                    }
                }
            }

            if (_activeSessionList.Count == 0 && removedSessionsCount > 0)
            {
                LogDebug($"No active sessions found after removing {removedSessionsCount} leftover sessions.");
            }

            return _activeSessionList;
        }

        public static List<ActiveSession> GetSessionList()
        {
            lock (_lockObject)
            {
                return _activeSessionList;
            }
        }

        public static ActiveSession? GetSessionByMachineID(string machineID)
        {
            lock (_lockObject)
            {
                return _activeSessionList.FirstOrDefault(s => s.MachineID == machineID);
            }
        }

        public static void RemoveSession(ActiveSession sessionToRemove)
        {
            LogVerbose($"Removing leftover session from {sessionToRemove.DeviceName}. Playback ID: {sessionToRemove.SessionID}");
            _activeSessionList.Remove(sessionToRemove);
            MonitorManager.RemoveMonitorForSession(sessionToRemove.SessionID);
        }

        // This specifically queries the server for data about the media item, which includes non-active subtitle tracks, whereas the session data does not include that
        // So we usually only use this when initially loading sessions, since available subs don't change often
        private static async Task<List<SubtitleStream>> FetchAllAvailableSubtitles_ViaServerQuery_Async(PlexSession session)
        {
            List<SubtitleStream> subtitles = [];
            try
            {
                // Make a separate call to get the full media metadata including all subtitle tracks
                string mediaKey = session.Key; // Like '/library/metadata/20884'
                PlexMediaItem mediaItem = await PlexServer.FetchItemAsync(mediaKey);

                // Get all subtitle streams from the media item
                subtitles = mediaItem.GetSubtitleStreams();

                LogDebug($"Found {subtitles.Count} available subtitle tracks for {session.Title}");
                return subtitles;
            }
            catch (Exception ex)
            {
                LogError($"Error getting available subtitles: {ex.Message}");
                return subtitles;
            }
        }

        private static List<SubtitleStream> GetOnlyActiveSubtitlesForSession(PlexSession session)
        {
            List<SubtitleStream> result = [];

            if (session.Media != null && session.Media.Count > 0)
            {
                foreach (PlexMedia media in session.Media)
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

                WriteLineSafe("\n-------------------------------------");
                WriteLineSafe($"Active Subtitles for {mediaTitle} on {deviceName}:");
                if (activeSubtitles.Count == 0)
                {
                    WriteLineSafe("[None]");
                }
                else
                {
                    foreach (SubtitleStream subtitle in activeSubtitles)
                    {
                        WriteLineSafe(subtitle.ExtendedDisplayTitle);
                    }
                }

                WriteLineSafe($"\nAvailable Subtitles for {mediaTitle} on {deviceName}:");
                if (availableSubtitles.Count == 0)
                {
                    WriteLineSafe("[None]");
                }
                else
                {
                    foreach (SubtitleStream subtitle in availableSubtitles)
                    {
                        WriteLineSafe(subtitle.ExtendedDisplayTitle);
                    }
                }
            }
        }
    }
}