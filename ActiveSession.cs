namespace RewindSubtitleDisplayerForPlex;

// Class to hold session objects and associated subtitles
public class ActiveSession
{
    private PlexSession _session;
    private List<SubtitleStream> _availableSubtitles;
    private List<SubtitleStream> _activeSubtitles;
    private readonly SubtitlesHotkeyMonitor _hotkeyMonitor;

    public string DeviceName { get; }
    public string MachineID { get; }
    public string MediaTitle { get; } // MediaTitle is derived from GrandparentTitle or Title, whichever is available (not an empty string)
    public string SessionID { get; }
    public string RawXml { get; }
    public SubtitleStream? PreferredSubtitle { get; private set; }

    // Settable properties
    public long? LastSeenTimeEpoch { get; set; } = null; // Used to decide when to remove from the active sessions list based on a grace period
    public bool ContainsInheritedMonitor { get; set; } = false; // Whether this session already has been inherited from another session to avoid duplicate inheritance

    // ------------ Properties related to more accurate timeline data ------------
    // If we are sure subtitles are showing or not, it's true or false, otherwise null
    public bool? KnownIsShowingSubtitles {get; private set;} = null;
    // Whether we have the more accurate subtitle and view offset data. Can be used to determine minimum expected resolution of view offset
    public int? AccurateTimeMs = null;
    // If we have an accurate time at all, then we know we are using the accurate timeline data, so use the accurate resolution setting
    public int SmallestResolutionExpected => AccurateTimeMs != null ? MonitorManager.AccurateTimelineResolution : MonitorManager.DefaultSmallestResolution;
    //-------------------------------------------------------------------------------

    public ActiveSession(PlexSession session, List<SubtitleStream> availableSubtitles, List<SubtitleStream> activeSubtitles)
    {
        _session = session;
        _availableSubtitles = availableSubtitles;
        _activeSubtitles = activeSubtitles;
        DeviceName = session.Player.Title;
        MachineID = session.Player.MachineIdentifier;
        MediaTitle = !string.IsNullOrEmpty(session.GrandparentTitle)
        ? session.GrandparentTitle
        : !string.IsNullOrEmpty(session.Title) ? session.Title : string.Empty;
        SessionID = session.PlaybackID;
        RawXml = session.RawXml;
        PreferredSubtitle = GetPreferredSubtitle_BasedOnSettings(availableSubtitles);

        GetAndApplyTimelineData(); // Initialize the known subtitle state and view offset if possible
        _hotkeyMonitor = new SubtitlesHotkeyMonitor(playbackID: PlaybackID, machineID: MachineID, this); // Initialize the hotkey monitor
    }

    // Expressions to access inner properties of the session and player objects more conveniently
    public string DirectUrlPath => _session.Player.DirectUrlPath;
    public string PlaybackID => _session.Player.PlaybackId; // Changes when changing episodes, etc

    // Properly implemented public properties that use the private fields
    public PlexSession Session
    {
        get => _session;
        private set => _session = value;
    }

    public List<SubtitleStream> AvailableSubtitles
    {
        get => _availableSubtitles;
        private set => _availableSubtitles = value;
    }

    public List<SubtitleStream> ActiveSubtitles
    {
        get => _activeSubtitles;
        private set => _activeSubtitles = value;
    }

    // ------------------ Methods ------------------

    public double GetPlayPositionSeconds()
    {
        int positionMilliseconds;

        if (AccurateTimeMs != null)
            positionMilliseconds = AccurateTimeMs.Value;
        else
            positionMilliseconds = Session.ViewOffset;

        double positionSec = Math.Round(positionMilliseconds / 1000.0, 2);
        return positionSec;
    }

    public int GetPlayPositionMilliseconds()
    {
        int positionMilliseconds;
        if (AccurateTimeMs != null)
            positionMilliseconds = AccurateTimeMs.Value;
        else
            positionMilliseconds = Session.ViewOffset;
        return positionMilliseconds;
    }

