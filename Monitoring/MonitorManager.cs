#nullable enable

using System;

namespace RewindSubtitleDisplayerForPlex
{
    public static class MonitorManager
    {
        // Shared values / constants
        public static readonly double DefaultMaxRewindAmount =  Settings.Default().MaxRewindSec;
        public const int DefaultWaitOnEventIdleFrequency_seconds = 3600; // Used when preferring event-based polling when idle, so this will be very long
        public const int DefaultSmallestResolution = 5; // If using the viewOffset, it's usually 5 seconds but apparently can be as high as 10s
        public const int AccurateTimelineResolution = 1; // Assume this resolution if have the accurate timeline data

        private static readonly List<RewindMonitor> _allMonitors = [];
        private static int _globalActiveFrequencyMs = (int)Math.Round(Settings.Default().ActiveMonitorFrequencySec * 1000); // Initial value but will be updated as needed on the fly
        private static int _globalIdleFrequencyMs = (int)Math.Round(Settings.Default().IdleMonitorFrequency * 1000);
        private static bool _isRunning = false;
        private static MonitoringState _monitoringState = MonitoringState.Active;
        private static int _idleGracePeriodCount = 0; // Used to allow a few further checks before switching to idle state

        private static readonly ManualResetEvent _sleepResetEvent = new ManualResetEvent(false);

        // This occurs when the server sends a real-time notification about playback state, so we can use this to get instant updates out of phase with the polling
        public static void HandlePlayingNotificationReceived(object? sender, PlexEventInfo e)
        {
            _ = Task.Run(() =>// Handle notifications in a separate thread
            { 
                if (e.EventObj is PlayingEvent playEvent && playEvent.StateEnum is PlexPlayState playState)
                {
                    string offsetStr = playEvent.ViewOffset is double offset ? $"{Math.Round(offset / 1000).ToString()}s" : "null";
                    string notificationString = $"[Notification] Playback Update: Client={playEvent.ClientIdentifier}, Key={playEvent.Key}, State={playEvent.State}, Offset={offsetStr}";

                    // Currently we just use it to wake up from idle since the active polling is frequent enough, but we could use it to update the monitors too
                    if (playState == PlexPlayState.Playing)
                    {
                        LogDebug(notificationString, ConsoleColor.Cyan);

                        if (_monitoringState == MonitoringState.Idle)
                        {
                            LogVerbose("Switching to active monitoring due to playback event.");
                            BreakFromIdle();
                        }

                        // Only proceed if we have info we can use in the first place
                        if (playEvent.ViewOffset is double newViewOffset && newViewOffset != 0)
                        {
                            if (GetMonitorForMachineID(playEvent.ClientIdentifier) is RewindMonitor monitor)
                            {
                                monitor.AttachedSession.UpdateAccurateViewOffsetFromNotification(newViewOffset);
                                monitor.MakeMonitoringPass(isFromNotification: true); // Force a pass with the new offset

                                // If there's only one active session, restart the timer so it waits long enough for actual new info to come in
                                if (_allMonitors.Count <= 1)
                                    RestartPassTimer();
                            }
                            else
                            {
                                LogDebug($"No monitor found for machine {playEvent.ClientIdentifier}. Cannot update view offset.");
                            }
                        }
                    }
                    else if (playState == PlexPlayState.Paused)
                    {
                        LogDebug(notificationString, ConsoleColor.DarkCyan);
                        LogVerbose("   Playback Paused.");
                    }
                    else if (playState == PlexPlayState.Buffering)
                    {
                        LogDebugExtra(notificationString, ConsoleColor.DarkCyan);
                    }
                    else if (playState == PlexPlayState.Stopped)
                    {
                        LogDebug(notificationString, ConsoleColor.DarkMagenta);
                        LogVerbose("   Playback Stopped.");
                    }
                }
                else
                {
                    LogError($"[Notification] Received 'playing' event but couldn't parse data: {e.RawData}");
                }
            });
        }

        public static RewindMonitor? GetMonitorForMachineID(string? machineID)
        {
            if (string.IsNullOrEmpty(machineID))
            {
                LogDebug("Machine ID is null or empty. Cannot find monitor.");
                return null;
            }

            // Find the monitor for the given machine name
            RewindMonitor? monitor = _allMonitors.FirstOrDefault(m => m.MachineID == machineID);
            if (monitor != null)
            {
                return monitor;
            }
            else
            {
                LogDebug($"No monitor found for machine {machineID}.");
                return null;
            }
        }

        public static void CreateAllMonitoringAllSessions(List<ActiveSession> activeSessionList)
        {
            double maxRewindAmountSec = Program.config.MaxRewindSec;
            double activeFrequencySec = Program.config.ActiveMonitorFrequencySec;
            double idleFrequencySec = Program.config.IdleMonitorFrequency;

            _globalActiveFrequencyMs = (int)Math.Round((activeFrequencySec * 1000)); // Convert to milliseconds
            _globalIdleFrequencyMs = (int)Math.Round((idleFrequencySec * 1000));     // Convert to milliseconds

            foreach (ActiveSession activeSession in activeSessionList)
            {
                CreateMonitorForSession(
                    activeSession: activeSession,
                    activeFrequencySec: activeFrequencySec,
                    idleFrequencySec: idleFrequencySec,
                    maxRewindAmountSec: maxRewindAmountSec,
                    smallestResolutionSec: activeSession.SmallestResolutionExpected
                );
            }
        }

