using System.ComponentModel;

namespace RewindSubtitleDisplayerForPlex
{
    // Monitors a single session for rewinding
    public class RewindMonitor
    {
        private readonly ActiveSession _activeSession;
        //private readonly PlexClient _client;
        private readonly double _activeFrequencySec;
        private readonly double _idleFrequencySec;
        private readonly double _maxRewindAmountSec;
        private readonly string _deviceName;

        private readonly int _fastForwardThreshold = 7; // Minimum amount of seconds to consider a fast forward (in seconds)

        private bool _isMonitoring;
        private bool _isDead;
        private bool _isSetup = false;
        private bool _subtitlesUserEnabled;
        private double _latestWatchedPosition;
        private double _previousPosition; // Use to detect fast forwards
        private bool _temporarilyDisplayingSubtitles;
        private readonly double _smallestResolutionSec; // This might be updated depending on available data during refreshes

        public string PlaybackID => _activeSession.Session.PlaybackID;
        public bool IsMonitoringAndNotDead => ((_isMonitoring == true) && (_isDead == false));
        public bool IsMonitoring => _isMonitoring;
        public ActiveSession AttachedSession => _activeSession;
        public string MachineID { get => _activeSession.MachineID; }
        public bool SubtitlesAreShowing => (_activeSession.KnownIsShowingSubtitles == true); // || _temporarilyDisplayingSubtitles);

        private bool isOnMaxRewindCooldown = false;
        private bool waitedInitialPeriod = false; // Used to check if we waited the initial period before showing subtitles

        // After disabling subtitles there is a delay before they actually stop showing, so we need to wait for that
        // Use this to check if subtitles have been disabled yet after we do, to avoid false positive that user enabled them
        private bool isOnPendingDisabledCooldown = false;

        // First 5 characters of the playback ID
        private string PlaybackIDShort => PlaybackID.Substring(0, 5); // Get the last 5 characters of the playback ID

        public RewindMonitor(
            ActiveSession session,
            double activeFrequencySec,
            double idleFrequencySec,
            double maxRewindAmountSec,
            int smallestResolution = MonitorManager.DefaultSmallestResolution
            )
        {
            _activeSession = session;
            _activeFrequencySec = activeFrequencySec;
            _idleFrequencySec = idleFrequencySec;
            _maxRewindAmountSec = maxRewindAmountSec;
            _deviceName = _activeSession.DeviceName;
            _idleFrequencySec = idleFrequencySec;
            _isMonitoring = false;
            _isDead = false;
            _subtitlesUserEnabled = false;
            _latestWatchedPosition = 0;
            _previousPosition = 0;
            _temporarilyDisplayingSubtitles = false;
            _smallestResolutionSec = Math.Max(_activeFrequencySec, smallestResolution);

            // Setup within constructor
            try
            {
                if (_activeSession.KnownIsShowingSubtitles == true)
                {
                    _subtitlesUserEnabled = true;
                }

                _latestWatchedPosition = _activeSession.GetPlayPositionSeconds();
                LogDebugExtra($"Before thread start - position: {_latestWatchedPosition} -- Previous: {_previousPosition} -- UserEnabledSubtitles: {_subtitlesUserEnabled}\n");

                _previousPosition = _latestWatchedPosition;

                if (Program.config.ManualModeOnly == true || Program.config.AlwaysEnableSubtitlesMode == true)
                    _isMonitoring = false;
                else
                    _isMonitoring = true;

                if (Program.config.AlwaysEnableSubtitlesMode == true)
                {
                    StartSubtitlesWithRetry(persist: true); // Start subtitles immediately if in AlwaysEnableSubtitlesMode
                    LogDebug($"AlwaysEnableSubtitlesMode is enabled - Starting subtitles immediately for {_activeSession.MediaTitle}");
                }
                else if (Program.config.RememberSubtitlesForTVShowMode == true)
                {
                    // Check if the show is in the remembered list
                    if (_activeSession.IsRememberedEnabledSubs())
                    {
                        StartSubtitlesWithRetry(persist: true); // Start subtitles immediately if in AlwaysEnableSubtitlesMode
                        LogDebug($"Remembered subtitles for {_activeSession.MediaTitle} - Starting subtitles immediately.");
                    }
                }

                SimpleSessionStartTimer();
                MakeMonitoringPass(); // Run the monitoring pass directly instead of in a separate thread since they all need to be updated together anyway

                LogDebug($"Finished setting up monitoring for {_deviceName} and ran first pass.");
            }
            catch (Exception e)
            {
                LogError($"Error during monitoring setup: {e.Message}");
                if (Program.config.ConsoleLogLevel >= LogLevel.Debug)
                    WriteLineSafe(e.StackTrace);
            }
        }

