namespace PlexShowSubtitlesOnRewind
{
    // Monitors a single session for rewinding
    public class SessionRewindMonitor
    {
        private readonly ActiveSession _activeSession;
        //private readonly PlexClient _client;
        private readonly int _activeFrequency;
        private readonly int _idleFrequency;
        private readonly int _maxRewindAmount;
        private readonly bool _printDebug;
        private readonly string _deviceName;

        private bool _isMonitoring;
        //private Thread _monitorThread;
        private bool _subtitlesUserEnabled;
        private double _latestWatchedPosition;
        private double _previousPosition;
        private bool _temporarilyDisplayingSubtitles;
        private int _smallestResolution; // This might be updated depending on available data during refreshes

        public string SessionID => _activeSession.Session.SessionId;
        public bool IsMonitoring => _isMonitoring;

        public SessionRewindMonitor(
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

        private void RewindOccurred()
        {
            if (_printDebug)
            {
                WriteWarning($"{_deviceName}: Rewind occurred for {_activeSession.MediaTitle}");
            }
            _activeSession.EnableSubtitles();
            _temporarilyDisplayingSubtitles = true;
        }

        private void ReachedOriginalPosition()
        {
            if (_printDebug)
            {
                WriteWarning($"{_deviceName}: Reached original position for {_activeSession.MediaTitle}");
            }
            if (!_subtitlesUserEnabled)
            {
                _activeSession.DisableSubtitles();
            }
            _temporarilyDisplayingSubtitles = false;
        }

        private void ForceStopShowingSubtitles()
        {
            _activeSession.DisableSubtitles();
            _temporarilyDisplayingSubtitles = false;
        }

        public void MakeMonitoringPass()
        {
            if (_isMonitoring)
            {
                try
                {
                    double positionSec = _activeSession.GetPlayPositionSeconds();
                    int _smallestResolution = Math.Max(_activeFrequency, _activeSession.SmallestResolutionExpected);
                    if (_printDebug)
                    {
                        Console.Write($"{_deviceName}: Position: {positionSec} | Latest: {_latestWatchedPosition} | Prev: {_previousPosition} |  -- UserEnabledSubs: ");
                        // Print last part about user subs with color if enabled so it's more obvious
                        string appendString = "";
                        if (_subtitlesUserEnabled)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            appendString = " (NEVER SHOWING SUBTITLES)";
                        }
                        Console.Write($"{_subtitlesUserEnabled}" + appendString);
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
                    // Only check for rewinds if the user hasn't manually enabled subtitles
                    else
                    {
                        // These all stop subtitles, so only bother if they are currently showing
                        if (_temporarilyDisplayingSubtitles)
                        {
                            // If the user fast forwards, stop showing subtitles
                            if (positionSec > _previousPosition + _smallestResolution + 2)
                            {
                                if (_printDebug)
                                    WriteWarning($"{_deviceName}: Force stopping subtitles for {_activeSession.MediaTitle} - Reason: User fast forwarded");

                                _latestWatchedPosition = positionSec;
                                ForceStopShowingSubtitles();
                            }
                            // If they rewind too far, stop showing subtitles, and reset the latest watched position
                            else if (positionSec < _latestWatchedPosition - _maxRewindAmount)
                            {
                                if (_printDebug)
                                    WriteWarning($"{_deviceName}: Force stopping subtitles for {_activeSession.MediaTitle} - Reason: User rewound too far");

                                _latestWatchedPosition = positionSec;
                                ForceStopShowingSubtitles();
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
                    WriteError($"{_deviceName}: Error in monitor iteration: {e.Message}");
                    // Add a small delay to avoid tight loop on errors
                    //Thread.Sleep(1000); // Moving the delay to more global loop
                }
            }
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            if (_temporarilyDisplayingSubtitles)
            {
                ForceStopShowingSubtitles();
            }
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