        public static void CreateMonitorForSession(
            ActiveSession activeSession,
            double maxRewindAmountSec,
            double activeFrequencySec,
            double idleFrequencySec,
            int smallestResolutionSec = DefaultSmallestResolution
            )
        {
            string playbackID = activeSession.Session.PlaybackID;

            // Check if a monitor already exists for this session, if not create a new one
            if (_allMonitors.Any(m => m.PlaybackID == playbackID))
            {
                LogDebug($"Monitor for session {playbackID} already exists. Not creating a new one.");
                return;
            }
            else
            {
                RewindMonitor monitor = new RewindMonitor(
                    activeSession,
                    activeFrequencySec: activeFrequencySec,
                    idleFrequencySec: idleFrequencySec,
                    maxRewindAmountSec: maxRewindAmountSec,
                    smallestResolution: smallestResolutionSec
                    );
                _allMonitors.Add(monitor);
                LogInfo($"Added new session for {activeSession.DeviceName}. Session Playback ID: {playbackID}", Yellow);
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
                LogDebug($"No monitor found for session {sessionID}. Nothing to remove.");
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
                LogVerbose($"Transferred {oldSession.DeviceName} monitoring state from {oldSession.PlaybackID} to {newSession.PlaybackID}");

                return true;
            }
            else
            {
                LogDebug($"No monitor found for session {oldSession.PlaybackID}. Nothing to transfer.");
                return false;
            }
        }


        private static bool skip = false; // Used to prevent skipping if the session is already at the end of the video
        // This contains the main loop that refreshes the monitors and checks their state
        // It also handles the transition between active and idle states
        private static void PollingRefreshLoop()
        {
            while (_isRunning)
            {
                if (skip == false)
                {   
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
                        LogVerbose("Switched to idle mode.");
                    else if (_monitoringState != previousState && _monitoringState == MonitoringState.Active)
                        LogVerbose("Switched to active mode.");

                    // Do the refresh right before the sleep so it will have time to process before the next pass
                    // This is async and will not block this loop
                    _ = SessionHandler.RefreshExistingActiveSessionsAsync(currentState: _monitoringState);
                }

                // Sleep for a while based on the current mode, but use the cancellable sleep mechanism
                int sleepTime;
                if (_monitoringState == MonitoringState.Active)
                    sleepTime = (int)_globalActiveFrequencyMs;
                // If using event-based polling (waiting for plex server notifications to break loop), use a long sleep time while idle
                else if (Program.config.UseEventPolling == true)
                    sleepTime = DefaultWaitOnEventIdleFrequency_seconds * 1000;
                // Otherwise use the normal idle frequency
                else
                    sleepTime = (int)_globalIdleFrequencyMs;

                // Wait on the event with a timeout, allowing for cancellation
                skip = false;
                _sleepResetEvent.Reset();
                _sleepResetEvent.WaitOne(millisecondsTimeout: sleepTime);
            }

            LogDebug("MonitorManager: Exiting polling refresh loop.");
            _isRunning = false; // Ensure state is updated on exit
        }

        // If the _sleepResetEvent is waiting, this will wake it up, but set a flag so the loop skips right back to waiting again with the full time
        // If the loop was already going, this will not affect it
        public static void RestartPassTimer()
        {
            skip = true; // Set the flag to skip the sleep
            _sleepResetEvent.Set(); // Wake up the sleeping thread
            LogDebugExtra("Polling timer reset - waiting for next pass.");
        }

        public static void BreakFromIdle()
        {
            _monitoringState = MonitoringState.Active;

            // Signal the event to wake up any waiting thread
            _sleepResetEvent.Set();

            LogDebug("Sleep canceled - switching to active monitoring immediately");
        }

        public static void StartMonitoringLoop()
        {
            if (_isRunning)
            {
                LogDebug("MonitorManager: Refresh loop already running.");
                return;
            }
            if (_allMonitors.Count == 0)
            {
                LogDebug("MonitorManager: No sessions to monitor. Loop not started.");
                // Optionally, start anyway if you expect sessions to appear later
                // return;
            }

            _isRunning = true;
            LogDebug("MonitorManager: Starting polling refresh loop...");
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

            LogDebug("MonitorManager: Stopping monitoring loop but keeping monitors...");
            _isRunning = false; // Signal the loop to stop
            _sleepResetEvent.Set(); // Wake up the sleeping thread immediately
        }

        // Ensure StopAllMonitoring sets _isRunning = false and cancels the sleep
        public static void RemoveAllMonitors()
        {
            // Stop subtitles if they are running and not user enabled
            foreach (RewindMonitor monitor in _allMonitors)
            {
                monitor.StopMonitoring(); // Stops them and disables subtitles if they were temporarily enabled
            }

            LogDebug("MonitorManager: Stopping all monitoring...");
            _isRunning = false;
            _sleepResetEvent.Set(); // Wake up the sleeping thread

            lock (_allMonitors) // Ensure thread safety when clearing
            {
                _allMonitors.Clear();
            }
            // Reset state
            _monitoringState = MonitoringState.Idle;
            _idleGracePeriodCount = 0;
            LogDebug("MonitorManager: Monitoring stopped and monitors cleared.");
        }

        // Ensure StopAndKeepAllMonitors sets _isRunning = false and cancels the sleep
        // Separate from when using _isRunning because this acts on the monitors, not the outer loop
        public static void StopAndKeepAllMonitorsIndividually()
        {
            LogDebug("MonitorManager: Stopping monitoring loop but keeping monitors...");
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
            LogDebug("MonitorManager: Monitoring loop stopped.");
        }

        public static void RestartAllMonitors()
        {
            LogDebug("MonitorManager: Restarting all monitors...");
            lock (_allMonitors) // Ensure thread safety when iterating
            {
                foreach (RewindMonitor monitor in _allMonitors)
                {
                    monitor.RestartMonitoring();
                }
            }
            LogDebug("MonitorManager: All monitors restarted.");
        }
    }
}