        // Constructor that takes another monitor and creates a new one with the same settings to apply to a new session
        public RewindMonitor(RewindMonitor otherMonitor, ActiveSession newSession)
        {
            // Potentially updated values
            _activeSession = newSession;
            _deviceName = newSession.DeviceName;

            // Values that will be re-used
            _latestWatchedPosition = otherMonitor._latestWatchedPosition; // Ensures subtitle stopping point is the same for new session
            _activeFrequencySec = otherMonitor._activeFrequencySec;
            _idleFrequencySec = otherMonitor._idleFrequencySec;
            _maxRewindAmountSec = otherMonitor._maxRewindAmountSec;
            _idleFrequencySec = otherMonitor._idleFrequencySec;
            _isMonitoring = otherMonitor._isMonitoring;
            _subtitlesUserEnabled = otherMonitor._subtitlesUserEnabled;
            _previousPosition = otherMonitor._previousPosition;
            _temporarilyDisplayingSubtitles = otherMonitor._temporarilyDisplayingSubtitles;
            _smallestResolutionSec = otherMonitor._smallestResolutionSec;

            SimpleSessionStartTimer();
        }

        private static string GetTimeString(double seconds)
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

            if (Program.config.ConsoleLogLevel >= LogLevel.Debug)
                return seconds.ToString() + $" ({SecondsToTimeString(seconds)})";
            else
                return SecondsToTimeString(seconds);
        }

        private void RewindOccurred()
        {
            if (discardNextPass)
                return;

            if (!waitedInitialPeriod)
            {
                LogWarning($"{_deviceName} [{PlaybackIDShort}]: Rewind occurred, but cannot start subtitles too soon after the player begins because it won't work right.");
                return;
            }

            LogInfo($"{_deviceName} [{PlaybackIDShort}]: Rewind occurred for {_activeSession.MediaTitle} - Will stop subtitles at time: {GetTimeString(_latestWatchedPosition)}", Yellow);
            StartSubtitlesWithRetry();
            _temporarilyDisplayingSubtitles = true;
        }

        // Disable subtitles but only if they were enabled by the monitor
        private void ReachedOriginalPosition()
        {
            if (discardNextPass)
                return;

            LogInfo($"{_deviceName} [{PlaybackIDShort}]: Reached original position {GetTimeString(_latestWatchedPosition)} for {_activeSession.MediaTitle}", Yellow);
            StopSubtitlesWithRetry(false);
        }

        public void StartSubtitlesWithRetry(bool persist = false)
        {
            int attemptCount = 3;
            bool? success = false;

            if (_activeSession.AvailableSubtitles.Count == 0)
            {
                LogWarning($"{_deviceName} [{PlaybackIDShort}]: No available subtitles for {_activeSession.MediaTitle}. Cannot start subtitles.", Yellow);
                return;
            }

            _ = Task.Run(async () =>
            {
                // Wait an initial period before enabling subtitles to avoid issues with the player not responding
                //await Task.Delay(250);

                while (attemptCount > 0 && success == false)
                {
                    success = await _activeSession.EnableSubtitles();
                    if (success == true)
                    {
                        if (persist == false)
                            _temporarilyDisplayingSubtitles = true;
                        break;
                    }
                    else if (success == false)
                    {
                        attemptCount--;
                        LogDebugExtra($"{_deviceName} [{PlaybackIDShort}]: {attemptCount} retries remaining to enable subtitles.");

                        if (attemptCount > 0)
                            await Task.Delay(150); // Short delay before retrying
                        else
                            LogError($"{_deviceName} [{PlaybackIDShort}]: Failed to enable subtitles for {_activeSession.MediaTitle} after multiple attempts.");
                    }
                    else // It's null which means no available subtitles, so don't bother retrying
                    {
                        break;
                    }
                }

                // Test workaround to seek to the same position to force subtitles to show
                //if (success == true)
                //{
                //    int position = _activeSession.GetPlayPositionMilliseconds() - 1000;
                //    string machineID = _activeSession.MachineID;
                //    await PlexServer.SeekToTime(position, machineID, true, _activeSession);
                //    LogDebugExtra($"{_deviceName} [{PlaybackIDShort}]: Seeking to {position}ms to force subtitles to show.");
                //}

            });
        }

