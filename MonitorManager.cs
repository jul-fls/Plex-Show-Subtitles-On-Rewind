#nullable enable

namespace PlexShowSubtitlesOnRewind
{
    public static class MonitorManager
    {
        // Shared values / constants
        public const int DefaultMaxRewindAmount = 60;
        public const int DefaultActiveFrequency = 1;
        public const int DefaultIdleFrequency = 5;
        public const int DefaultSmallestResolution = 5; // If using the viewOffset, it's usually 5 seconds but apparently can be as high as 10s
        public const int AccurateTimelineResolution = 1; // Assume this resolution if have the accurate timeline data


        private static readonly List<RewindMonitor> _allMonitors = [];
        private static int _globalActiveFrequencyMs = DefaultActiveFrequency;
        private static int _globalIdleFrequencyMs = DefaultIdleFrequency;
        private static bool _isRunning = false;
        private static bool _printDebugAll = false;
        private static MonitoringState _monitoringState = MonitoringState.Active;
        private static int idleGracePeriodCount = 0; // Used to allow a few further checks before switching to idle state

        // In MonitorManager or Program.cs initialization
        private static PlexNotificationListener? _plexListener;

        // Create the listener
        public static void CreatePlexListener(string plexUrl, string plexToken)
        {
            _plexListener = new PlexNotificationListener(plexUrl, plexToken, notificationFilters: "playing");
            // Subscribe to the specific 'playing' event
            _plexListener.PlayingNotificationReceived += PlexListener_PlayingNotificationReceived;
            // Start listening
            _plexListener.StartListening();
        }