    public void GetAndApplyTimelineData()
    {
        // Try getting the timeline container, which has more accuate info about current view time and subtitles
        TimelineMediaContainer? timelineContainer = PlexServer.GetTimelineAsync(machineID: MachineID, deviceName: DeviceName, url: DirectUrlPath).Result;

        // If we can't get the timeline container, we can't do any more here
        if (timelineContainer == null)
        {
            this.KnownIsShowingSubtitles = null;
            this.AccurateTimeMs = null;
            return;
        }

        List<PlexTimeline> timelineList = timelineContainer.Timeline;

        // We need the specific timeline for this session, which is identified by the MachineID
        // We can check in a lot of ways, but we'll just check for a non-empty time attribute
        //    (Our program puts empty strings if the attribute wasn't found, so we'll check for not-empty strings instead)
        // It seems the timeline container usually has 3 items - music, photo, and video. We usually want the video one,
        //    the others usually only have attributes for 'state' (stopped) and 'type'
        PlexTimeline? timeline = timelineList.FirstOrDefault(t => t.Time != ""); 

        if (timeline != null)
        {
            if (timeline.Time != null && timeline.Time != "")
            {
                AccurateTimeMs = int.Parse(timeline.Time);
                this.Session.ViewOffset = AccurateTimeMs.Value; // Update the view offset with the latest time from the timeline
            }

            // If we have the timeline info, we can know for sure if subtitles are showing
            if (timeline.SubtitleStreamID != null && timeline.SubtitleStreamID != "" && timeline.SubtitleStreamID != "0") // Might be null or 0 depending on the player to represent not showing subs
            {
                this.KnownIsShowingSubtitles = true; // If we have a subtitle stream ID, we know subtitles are showing
            }
            else
            {
                this.KnownIsShowingSubtitles = false;
            }
        }
        else
        {
            this.KnownIsShowingSubtitles = null;
            this.AccurateTimeMs = null;
        }
    }

    public ActiveSession ApplyUpdatedData(PlexSession session, List<SubtitleStream> activeSubtitles)
    {
        Session = session;
        AvailableSubtitles = _availableSubtitles; // Don't bother updating available subtitles
        ActiveSubtitles = activeSubtitles; // Don't bother updating active subtitles
        LastSeenTimeEpoch = null; // Reset the missing time since we have new data

        GetAndApplyTimelineData(); // Update the view offset and known subtitle state

        return this;
    }

    public void UpdateAccurateViewOffsetFromNotification(double? newViewOffset)
    {
        if (newViewOffset != null)
        {
            Session.ViewOffset = (int)newViewOffset; // Update the view offset with the latest time from the timeline
            AccurateTimeMs = (int)newViewOffset; // Update the accurate time as well
        }
        else
        {
            LogWarning("New view offset from Playing notification is null. Cannot update.");
        }
    }

    // Returns true for success, false for failure, null for no subtitles available
    public async Task<bool?> EnableSubtitles()
    {
        if (AvailableSubtitles.Count > 0)
        {
            int subtitleID;

            if (PreferredSubtitle != null) // Shouldn't be null if there are any available subtitles because the other function handles the default case, but just in case
            {
                // If we have a preferred subtitle, use that
                subtitleID = PreferredSubtitle.Id;
            }
            else
            {
                // Otherwise, just use the first available subtitle stream
                subtitleID = AvailableSubtitles[0].Id;
            }

            CommandResult result1 = await PlexServer.SetSubtitleStreamAsync(machineID: MachineID, sendDirectToDevice: Program.config.SendCommandDirectToDevice, subtitleStreamID: subtitleID, activeSession:this);

            if (result1.Success)
            {
                // There is a delay from the server even after it accepts the command
                // So we'll set to null because we can't be sure it's true yet. It will be updated in the next timeline update
                KnownIsShowingSubtitles = null;
                return true;
            }
            else
            {
                // Log the error message
                LogWarning($"Failed to enable subtitles: {result1.Message}");
                return false;
            }
        }
        else
        {
            LogWarning("No available subtitles to enable.");
            return null; // Use null to indicate no subtitles available
        }
    }

