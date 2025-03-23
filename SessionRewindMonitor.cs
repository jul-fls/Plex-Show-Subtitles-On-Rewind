namespace PlexShowSubtitlesOnRewind
{
    // Monitors a single session for rewinding
    public class SessionRewindMonitor
    {
        private readonly ActiveSession _activeSession;
        //private readonly PlexClient _client;
        private readonly int _frequency;
        private readonly int _maxRewindAmount;
        private readonly bool _printDebug;
        private readonly string _deviceName;

        private bool _isMonitoring;
        //private Thread _monitorThread;
        private bool _subtitlesUserEnabled;
        private double _latestWatchedPosition;
        private double _previousPosition;
        private bool _temporarilyDisplayingSubtitles;
        private readonly int _smallestResolution;

        public string SessionID => _activeSession.Session.SessionId;
        public bool IsMonitoring => _isMonitoring;

        public SessionRewindMonitor(
            ActiveSession session,
            //PlexClient client,
            int frequency,
            int maxRewindAmount,
            bool printDebug = false
            )
        {
            _activeSession = session;
            //_client = client;
            _frequency = frequency;
            _maxRewindAmount = maxRewindAmount;
            _printDebug = printDebug;
            _deviceName = _activeSession.DeviceName;

            _isMonitoring = false;
            _subtitlesUserEnabled = false;
            _latestWatchedPosition = 0;
            _previousPosition = 0;
            _temporarilyDisplayingSubtitles = false;
            _smallestResolution = Math.Max(_frequency, MonitorManager.DefaultSmallestResolution);

            SetupMonitoringInitialConditions();
        }

        private void RewindOccurred()
        {
            if (_printDebug)
            {
                Console.WriteLine($"{_deviceName}: Rewind occurred for {_activeSession.MediaTitle}");
            }
            ClientManager.EnableSubtitlesBySession(_activeSession);
            _temporarilyDisplayingSubtitles = true;
        }

        private void ReachedOriginalPosition()
        {
            if (_printDebug)
            {
                Console.WriteLine($"{_deviceName}: Reached original position for {_activeSession.MediaTitle}");
            }
            if (!_subtitlesUserEnabled)
            {
                ClientManager.DisableSubtitlesBySession(_activeSession);
            }
            _temporarilyDisplayingSubtitles = false;
        }

        private void ForceStopShowingSubtitles()
        {
            if (_printDebug)
            {
                Console.WriteLine($"{_deviceName}: Force stopping subtitles for {_activeSession.MediaTitle}");
            }
            ClientManager.DisableSubtitlesBySession(_activeSession);
        }

        public void MakeMonitoringPass()
        {
            if (_isMonitoring)
            {
                try
                {
                    double positionSec = _activeSession.GetPlayPositionSeconds();
                    if (_printDebug)
                    {
                        Console.WriteLine($"{_deviceName}: Loop iteration - position: {positionSec} -- Previous: {_previousPosition} -- Latest: {_latestWatchedPosition} -- UserEnabledSubtitles: {_subtitlesUserEnabled}\n");
                    }

                    // If the user had manually enabled subtitles, check if they disabled them
                    if (_subtitlesUserEnabled)
                    {
                        _latestWatchedPosition = positionSec;
                        // If the active subtitles are empty, the user must have disabled them
                        if (_activeSession.ActiveSubtitles == null || _activeSession.ActiveSubtitles.Count == 0)
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
                                _latestWatchedPosition = positionSec;
                                ForceStopShowingSubtitles();
                            }
                            // If they rewind too far, stop showing subtitles, and reset the latest watched position
                            else if (positionSec < _maxRewindAmount)
                            {
                                ForceStopShowingSubtitles();
                                _latestWatchedPosition = positionSec;
                            }
                            // Check if the position has gone back by the rewind amount. Don't update latest watched position here.
                            // Add smallest resolution to avoid stopping subtitles too early
                            else if (positionSec > _latestWatchedPosition + _smallestResolution)
                            {
                                ReachedOriginalPosition();
                            }
                        }
                        // Check if the position has gone back by 2 seconds. Using 2 seconds just for safety to be sure.
                        // This also will be valid if the user rewinds multiple times up to the maximum rewind amount
                        else if (positionSec < _latestWatchedPosition - 2)
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
                    Console.WriteLine($"{_deviceName}: Error in monitor iteration: {e.Message}");
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
            //if (_monitorThread != null && _monitorThread.IsAlive)
            //{
            //    _monitorThread.Join(2000); // Wait up to 2 seconds for thread to finish
            //}
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
                if (_activeSession.ActiveSubtitles != null && _activeSession.ActiveSubtitles.Count > 0)
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

                //if (_printDebug)
                //{
                //    Console.WriteLine("About to start thread with target: MonitoringLoop");
                //}

                //_monitorThread = new Thread(MakeMonitoringPass);
                //_monitorThread.IsBackground = false; // Continue running. If the main thread is forced to stop, this will also stop though
                //_monitorThread.Start();

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