        private static void PlexListener_PlayingNotificationReceived(object? sender, PlexEventArgs e)
        {
            if (e.ParsedData is PlaySessionStateNotification playState)
            {
                WriteColor(
                    message: $"[Notification] Playback Update: Client={playState.clientIdentifier}, Key={playState.key}, State={playState.state}, Offset={playState.viewOffset}ms",
                    foreground: ConsoleColor.Cyan
                    );

                // --- Your Logic Here ---
                // Use playState.state, playState.clientIdentifier, playState.sessionKey, playState.viewOffset etc.
                // to decide when to activate or deactivate your *active* polling/monitoring.

                // Example: Start active monitoring when state is 'playing'
                if (playState.state?.Equals("playing", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Potentially find or create the appropriate RewindMonitor
                    // based on playState.sessionKey or playState.clientIdentifier
                    // and switch it to an 'active' state if it's not already.
                    // You might need to adapt RewindMonitor to have such a state.
                    //Console.WriteLine($"Playback started for session {playState.sessionKey}. Consider activating detailed monitoring.");

                    // You could trigger your existing RewindMonitor's check logic here,
                    // perhaps more frequently now that you know something is actively playing.
                    // GetOrCreateRewindMonitor(playState.sessionKey).CheckPlaybackStatus(); // Example call
                }
                // Example: Stop active monitoring when state is 'stopped' or maybe 'paused'
                else if (playState.state?.Equals("stopped", StringComparison.OrdinalIgnoreCase) == true ||
                         playState.state?.Equals("paused", StringComparison.OrdinalIgnoreCase) == true) // Decide if pause should stop active polling
                {
                    // Switch the corresponding RewindMonitor back to an 'idle' state
                    // or stop its timer.
                    //Console.WriteLine($"Playback stopped/paused for session {playState.sessionKey}. Consider deactivating detailed monitoring.");
                    // StopRewindMonitor(playState.sessionKey); // Example call
                }
            }
            else
            {
                WriteError($"[Notification] Received 'playing' event but couldn't parse data: {e.RawData}");
            }
        }

        public static void CreateAllMonitoringAllSessions(
            List<ActiveSession> activeSessionList,
            int activeFrequency = DefaultActiveFrequency,
            int idleFrequency = DefaultIdleFrequency,
            int maxRewindAmount = DefaultMaxRewindAmount,
            bool printDebugAll = false,
            string? debugDeviceName = null
            )
        {
            _globalActiveFrequencyMs = activeFrequency * 1000; // Convert to milliseconds
            _globalIdleFrequencyMs = idleFrequency * 1000;     // Convert to milliseconds

            if (printDebugAll)
                _printDebugAll = true; // Set global debug flag so that future monitors can use it

            foreach (ActiveSession activeSession in activeSessionList)
            {
                // Enable/Disable debugging per session depending on variables. Either for all devices or just a specific one
                bool printDebug = printDebugAll || Utils.CompareStringsWithWildcards(debugDeviceName, activeSession.DeviceName);

                CreateMonitorForSession(
                    activeSession: activeSession,
                    activeFrequency: activeFrequency,
                    idleFrequency: idleFrequency,
                    maxRewindAmount: maxRewindAmount,
                    smallestResolution: activeSession.SmallestResolutionExpected,
                    printDebug: printDebug
                );
            }

            StartRefreshLoop();
        }

        public static void CreateMonitorForSession(
            ActiveSession activeSession,
            int activeFrequency = DefaultActiveFrequency,
            int idleFrequency = DefaultIdleFrequency,
            int maxRewindAmount = DefaultMaxRewindAmount,
            bool printDebug = false,
            int smallestResolution = DefaultSmallestResolution
            )
        {
            string PlaybackID = activeSession.Session.PlaybackID;

            if (_printDebugAll)
            {
                printDebug = true;
            }

            // Check if a monitor already exists for this session, if not create a new one
            if (_allMonitors.Any(m => m.PlaybackID == PlaybackID))
            {
                Console.WriteLine($"Monitor for session {PlaybackID} already exists. Not creating a new one.");
                return;
            }
            else
            {
                RewindMonitor monitor = new RewindMonitor(
                    activeSession, 
                    activeFrequency: activeFrequency, 
                    idleFrequency: idleFrequency, 
                    maxRewindAmount: maxRewindAmount, 
                    printDebug: printDebug,
                    smallestResolution: smallestResolution
                    );
                _allMonitors.Add(monitor);
                WriteWarning($"Added new session for {activeSession.DeviceName}");
            }
        }

        public static List<string> GetMonitoredSessions()
        {
            List<string> sessionIDs = [];
            foreach (RewindMonitor monitor in _allMonitors)
            {
                sessionIDs.Add(monitor.PlaybackID);
            }
            return sessionIDs;
        }

        public static void RemoveMonitorForSession(string sessionID)
        {
            RewindMonitor? monitor = _allMonitors.FirstOrDefault(m => m.PlaybackID == sessionID);
            if (monitor != null)
            {
                _allMonitors.Remove(monitor);
            }
            else
            {
                Console.WriteLine($"No monitor found for session {sessionID}. Nothing to remove.");
            }
        }

        // This contains the main loop that refreshes the monitors and checks their state
        // It also handles the transition between active and idle states
        private static void RefreshLoop()
        {
            while (_isRunning)
            {
                List<ActiveSession> updatedSessions = SessionHandler.RefreshExistingActiveSessionsAsync(currentState: _monitoringState).Result; // Using .Result to get the result of the async method

                MonitoringState previousState = _monitoringState; // Store previous idle status for comparison
                MonitoringState pendingNewState = RunMonitors_OneIteration(_allMonitors);

                // When switching from active to idle, we'll allow a few extra loops before actually switching to idle
                // Because when moving from one episode to the next, it might incorrectly detect that the session goes idle
                if (pendingNewState == MonitoringState.Idle && previousState == MonitoringState.Active)
                {
                    idleGracePeriodCount++;
                    if (idleGracePeriodCount > 5)
                    {
                        _monitoringState = MonitoringState.Idle;
                        idleGracePeriodCount = 0; // Reset the counter if we switch to idle
                    }
                }
                else
                {
                    // If staying idle, or switching from idle to active, we can just set the new state immediately
                    _monitoringState = pendingNewState;
                    idleGracePeriodCount = 0; // Reset the counter if we switch to active
                }

                // Notify if the monitoring state has actually changed
                if (_monitoringState != previousState && _monitoringState == MonitoringState.Idle)
                    Console.WriteLine("Switched to idle mode.");
                else if (_monitoringState != previousState && _monitoringState == MonitoringState.Active)
                    Console.WriteLine("Switched to active mode.");

                // Sleep for a while based on the current mode
                if (_monitoringState == MonitoringState.Active)
                    Thread.Sleep(_globalActiveFrequencyMs);
                else
                    Thread.Sleep(_globalIdleFrequencyMs);
            }
        }

        private static void StartRefreshLoop()
        {
            if (_isRunning)
            {
                Console.WriteLine("Refresh loop already running. Not starting a new one.");
                return;
            }
            else
            {
                _isRunning = true;
                RefreshLoop();
            }
        }

        // Will return false if no monitors are active
        private static MonitoringState RunMonitors_OneIteration(List<RewindMonitor> monitorsToRefresh)
        {
            bool anyMonitorsActive = false;
            foreach (RewindMonitor monitor in monitorsToRefresh)
            {
                if (monitor.IsMonitoring) // This gets checked inside the loop also but is here for clarity. Might remove later
                {
                    anyMonitorsActive = true;
                    monitor.MakeMonitoringPass();
                }
            }

            if (anyMonitorsActive == true)
                return MonitoringState.Active;
            else
                return MonitoringState.Idle;
        }

        public static void StopAllMonitors()
        {
            foreach (RewindMonitor monitor in _allMonitors)
            {
                monitor.StopMonitoring();
            }
        }
    }
}