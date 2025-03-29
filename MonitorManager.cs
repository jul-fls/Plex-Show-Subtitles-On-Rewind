#nullable enable

using System;

namespace PlexShowSubtitlesOnRewind
{
    public static class MonitorManager
    {
        // Shared values / constants
        public const int DefaultMaxRewindAmount = 60;
        public const int DefaultActiveFrequency = 1;
        public const int DefaultIdleFrequency = 30;
        public const int DefaultWaitOnEventIdleFrequency = 3600; // Used when preferring event-based polling when idle, so this will be very long
        public const int DefaultSmallestResolution = 5; // If using the viewOffset, it's usually 5 seconds but apparently can be as high as 10s
        public const int AccurateTimelineResolution = 1; // Assume this resolution if have the accurate timeline data

        private static readonly List<RewindMonitor> _allMonitors = [];
        private static int _globalActiveFrequencyMs = DefaultActiveFrequency;
        private static int _globalIdleFrequencyMs = DefaultIdleFrequency;
        private static bool _isRunning = false;
        private static bool _printDebugAll = false;
        private static MonitoringState _monitoringState = MonitoringState.Active;
        private static int _idleGracePeriodCount = 0; // Used to allow a few further checks before switching to idle state
        private static PollingMode _idlePollingMode = PollingMode.Timer; // Default to timer polling when idle

        private static ManualResetEvent _sleepResetEvent = new ManualResetEvent(false);
        private static bool _sleepCancellationRequested = false;

        // In MonitorManager or Program.cs initialization
        private static PlexNotificationListener? _plexListener;

        // Create the listener
        public static void CreatePlexListener(string plexUrl, string plexToken)
        {
            _plexListener = new PlexNotificationListener(plexUrl, plexToken, notificationFilters: "playing");
            // Subscribe to the specific 'playing' event
            _plexListener.PlayingNotificationReceived += PlexListener_PlayingEventReceived;
            // Start listening
            _plexListener.StartListening();
        }

        private static void PlexListener_PlayingEventReceived(object? sender, PlexEventInfo e)
        {
            if (e.EventObj is PlayingEvent playEvent && playEvent.playState is PlayState playState)
            {
                WriteColor(
                    message: $"[Notification] Playback Update: Client={playEvent.clientIdentifier}, Key={playEvent.key}, State={playEvent.state}, Offset={playEvent.viewOffset}ms",
                    foreground: ConsoleColor.Cyan
                    );

                if (playState == PlayState.Playing)
                {
                    if (_monitoringState == MonitoringState.Idle)
                    {
                        Console.WriteLine("Switching to active monitoring due to playback event.");
                    }                   
                }
                else if (playState == PlayState.Paused)
                {
                    Console.WriteLine("Paused.");
                }
                else if (playState == PlayState.Stopped)
                {
                    Console.WriteLine("Stopped.");
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
        private static void PollingRefreshLoop()
        {
            while (_isRunning)
            {
                // Reset the cancellation flag at the start of each loop
                _sleepCancellationRequested = false;
                _sleepResetEvent.Reset();

                List<ActiveSession> updatedSessions = SessionHandler.RefreshExistingActiveSessionsAsync(currentState: _monitoringState).Result;

                MonitoringState previousState = _monitoringState;
                MonitoringState pendingNewState = RunMonitors_OneIteration(_allMonitors);

                // When switching from active to idle, we'll allow a few extra loops before actually switching to idle
                if (pendingNewState == MonitoringState.Idle && previousState == MonitoringState.Active)
                {
                    _idleGracePeriodCount++;
                    if (_idleGracePeriodCount > 5)
                    {
                        _monitoringState = MonitoringState.Idle;
                        _idleGracePeriodCount = 0;
                    }
                }
                else
                {
                    _monitoringState = pendingNewState;
                    _idleGracePeriodCount = 0;
                }

                // Notify if the monitoring state has actually changed
                if (_monitoringState != previousState && _monitoringState == MonitoringState.Idle)
                    Console.WriteLine("Switched to idle mode.");
                else if (_monitoringState != previousState && _monitoringState == MonitoringState.Active)
                    Console.WriteLine("Switched to active mode.");

                // Sleep for a while based on the current mode, but use the cancellable sleep mechanism
                int sleepTime;
                if (_monitoringState == MonitoringState.Active)
                    sleepTime = _globalActiveFrequencyMs;
                else
                    sleepTime = _globalIdleFrequencyMs;

                // Wait on the event with a timeout, allowing for cancellation
                _sleepResetEvent.WaitOne(sleepTime);
            }
        }

        public static void BreakFromIdle()
        {
            _monitoringState = MonitoringState.Active;

            // Request cancellation of the current sleep operation
            _sleepCancellationRequested = true;

            // Signal the event to wake up any waiting thread
            _sleepResetEvent.Set();

            Console.WriteLine("Sleep canceled - switching to active monitoring immediately");
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
                PollingRefreshLoop();
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