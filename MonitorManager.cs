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


        private static readonly List<SessionRewindMonitor> _allMonitors = [];
        private static int _globalActiveFrequencyMs = DefaultActiveFrequency;
        private static int _globalIdleFrequencyMs = DefaultIdleFrequency;
        private static bool _isRunning = false;
        private static bool _printDebugAll = false;

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
            string sessionID = activeSession.Session.SessionId;

            if (_printDebugAll)
            {
                printDebug = true;
            }

            // Check if a monitor already exists for this session, if not create a new one
            if (_allMonitors.Any(m => m.SessionID == sessionID))
            {
                Console.WriteLine($"Monitor for session {sessionID} already exists. Not creating a new one.");
                return;
            }
            else
            {
                SessionRewindMonitor monitor = new SessionRewindMonitor(
                    activeSession, 
                    activeFrequency: activeFrequency, 
                    idleFrequency: idleFrequency, 
                    maxRewindAmount: maxRewindAmount, 
                    printDebug: printDebug,
                    smallestResolution: smallestResolution
                    );
                _allMonitors.Add(monitor);
                WriteWarning($"Found and monitoring new session for {activeSession.DeviceName}");
            }
        }

        public static List<string> GetMonitoredSessions()
        {
            List<string> sessionIDs = [];
            foreach (SessionRewindMonitor monitor in _allMonitors)
            {
                sessionIDs.Add(monitor.SessionID);
            }
            return sessionIDs;
        }

        public static void RemoveMonitorForSession(string sessionID)
        {
            SessionRewindMonitor? monitor = _allMonitors.FirstOrDefault(m => m.SessionID == sessionID);
            if (monitor != null)
            {
                _allMonitors.Remove(monitor);
            }
            else
            {
                Console.WriteLine($"No monitor found for session {sessionID}. Nothing to remove.");
            }
        }

        private static void RefreshLoop()
        {
            while (_isRunning)
            {
                _ = SessionManager.RefreshExistingActiveSessionsAsync(); // Using discard since it's an async method, but we want this loop synchronous
                bool anyMonitorsActive = RefreshMonitors_OneIteration(_allMonitors);

                if (anyMonitorsActive == true)
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