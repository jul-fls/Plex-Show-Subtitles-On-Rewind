namespace PlexShowSubtitlesOnRewind
{
    public static class MonitorManager
    {
        private static List<SessionRewindMonitor> _allMonitors = new List<SessionRewindMonitor>();

        public static void StartMonitoringAllSessions(
            List<ActiveSession> activeSessionList,
            int? frequency = null,
            int? maxRewindAmount = null)
        {
            foreach (ActiveSession activeSession in activeSessionList)
            {
                bool printDebug = activeSession.DeviceName == "Apple TV";
                SessionRewindMonitor monitor = new SessionRewindMonitor(activeSession, printDebug: printDebug);
                _allMonitors.Add(monitor);
            }
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