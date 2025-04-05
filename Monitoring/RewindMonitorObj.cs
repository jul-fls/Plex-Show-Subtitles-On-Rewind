namespace RewindSubtitleDisplayerForPlex
{
    // Monitors a single session for rewinding
    public class RewindMonitor
    {
        private readonly ActiveSession _activeSession;
        //private readonly PlexClient _client;
        private readonly int _activeFrequency;
        private readonly int _idleFrequency;
        private readonly int _maxRewindAmount;
        private readonly bool _printDebug;
        private readonly string _deviceName;

        private readonly int _fastForwardThreshold = 7; // Minimum amount of seconds to consider a fast forward (in seconds)

        private bool _isMonitoring;
        //private Thread _monitorThread;
        private bool _subtitlesUserEnabled;
        private double _latestWatchedPosition;
        private double _previousPosition;
        private bool _temporarilyDisplayingSubtitles;
        private int _smallestResolution; // This might be updated depending on available data during refreshes

        public string PlaybackID => _activeSession.Session.PlaybackID;
        public bool IsMonitoring => _isMonitoring;

        public RewindMonitor(
            ActiveSession session,
            int activeFrequency,
            int idleFrequency,
            int maxRewindAmount,
            bool printDebug = false,
            int smallestResolution = MonitorManager.DefaultSmallestResolution
            )
        {
            _activeSession = session;
            _activeFrequency = activeFrequency;
            _idleFrequency = idleFrequency;
            _maxRewindAmount = maxRewindAmount;
            _printDebug = printDebug;
            _deviceName = _activeSession.DeviceName;
            _idleFrequency = idleFrequency;
            _isMonitoring = false;
            _subtitlesUserEnabled = false;
            _latestWatchedPosition = 0;
            _previousPosition = 0;
            _temporarilyDisplayingSubtitles = false;
            _smallestResolution = Math.Max(_activeFrequency, smallestResolution);

            SetupMonitoringInitialConditions();
        }

        // Constructor that takes another monitor and creates a new one with the same settings to apply to a new session
        public RewindMonitor(RewindMonitor otherMonitor, ActiveSession newSession)
        {
            // Potentially updated values
            _activeSession = newSession;
            _deviceName = newSession.DeviceName;

            // Values that will be re-used
            _latestWatchedPosition = otherMonitor._latestWatchedPosition; // Ensures subtitle stopping point is the same for new session
            _activeFrequency = otherMonitor._activeFrequency;
            _idleFrequency = otherMonitor._idleFrequency;
            _maxRewindAmount = otherMonitor._maxRewindAmount;
            _printDebug = otherMonitor._printDebug;
            _idleFrequency = otherMonitor._idleFrequency;
            _isMonitoring = otherMonitor._isMonitoring;
            _subtitlesUserEnabled = otherMonitor._subtitlesUserEnabled;
            _previousPosition = otherMonitor._previousPosition;
            _temporarilyDisplayingSubtitles = otherMonitor._temporarilyDisplayingSubtitles;
            _smallestResolution = otherMonitor._smallestResolution;
        }

        private string GetTimeString(double seconds)
        {
            // ---------------- Local function -------------------
            static string SecondsToTimeString(double seconds)
            {
                TimeSpan time = TimeSpan.FromSeconds(seconds);
                if (time.Hours > 0)
                {
                    return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
                }
                else
                {
                    return $"{time.Minutes:D2}:{time.Seconds:D2}";
                }
            }
            // --------------------------------------------------

            if (_printDebug)
                return seconds.ToString() + $" ({SecondsToTimeString(seconds)})";
            else
                return SecondsToTimeString(seconds);
        }

        private void RewindOccurred()
        {
            WriteWarning($"{_deviceName}: Rewind occurred for {_activeSession.MediaTitle} - Will stop subtitles at time: {GetTimeString(_latestWatchedPosition)}");
            _activeSession.EnableSubtitles();
            _temporarilyDisplayingSubtitles = true;
        }

        // Disable subtitles but only if they were enabled by the monitor
        private void ReachedOriginalPosition()
        {
            WriteWarning($"{_deviceName}: Reached original position {GetTimeString(_latestWatchedPosition)} for {_activeSession.MediaTitle}");
            if (!_subtitlesUserEnabled)
            {
                _activeSession.DisableSubtitles();
            }
            _temporarilyDisplayingSubtitles = false;
        }

        public void StopSubtitlesIfNotUserEnabled()
        {
            if (_temporarilyDisplayingSubtitles)
            {
                _activeSession.DisableSubtitles();
                _temporarilyDisplayingSubtitles = false;
            }
        }

        // Disables subtitles regardless of how they were enabled
        private void ForceStopShowingSubtitles()
        {
            _activeSession.DisableSubtitles();
            _temporarilyDisplayingSubtitles = false;
        }

        public void MakeMonitoringPass()
        {
            try
            {
                double positionSec = _activeSession.GetPlayPositionSeconds();
                int _smallestResolution = Math.Max(_activeFrequency, _activeSession.SmallestResolutionExpected);
                if (_printDebug)
                {
                    Console.Write($"{_deviceName}: Position: {positionSec} | Latest: {_latestWatchedPosition} | Prev: {_previousPosition} |  -- UserEnabledSubs: ");
                    // Print last part about user subs with color if enabled so it's more obvious
                    if (_subtitlesUserEnabled)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                    }
                    Console.Write(_subtitlesUserEnabled);
                    Console.ResetColor();
                    Console.WriteLine();
                }

                    
                // If the user had manually enabled subtitles, check if they disabled them
                if (_subtitlesUserEnabled)
                {
                    _latestWatchedPosition = positionSec;
                    // If the active subtitles are empty, the user must have disabled them
                    if (_activeSession.HasActiveSubtitles() == false)
                    {
                        _subtitlesUserEnabled = false;
                    }
                }
                // If we know there are subtitles showing but we didn't enable them, then the user must have enabled them
                else if (!_temporarilyDisplayingSubtitles && _activeSession.KnownIsShowingSubtitles == true)
                {
                    _subtitlesUserEnabled = true;
                    _latestWatchedPosition = positionSec;
                    LogInfo($"{_deviceName}: User appears to have enabled subtitles manually.", Yellow);
                }
                // Only check for rewinds if the user hasn't manually enabled subtitles
                else
                {
                    // These all stop subtitles, so only bother if they are currently showing
                    if (_temporarilyDisplayingSubtitles)
                    {
                        // If the user fast forwards, stop showing subtitles
                        if (positionSec > _previousPosition + Math.Max(_smallestResolution + 2, _fastForwardThreshold)) //Setting minimum to 7 seconds to avoid false positives
                        {
                            LogInfo($"{_deviceName}: Force stopping subtitles for {_activeSession.MediaTitle} - Reason: User fast forwarded", Yellow);

                            _latestWatchedPosition = positionSec;
                            StopSubtitlesIfNotUserEnabled();
                        }
                        // If they rewind too far, stop showing subtitles, and reset the latest watched position
                        else if (positionSec < _latestWatchedPosition - _maxRewindAmount)
                        {
                            LogInfo($"{_deviceName}: Force stopping subtitles for {_activeSession.MediaTitle} - Reason: User rewound too far", Yellow);

                            _latestWatchedPosition = positionSec;
                            StopSubtitlesIfNotUserEnabled();
                        }
                        // Check if the position has gone back by the rewind amount. Don't update latest watched position here.
                        // Add smallest resolution to avoid stopping subtitles too early
                        else if (positionSec > _latestWatchedPosition + _smallestResolution)
                        {
                            ReachedOriginalPosition();
                        }
                    }
                    // Check if the position has gone back by 2 seconds. Using 2 seconds just for safety to be sure.
                    // This also will be valid if the user rewinds multiple times
                    // But don't count it if the rewind amount is beyond the max
                    else if ((positionSec < _latestWatchedPosition - 2) && !(positionSec < _latestWatchedPosition - _maxRewindAmount))
                    {
                        RewindOccurred();
                    }
                    // Otherwise update the latest watched position
                    else
                    {
                        _latestWatchedPosition = positionSec;
                    }
                }

                _previousPosition = positionSec;
            }
            catch (Exception e)
            {
                LogError($"{_deviceName}: Error in monitor iteration: {e.Message}");
                // Add a small delay to avoid tight loop on errors
                //Thread.Sleep(1000); // Moving the delay to more global loop
            }
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            StopSubtitlesIfNotUserEnabled();
        }

        public void RestartMonitoring()
        {
            _isMonitoring = true;
        }

        public void SetupMonitoringInitialConditions()
        {
            if (_isMonitoring)
            {
                Console.WriteLine("Already monitoring this session");
                return;
            }

            try
            {
                if (_activeSession.HasActiveSubtitles())
                {
                    _subtitlesUserEnabled = true;
                }

                _latestWatchedPosition = _activeSession.GetPlayPositionSeconds();
                if (_printDebug)
                {
                    Console.WriteLine($"Before thread start - position: {_latestWatchedPosition} -- Previous: {_previousPosition} -- UserEnabledSubtitles: {_subtitlesUserEnabled}\n");
                }

                _previousPosition = _latestWatchedPosition;
                _isMonitoring = true;

                MakeMonitoringPass(); // Run the monitoring pass directly instead of in a separate thread since they all need to be updated together anyway

                if (_printDebug)
                {
                    Console.WriteLine($"Finished setting up monitoring for {_deviceName} and ran first pass.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error during monitoring setup: {e.Message}");
                Console.WriteLine(e.StackTrace);
            }
        }
    }
}