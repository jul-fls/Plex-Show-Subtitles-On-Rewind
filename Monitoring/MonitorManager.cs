#nullable enable

using System;

namespace RewindSubtitleDisplayerForPlex
{
    public static class MonitorManager
    {
        // Shared values / constants
        public const int DefaultMaxRewindAmount = 60;
        public const int DefaultActiveFrequency = 1;
        public const int DefaultIdleFrequency = 30;
        public const int DefaultWaitOnEventIdleFrequency_seconds = 3600; // Used when preferring event-based polling when idle, so this will be very long
        public const int DefaultSmallestResolution = 5; // If using the viewOffset, it's usually 5 seconds but apparently can be as high as 10s
        public const int AccurateTimelineResolution = 1; // Assume this resolution if have the accurate timeline data

        private static readonly List<RewindMonitor> _allMonitors = [];
        private static int _globalActiveFrequencyMs = DefaultActiveFrequency;
        private static int _globalIdleFrequencyMs = DefaultIdleFrequency;
        private static bool _isRunning = false;
        private static bool _printDebugAll = false;
        private static MonitoringState _monitoringState = MonitoringState.Active;
        private static int _idleGracePeriodCount = 0; // Used to allow a few further checks before switching to idle state
        private static PollingMode _idlePollingMode = PollingMode.Timer;

        private static ManualResetEvent _sleepResetEvent = new ManualResetEvent(false);

