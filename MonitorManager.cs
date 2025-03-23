#nullable enable

namespace PlexShowSubtitlesOnRewind
{
    public static class MonitorManager
    {
        // Shared values / constants
        public const int DefaultMaxRewindAmount = 60;
        public const int DefaultActiveFrequency = 1;
        public const int DefaultIdleFrequency = 5;
        public const int DefaultSmallestResolution = 5; // iPhone has 5 second resolution apparently

        private static List<SessionRewindMonitor> _allMonitors = [];
        private static int _activeFrequencyMs = DefaultActiveFrequency;
        private static int _idleFrequencyMs = DefaultIdleFrequency;
        private static bool _isRunning = false;

        public static void CreateAllMonitoringAllSessions(
            List<ActiveSession> activeSessionList,
            int activeFrequency = DefaultActiveFrequency,
            int idleFrequency = DefaultIdleFrequency,
            int maxRewindAmount = DefaultMaxRewindAmount,
            bool printDebugAll = false,
            string? debugDeviceName = null
            )
        {
            _activeFrequencyMs = activeFrequency * 1000; // Convert to milliseconds
            _idleFrequencyMs = idleFrequency * 1000;     // Convert to milliseconds

            foreach (ActiveSession activeSession in activeSessionList)
            {
                string sessionID = activeSession.Session.SessionId;
                bool printDebug = false;

                debugDeviceName = "Apple TV"; //DEBUG - Remove this later and use parameter / command line argument instead

                // Enable/Disable debugging per session depending on variables. Either for all devices or just a specific one
                if (printDebugAll == true || Utils.CompareStringsWithWildcards(debugDeviceName, activeSession.DeviceName))
                {
                    printDebug = true;
                }

                // Check if a monitor already exists for this session, if not create a new one
                if (_allMonitors.Any(m => m.SessionID == sessionID))
                {
                    continue;
                }
                else
                {
                    SessionRewindMonitor monitor = new SessionRewindMonitor(activeSession, frequency: activeFrequency, maxRewindAmount: maxRewindAmount, printDebug: printDebug);
                    _allMonitors.Add(monitor);
                }
            }

            StartRefreshLoop();
        }

        private static void StartRefreshLoop()
        {
            _isRunning = true;

            while (_isRunning)
            {
                _ = SessionManager.RefreshExistingActiveSessionsAsync(); // Using discard since it's an async method, but we want this loop synchronous
                bool anyMonitorsActive = RefreshMonitors_OneIteration(_allMonitors);

                if (anyMonitorsActive == true)
                    Thread.Sleep(_activeFrequencyMs);
                else
                    Thread.Sleep(_idleFrequencyMs);
            }
        }

        // Will return false if no monitors are active
        private static bool RefreshMonitors_OneIteration(List<SessionRewindMonitor> monitorsToRefresh)
        {
            bool anyMonitorsActive = false;
            foreach (SessionRewindMonitor monitor in monitorsToRefresh)
            {
                if (monitor.IsMonitoring) // This gets checked inside the loop also but is here for clarity. Might remove later
                {
                    anyMonitorsActive = true;
                    monitor.MakeMonitoringPass();
                }
            }
            return anyMonitorsActive;
        }

        public static void StopAllMonitors()
        {
            foreach (SessionRewindMonitor monitor in _allMonitors)
            {
                monitor.StopMonitoring();
            }
        }
    }
}