    public async Task<bool> DisableSubtitles()
    {
        CommandResult commandResult = await PlexServer.SetSubtitleStreamAsync(machineID: MachineID, sendDirectToDevice:Program.config.SendCommandDirectToDevice, subtitleStreamID: 0, activeSession:this);

        if (commandResult.Success)
        {
            // There is a delay from the server even after it accepts the command
            // So we'll set to null because we can't be sure it's false yet. It will be updated in the next timeline update
            KnownIsShowingSubtitles = null; 
            return true;
        }
        else
        {
            // Log the error message
            LogWarning($"Failed to disable subtitles: {commandResult.Message}");
            return false;
        }
    }

    public static SubtitleStream? GetPreferredSubtitle_BasedOnSettings(List<SubtitleStream> availableSubtitles)
    {
        // Check if the user has a preferred subtitle stream
        SubtitleStream? preferredSubtitle = null;
        List<string> preferredSubtitlePatterns = Program.config.SubtitlePreferencePatterns.Value;
        List<string> positivePatterns = [];
        List<string> negativePatterns = [];
        List<SubtitleStream> subtitleTrackCandidates = [];

        if (availableSubtitles.Count > 0)
        {
            // Apply the user's preferred subtitle patterns if any
            if (preferredSubtitlePatterns.Count > 0)
            {
                foreach (string pattern in preferredSubtitlePatterns)
                {
                    if (pattern.StartsWith('-'))
                        negativePatterns.Add(pattern.Substring(1));
                    else
                        positivePatterns.Add(pattern);
                }

                // Now we can check the available subtitles against the positive and negative patterns. ALL patterns must be satisfied
                foreach (SubtitleStream subtitle in availableSubtitles)
                {
                    // Check if the subtitle matches any of the positive patterns
                    bool matchesAllPositives = positivePatterns.All(pattern => subtitle.ExtendedDisplayTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                    // Check to name sure none of the negative patterns match
                    bool matchesAnyNegatives = negativePatterns.Any(pattern => subtitle.ExtendedDisplayTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase));

                    // If it matches all positive patterns and none of the negative patterns, we have a match
                    if (matchesAllPositives && !matchesAnyNegatives)
                    {
                        //preferredSubtitle = subtitle;
                        subtitleTrackCandidates.Add(subtitle);
                    }
                }
            }

            // if there's only one candidate, we can just use that
            if (subtitleTrackCandidates.Count == 1)
            {
                preferredSubtitle = subtitleTrackCandidates[0];
            }
            // If we have multiple candidates
            else if (subtitleTrackCandidates.Count > 1)
            {
                bool preferExternal = Program.config.PreferExternalSubtitles.Value;

                // If external is preferred, add those to a list and get the first one
                if (preferExternal)
                {
                    List<SubtitleStream> externalSubtitles = subtitleTrackCandidates.Where(s => s.IsExternal).ToList();

                    if (externalSubtitles.Count > 0)
                        preferredSubtitle = externalSubtitles[0];
                    else
                        preferredSubtitle = subtitleTrackCandidates[0]; // If no external subtitles were found, just get the first one
                }
                else // If no preference, get the first one
                {
                    preferredSubtitle = subtitleTrackCandidates[0];
                }
            }
            // If there are no preferred matching subtitles, we'll pull from the rest. First check if external is preferred
            else
            {
                // If external is preferred, add those to a list and get the first one
                if (Program.config.PreferExternalSubtitles.Value == true)
                {
                    List<SubtitleStream> externalSubtitles = availableSubtitles.Where(s => s.IsExternal).ToList();
                    if (externalSubtitles.Count > 0)
                        preferredSubtitle = externalSubtitles[0];
                    else
                        preferredSubtitle = availableSubtitles[0]; // If no external subtitles were found, just get the first one
                }
                else // If no preference, get the first one
                {
                    preferredSubtitle = availableSubtitles[0];
                }
            }
        }

        // This will be null if none were found
        return preferredSubtitle;
    }
}