        public static void HandlePlayingNotificationReceived(object? sender, PlexEventInfo e)
        {
            if (e.EventObj is PlayingEvent playEvent && playEvent.playState is PlayState playState)
            {
                if (Program.debugMode)
                {
                    WriteColor(
                    message: $"[Notification] Playback Update: Client={playEvent.clientIdentifier}, Key={playEvent.key}, State={playEvent.state}, Offset={playEvent.viewOffset}ms",
                    foreground: ConsoleColor.Cyan
                    );
                }

                if (playState == PlayState.Playing)
                {
                    if (_monitoringState == MonitoringState.Idle)
                    {
                        Console.WriteLine("Switching to active monitoring due to playback event.");
                        BreakFromIdle();
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

            //StartRefreshLoop(); // Will be called externally
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
            string playbackID = activeSession.Session.PlaybackID;

            if (_printDebugAll)
            {
                printDebug = true;
            }

            // Check if a monitor already exists for this session, if not create a new one
            if (_allMonitors.Any(m => m.PlaybackID == playbackID))
            {
                Console.WriteLine($"Monitor for session {playbackID} already exists. Not creating a new one.");
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
                WriteWarning($"Added new session for {activeSession.DeviceName}. Session Playback ID: {playbackID}");
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

        // Transfer the monitor from one session to another. Returns true if successful
        public static bool TransferMonitorInheritance(ActiveSession oldSession, ActiveSession newSession)
        {
            RewindMonitor? oldMonitor = _allMonitors.FirstOrDefault(m => m.PlaybackID == oldSession.PlaybackID);
            RewindMonitor? existingMonitorForNewSession = _allMonitors.FirstOrDefault(m => m.PlaybackID == newSession.PlaybackID);

            if (oldMonitor != null)
            {
                // Create a new monitor with the same settings as the old one using the duplication constructor
                RewindMonitor newMonitor = new RewindMonitor(
                    otherMonitor: oldMonitor,
                    newSession: newSession
                );

                // If there's already a monitor for the new session, remove it
                if (existingMonitorForNewSession != null)
                {
                    _allMonitors.Remove(existingMonitorForNewSession);
                }

                // Add the new monitor. Old session's monitor will be removed when its session is removed
                _allMonitors.Add(newMonitor);

                newSession.HasInheritedMonitor = true;
                Console.WriteLine($"Transferred {oldSession.DeviceName} monitoring state from {oldSession.PlaybackID} to {newSession.PlaybackID}");

                return true;
            }
            else
            {
                Console.WriteLine($"No monitor found for session {oldSession.PlaybackID}. Nothing to transfer.");
                return false;
            }
        }

        // This contains the main loop that refreshes the monitors and checks their state
        // It also handles the transition between active and idle states
        private static void PollingRefreshLoop()
        {
            while (_isRunning)
            {
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
                // If using event-based polling, use a long sleep time while idle
                else if (_idlePollingMode == PollingMode.Event)
                    sleepTime = DefaultWaitOnEventIdleFrequency_seconds * 1000;
                // Otherwise use the normal idle frequency
                else
                    sleepTime = _globalIdleFrequencyMs;

                // Wait on the event with a timeout, allowing for cancellation
                _sleepResetEvent.WaitOne(sleepTime);
            }

            Console.WriteLine("MonitorManager: Exiting polling refresh loop.");
            _isRunning = false; // Ensure state is updated on exit
        }

        public static void BreakFromIdle()
        {
            _monitoringState = MonitoringState.Active;

            // Signal the event to wake up any waiting thread
            _sleepResetEvent.Set();

            if (Program.debugMode)
                Console.WriteLine("Sleep canceled - switching to active monitoring immediately");
        }

        public static void StartMonitoringLoop()
        {
            if (_isRunning)
            {
                Console.WriteLine("MonitorManager: Refresh loop already running.");
                return;
            }
            if (_allMonitors.Count == 0)
            {
                WriteWarning("MonitorManager: No sessions to monitor. Loop not started.");
                // Optionally, start anyway if you expect sessions to appear later
                // return;
            }

            _isRunning = true;
            Console.WriteLine("MonitorManager: Starting polling refresh loop...");
            // Run the loop in a background thread so it doesn't block
            Task.Run(() => PollingRefreshLoop());
        }

        // Will return false if no monitors are active
        private static MonitoringState RunMonitors_OneIteration(List<RewindMonitor> monitorsToRefresh)
        {
            bool anyMonitorsActive = false;
            foreach (RewindMonitor monitor in monitorsToRefresh)
            {
                if (monitor.IsMonitoring)
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

        // Stop the polling loop but leave individual monitors in their current state
        public static void PauseMonitoringManager()
        {
            if (!_isRunning) // Only act if the loop is actually running
            {
                //WriteWarning("MonitorManager: Monitoring loop is already stopped.");
                return;
            }

            WriteWarning("MonitorManager: Stopping monitoring loop but keeping monitors...");
            _isRunning = false; // Signal the loop to stop
            _sleepResetEvent.Set(); // Wake up the sleeping thread immediately

            WriteWarning("MonitorManager: Monitoring loop stopped.");
        }

        // Ensure StopAllMonitoring sets _isRunning = false and cancels the sleep
        public static void RemoveAllMonitors()
        {
            // Stop subtitles if they are running and not user enabled
            foreach (RewindMonitor monitor in _allMonitors)
            {
                monitor.StopMonitoring(); // Stops them and disables subtitles if they were temporarily enabled
            }

            WriteWarning("MonitorManager: Stopping all monitoring...");
            _isRunning = false;
            _sleepResetEvent.Set(); // Wake up the sleeping thread

            lock (_allMonitors) // Ensure thread safety when clearing
            {
                _allMonitors.Clear();
            }
            // Reset state
            _monitoringState = MonitoringState.Idle;
            _idleGracePeriodCount = 0;
            WriteWarning("MonitorManager: Monitoring stopped and monitors cleared.");
        }

        // Ensure StopAndKeepAllMonitors sets _isRunning = false and cancels the sleep
        // Separate from when using _isRunning because this acts on the monitors, not the outer loop
        public static void StopAndKeepAllMonitorsIndividually()
        {
            WriteWarning("MonitorManager: Stopping monitoring loop but keeping monitors...");
            _isRunning = false;
            _sleepResetEvent.Set(); // Wake up the sleeping thread

            lock (_allMonitors) // Ensure thread safety when iterating
            {
                foreach (RewindMonitor monitor in _allMonitors)
                {
                    monitor.StopMonitoring(); // Stop individual monitors gracefully
                }
            }
            // Reset state
            _monitoringState = MonitoringState.Idle;
            _idleGracePeriodCount = 0;
            WriteWarning("MonitorManager: Monitoring loop stopped.");
        }

        public static void RestartAllMonitors()
        {
            WriteWarning("MonitorManager: Restarting all monitors...");
            lock (_allMonitors) // Ensure thread safety when iterating
            {
                foreach (RewindMonitor monitor in _allMonitors)
                {
                    monitor.RestartMonitoring();
                }
            }
            WriteWarning("MonitorManager: All monitors restarted.");
        }
    }
}