        public void StopSubtitlesWithRetry(bool force)
        {
            if (_temporarilyDisplayingSubtitles || force == true)
            {
                int attemptCount = 3;
                bool success = false;

                _ = Task.Run(async () =>
                {
                    while (attemptCount > 0 && !success)
                    {
                        success = await _activeSession.DisableSubtitles();
                        if (success)
                        {
                            _temporarilyDisplayingSubtitles = false;
                            StartPendingDisabledCooldown();
                            break;
                        }
                        else
                        {
                            attemptCount--;
                            LogDebugExtra($"{_deviceName} [{PlaybackIDShort}]: {attemptCount} retries remaining to disable subtitles.");

                            if (attemptCount > 0)
                                await Task.Delay(150); // Short delay before retrying
                            else
                                LogError($"{_deviceName} [{PlaybackIDShort}]: Failed to disable subtitles for {_activeSession.MediaTitle} after multiple attempts.");
                        }
                    }
                });
            }
        }

        private void SetLatestWatchedPosition(double newTime)
        {
            // If we're in a cooldown, that means the user might still be rewinding further,
            // so we don't want to update the latest watched position until the cooldown is over,
            // otherwise when they finish rewinding beyond the max it might result in showing subtitles again
            if (!isOnMaxRewindCooldown && !discardNextPass)
                _latestWatchedPosition = newTime;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0071:Simplify interpolation")]
        private void PrintTimelineDebugMessage(double positionSec, bool isFromNotification, bool temporarySubtitlesWereEnabledForPass, string prepend="", bool discarded=false)
        {
            // Local function to pad true/false values
            static string PadBool(bool boolVal, bool left=true)
            {
                if (left)
                    return boolVal.ToString().PadLeft(5);
                else
                    return boolVal.ToString().PadRight(5);
            }

            string subtitlesStatus = _activeSession.KnownIsShowingSubtitles.HasValue
                ? PadBool(_activeSession.KnownIsShowingSubtitles.Value)
                : "Null ";

            string expectedShowingSubs;
            if (isOnPendingDisabledCooldown)
                expectedShowingSubs = "Wait.";
            else if (_activeSession.KnownIsShowingSubtitles == true)
                expectedShowingSubs = PadBool(true, left:false);
            else if (_activeSession.KnownIsShowingSubtitles == false)
                expectedShowingSubs = PadBool(false, left:false);
            else
                expectedShowingSubs = "Null ";

            string msgPart1 = $"           " + prepend + 
                $"> {_deviceName} [{PlaybackIDShort}]: Position: {positionSec.ToString().PadLeft(7)} " +
                $"| Latest: {Math.Round(_latestWatchedPosition).ToString().PadLeft(5)} " + // Round to whole number and pad spaces to 4 digits
                $"| Prev: {Math.Round(_previousPosition).ToString().PadLeft(5)} " +
                $"|  Actually/Expected Showing Subs: {subtitlesStatus}/{expectedShowingSubs} " +
                $"| FromNotification: {PadBool(isFromNotification)} " + // Not currently using notifications
                $"| UserEnabledSubs: ";

            string msgPart2 = _subtitlesUserEnabled.ToString();

            // Print last part about user subs with color if enabled so it's more obvious
            if (_subtitlesUserEnabled)
            {
                ConsoleColor msgColor = ConsoleColor.White;

                if (discarded == true)
                    msgColor = ConsoleColor.DarkGray;

                WriteColorParts(msgPart1, msgPart2, msgColor, ConsoleColor.Red);
            }
            else
            {
                if (discarded)
                    WriteColor(msgPart1 + msgPart2, ConsoleColor.DarkGray);
                else
                    WriteLineSafe(msgPart1 + msgPart2);

                Task.Run(() => MyLogger.LogToFile(msgPart1 + msgPart2));
            }
        }

        private bool discardNextPass = false; // Used to discard the next pass if it was triggered by a notification since it will be out of date

        // This is a point-in-time function that will stop subtitles based on last known position and collected data
        // It might be called from a polling loop at a regular interval, or can be updated 'out-of-phase' from a plex server notification
        //      Doing so should not interrupt the loop intervals but will allow for more instant reactions to user input
        public void MakeMonitoringPass(bool isFromNotification = false)
        {
            try
            {
                double positionSec = _activeSession.GetPlayPositionSeconds();

                // If the position is the same as before, we don't have any new info so we might not want to do anything
                if (Program.config.IgnoreMessagesSameOffset && positionSec == _previousPosition)
                {
                    string type = isFromNotification ? "Notification" : "Polling";
                    LogDebugExtra($"{_deviceName} [{PlaybackIDShort}]: Ignoring {type} message without new data.");

                    // Set the discardNextPass flag (whether enabling or disabling) because this pass should still count
                    if (isFromNotification && Program.config.DiscardNextPass)
                        discardNextPass = true;
                    else
                    {
                        discardNextPass = false;
                    }

                    return;
                }

                // We always want to use notification info if available, but this will be set to true again at the end of the pass
                // This won't skip the entire loop like the duplicate message check does, it just skips certain parts.
                // Because there is still some logic that needs to run even if the position is the same
                if (isFromNotification)
                    discardNextPass = false;

                bool temporarySubtitlesWereEnabledForPass = _temporarilyDisplayingSubtitles; // Store the value before it gets updated to print at the end
                double _smallestResolution = Math.Max(_activeFrequencySec, _activeSession.SmallestResolutionExpected);

                if (isOnPendingDisabledCooldown && _activeSession.KnownIsShowingSubtitles == false)
                {
                    StopPendingDisableCooldownNow();
                    LogDebugExtra($"{_deviceName} [{PlaybackIDShort}]: Ended Pending Disabled cooldown because subtitles are now known as disabled.");
                }

                // If the user had manually enabled subtitles, check if they disabled them
                if (_subtitlesUserEnabled)
                {
                    SetLatestWatchedPosition(positionSec);

                    // If the active subtitles are empty, the user must have disabled them
                    if (_activeSession.KnownIsShowingSubtitles == false)
                    {
                        _subtitlesUserEnabled = false;

                        if (_activeSession.IsTVShow())
                            MonitorManager.RemoveFromRememberedShows(_activeSession.MediaTitle);

                        LogInfo($"{_deviceName} [{PlaybackIDShort}]: User appears to have disabled subtitles manually.", Yellow);
                        
                    }
                }
                // If we know there are subtitles showing but we didn't enable them, then the user must have enabled them.
                // In this case again we don't want to stop them, so this is an else-if to prevent it falling through to the else
                else if (!_temporarilyDisplayingSubtitles && _activeSession.KnownIsShowingSubtitles == true && !isOnPendingDisabledCooldown)
                {
                    _subtitlesUserEnabled = true;
                    SetLatestWatchedPosition(positionSec);

                    if (_activeSession.IsTVShow())
                        MonitorManager.AddToRememberedShows(_activeSession.MediaTitle);

                    if (Program.config.RememberSubtitlesForTVShowMode == false || !_activeSession.IsRememberedEnabledSubs())
                        LogInfo($"{_deviceName} [{PlaybackIDShort}]: User appears to have enabled subtitles manually.", Yellow);
                }
                // Only further process & check for rewinds if the user hasn't manually enabled subtitles
                else
                {
                    // These all stop subtitles, so only bother if they are currently showing
                    if (_temporarilyDisplayingSubtitles)
                    {
                        // If the user fast forwards, stop showing subtitles
                        if (positionSec > _previousPosition + Math.Max(_smallestResolution + 2, _fastForwardThreshold)) //Setting minimum to 7 seconds to avoid false positives
                        {
                            LogInfo($"{_deviceName} [{PlaybackIDShort}]: Force stopping subtitles for {_activeSession.MediaTitle} - Reason: User fast forwarded", Yellow);

                            SetLatestWatchedPosition(positionSec);
                            StopSubtitlesWithRetry(false);

                        }
                        // If they rewind too far, stop showing subtitles, and reset the latest watched position
                        else if (positionSec < _latestWatchedPosition - _maxRewindAmountSec)
                        {
                            LogInfo($"{_deviceName} [{PlaybackIDShort}]: Force stopping subtitles for {_activeSession.MediaTitle} - Reason: User rewound too far. Initiating cooldown.", Yellow);

                            SetLatestWatchedPosition(positionSec);
                            StopSubtitlesWithRetry(false);

                            // Initiate a cooldown, because if the user is rewinding in steps with a remote with brief pauses,
                            //      further rewinds may be interpreted as rewinds to show subtitles again
                            StartMaxRewindCooldown();
                        }
                        // Check if the position has gone back by the rewind amount.
                        // Add smallest resolution to avoid stopping subtitles too early
                        else if (positionSec > _latestWatchedPosition + _smallestResolution)
                        {
                            ReachedOriginalPosition();
                            SetLatestWatchedPosition(positionSec);
                        }
                    }
                    // Special handling during cooldown
                    else if (isOnMaxRewindCooldown)
                    {
                        // If they have fast forwarded
                        if (positionSec > _previousPosition + Math.Max(_smallestResolution + 2, _fastForwardThreshold)) //Setting minimum to 7 seconds to avoid false positives
                        {
                            LogInfo($"{_deviceName} [{PlaybackIDShort}]: Cancelling cooldown - Reason: User fast forwarded during cooldown", Yellow);
                            StopMaxRewindCooldownNow(); // Reset cooldown
                        }
                        else
                        {
                            LogDebug($"{_deviceName} [{PlaybackIDShort}]: Still on cooldown.");

                            // If the user rewinded again while in cooldown, we want to reset the cooldown
                            if (positionSec < _previousPosition - 2)
                            {
                                RestartMaxRewindCooldownTimer();
                            }
                        }

                        SetLatestWatchedPosition(positionSec);
                    }
                    // Check if the position has gone back by 2 seconds or more. Using 2 seconds just for safety to be sure.
                    // But don't count it if the rewind amount goes beyond the max.
                    // Since at this point it isn't displaying subtitles we can technically use either _previousPosition or _latestWatchedPosition to check for rewinds.
                    // Only _previousPosition works with the cooldown but that doesn't matter because we handle that in the other else if
                    else if ((positionSec < _previousPosition - 2) && !(positionSec < _previousPosition - _maxRewindAmountSec))
                    {
                        RewindOccurred();
                    }
                    // Otherwise update the latest watched position
                    else
                    {
                        SetLatestWatchedPosition(positionSec);
                    }
                }

                // Print the timeline debug message at the end of the pass so the watch position related data is updated
                // But use the temporary subtitles value from the start of the pass because any changes wouldn't have taken effect yet because the player takes time to do it
                if (discardNextPass)
                {
                    if (Program.config.ConsoleLogLevel >= LogLevel.DebugExtra) PrintTimelineDebugMessage(positionSec, isFromNotification, temporarySubtitlesWereEnabledForPass, prepend: "Discarded: ", discarded:true);
                }
                else
                {
                    if (Program.config.ConsoleLogLevel >= LogLevel.Debug) PrintTimelineDebugMessage(positionSec, isFromNotification, temporarySubtitlesWereEnabledForPass);
                }
                    

                _previousPosition = positionSec;

                // ----- Finally set or reset discard flag ------
                // DiscardNextPass will make certain actions not happen unless the pass was triggered by a notification
                if (isFromNotification == true) // From a notification
                {
                    if (Program.config.DiscardNextPass == true)
                        discardNextPass = true;
                }
                else // From the polling loop
                {
                    if (discardNextPass)
                    {
                        LogDebugExtra($"{_deviceName} [{PlaybackIDShort}]: Discarding current pass due to notification.");
                        discardNextPass = false; // Reset for next time
                    }
                }

            }
            catch (Exception e)
            {
                LogError($"{_deviceName} [{PlaybackIDShort}]: Error in monitor iteration: {e.Message}");
                // Add a small delay to avoid tight loop on errors
                //Thread.Sleep(1000); // Moving the delay to more global loop
            }
        }

        // ------------------ Max Rewind Cooldown Logic ------------------ //

        // Function that sets isOnCooldown to true, waits 5 seconds on another thread, then sets it back to false
        private readonly ManualResetEvent _maxRewindCooldownResetEvent = new ManualResetEvent(false);
        private bool _maxRewindCooldownTimerLoop_DontDisableCooldown = false;
        private void StartMaxRewindCooldown()
        {
            int cooldownMs = (int)Program.config.MaxRewindCoolDownSec * 1000;

            // -------------------- Check whether to skip ---------------------------

            if (cooldownMs == 0)
            {
                LogDebug("Max Rewind Cooldown disabled in settings (value is 0), not starting.");
                return;
            }

            // Automatically restart the cooldown if it was already running. Can also separately call associated Restart method to do this
            if (isOnMaxRewindCooldown)
            {
                _maxRewindCooldownTimerLoop_DontDisableCooldown = true;
                _maxRewindCooldownResetEvent.Set(); // Wake up the sleeping thread
                LogDebug("Max Rewind Sleep timer reset.", ConsoleColor.DarkYellow);
                return;
            }
            else
            {
                LogDebug("COOLDOWN STARTED: Starting rewind cooldown.", ConsoleColor.DarkYellow);
            }

            // -------------------- Actual Logic ---------------------------

            // -- Wait on the event with a timeout, allowing for cancellation --
            // The loop behavior is: Setup the event, wait for the timer, then disable the cooldown
            //    HOWEVER if the timer was reset, the 'disable cooldown' flag will have been set to true,
            //    so it will pass over the cooldown disable line and restart to hit the delay again
            isOnMaxRewindCooldown = true;
            while (isOnMaxRewindCooldown) 
            {
                _maxRewindCooldownTimerLoop_DontDisableCooldown = false;
                _maxRewindCooldownResetEvent.Reset(); // Reset the event for the next wait. In this case it is like filling up the hourglass again
                _maxRewindCooldownResetEvent.WaitOne(millisecondsTimeout: cooldownMs); // Wait on the event, with a timeout. Note: Can also be ended early using Set() on the event

                // After the timer expires or is cancelled in the WaitOne line, the code is 'released' and allowed to continue here
                if (!_maxRewindCooldownTimerLoop_DontDisableCooldown)
                {
                    isOnMaxRewindCooldown = false;
                    LogVerbose("COOLDOWN ENDED: Max Rewind Cooldown", ConsoleColor.DarkYellow);
                    break;
                }
            }
        }

        public void StopMaxRewindCooldownNow()
        {
            isOnMaxRewindCooldown = false;
            _maxRewindCooldownTimerLoop_DontDisableCooldown = false;
            _maxRewindCooldownResetEvent.Set(); // Wake up the sleeping thread
            LogDebug("Max Rewind Cooldown timer stopped.");
        }

        public void RestartMaxRewindCooldownTimer()
        {
            _maxRewindCooldownTimerLoop_DontDisableCooldown = true;
            _maxRewindCooldownResetEvent.Set(); // Wake up the sleeping thread
            LogDebug("Max Rewind Sleep timer reset.");
        }

        // ---------------------------------------------------------------------

        // Function that sets isOnCooldown to true, waits 5 seconds on another thread, then sets it back to false
        private readonly ManualResetEvent _pendingDisabledCooldownResetEvent = new ManualResetEvent(false);
        private bool _pendingDisabledCooldownTimerLoop_DontDisableCooldown = false;
        private void StartPendingDisabledCooldown()
        {
            int cooldownMs = (int)Program.config.PendingDisabledCooldownSec * 1000;

            // -------------------- Check whether to skip ---------------------------

            if (cooldownMs == 0)
            {
                LogDebug("Subtitle-Disabled Cooldown disabled in settings (value is 0), not starting.");
                isOnPendingDisabledCooldown = false;
                return;
            }

            // Automatically restart the cooldown if it was already running. Can also separately call associated Restart method to do this
            if (isOnPendingDisabledCooldown)
            {
                _pendingDisabledCooldownTimerLoop_DontDisableCooldown = true;
                _pendingDisabledCooldownResetEvent.Set(); // Wake up the sleeping thread
                LogDebug("Subtitle-Disabled Sleep timer reset.", ConsoleColor.DarkYellow);
                return;
            }
            else
            {
                LogVerbose("COOLDOWN STARTED: Subtitle-Disabled Cooldown", ConsoleColor.DarkYellow);
            }

            // -------------------- Actual Logic ---------------------------

            // -- Wait on the event with a timeout, allowing for cancellation --
            // The loop behavior is: Setup the event, wait for the timer, then disable the cooldown
            //    HOWEVER if the timer was reset, the 'disable cooldown' flag will have been set to true,
            //    so it will pass over the cooldown disable line and restart to hit the delay again
            isOnPendingDisabledCooldown = true;
            while (isOnPendingDisabledCooldown)
            {
                _pendingDisabledCooldownTimerLoop_DontDisableCooldown = false;
                _pendingDisabledCooldownResetEvent.Reset(); // Reset the event for the next wait. In this case it is like filling up the hourglass again
                _pendingDisabledCooldownResetEvent.WaitOne(millisecondsTimeout: cooldownMs); // Wait on the event, with a timeout. Note: Can also be ended early using Set() on the event

                // After the timer expires or is cancelled in the WaitOne line, the code is 'released' and allowed to continue here
                if (!_pendingDisabledCooldownTimerLoop_DontDisableCooldown)
                {
                    isOnPendingDisabledCooldown = false;
                    LogVerbose("COOLDOWN ENDED: Subtitle-Disabled Cooldown", ConsoleColor.DarkYellow);
                    break;
                }
            }
        }

        public void StopPendingDisableCooldownNow()
        {
            isOnPendingDisabledCooldown = false;
            _pendingDisabledCooldownTimerLoop_DontDisableCooldown = false;
            _pendingDisabledCooldownResetEvent.Set(); // Wake up the sleeping thread
            LogDebug("Subtitle-Disabled Cooldown timer stopped.");
        }

        public void RestartPendingDisableCooldownTimer()
        {
            _pendingDisabledCooldownTimerLoop_DontDisableCooldown = true;
            _pendingDisabledCooldownResetEvent.Set(); // Wake up the sleeping thread
            LogDebug("Subtitle-Disabled Sleep timer reset.");
        }

        // ---------------------------------------------------------------------

        // Within about 10 seconds of the player starting a new session, it doesn't respond to subtitle commands correctly
        // So we need to wait a bit before allowing subtitles
        private void SimpleSessionStartTimer()
        {
            if (waitedInitialPeriod)
            {
                LogDebug("Already waited initial period for session start.");
                return;
            }

            if (Program.config.InitialSessionDelay == 0)
            {
                LogDebug("Initial session delay disabled in settings (value is 0), not starting.");
                waitedInitialPeriod = true;
                return;
            }

            int delay = Program.config.InitialSessionDelay * 1000; // Convert to milliseconds

            Task.Run(() =>
            {
                Thread.Sleep(delay); // Wait 10 seconds
                waitedInitialPeriod = true;
                LogDebug("Waited initial period for session start.");
            });
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            StopSubtitlesWithRetry(false);
        }

        public void MakeDead()
        {
            _isDead = true;
            // Don't stop subtitles here like we do in StopMonitoring() because it might be a new session,
            //    and the command would just go to the machine and might interfere with the new session
        }

        public void RestartMonitoring()
        {
            _isMonitoring = true;
        }

        public void ToggleMonitoring()
        {
            if (IsMonitoringAndNotDead)
            {
                StopMonitoring();
                LogDebug("Stopped monitoring.");
            }
            else
            {
                RestartMonitoring();
                LogDebug("Restarted monitoring.");
            }
        }
    }
}