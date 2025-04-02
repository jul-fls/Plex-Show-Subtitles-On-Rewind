using System.Xml;
using System.Xml.Serialization;

//#pragma warning disable IDE0074 // Use compound assignment
#pragma warning disable IDE0290 // Use primary constructor

namespace PlexShowSubtitlesOnRewind;

public class PlexMediaItem
{
    public PlexMediaItem(string key)
    {
        Key = key;
        Title = string.Empty;
        Type = string.Empty;
    }

    public string Key { get; set; }
    public string Title { get; set; }
    public string Type { get; set; }
    public List<Media> Media { get; set; } = [];

    public List<SubtitleStream> GetSubtitleStreams()
    {
        List<SubtitleStream> subtitles = [];

        foreach (Media media in Media)
        {
            foreach (MediaPart part in media.Parts)
            {
                subtitles.AddRange(part.Subtitles);
            }
        }

        return subtitles;
    }
}

[XmlRoot("Video")]
public class PlexSession
{
    // XML mapped properties
    [XmlAttribute("key")]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute("ratingKey")]
    public string RatingKey { get; set; } = string.Empty;

    [XmlAttribute("sessionKey")]
    public string SessionKey { get; set; } = string.Empty;

    [XmlAttribute("title")]
    public string Title { get; set; } = string.Empty;

    [XmlAttribute("grandparentTitle")]
    public string GrandparentTitle { get; set; } = string.Empty;

    [XmlAttribute("type")]
    public string Type { get; set; } = string.Empty;

    [XmlAttribute("viewOffset")]
    public int ViewOffset { get; set; }

    [XmlElement("Player")]
    public PlexPlayer Player { get; set; } = new();

    [XmlElement("Media")]
    public List<Media> Media { get; set; } = [];

    // The Session element is at the same level as Media, Player, etc.
    [XmlElement("Session")]
    public PlexInnerSession InnerSession { get; set; } = new();

    [XmlIgnore]
    public string PlaybackID => Player.PlaybackId;

    [XmlIgnore]
    public string SessionID => InnerSession.Id;

    [XmlIgnore]
    public string RawXml { get; set; } = string.Empty;

    [XmlIgnore]
    private PlexMediaItem? _cachedItem;  

    // Business logic methods
    public async Task<PlexMediaItem> FetchItemAsync(string key)
    {
        if (_cachedItem == null)
        {
            _cachedItem = await PlexServer.FetchItemAsync(key);
        }
        return _cachedItem;
    }

    // For the "Session" node within the "Video" node. Even though we're calling the "video" node the "session"
    public class PlexInnerSession
    {
        [XmlAttribute("id")]
        public string Id { get; set; } = string.Empty;

        [XmlAttribute("bandwidth")]
        public string Bandwidth { get; set; } = string.Empty;

        [XmlAttribute("location")]
        public string Location { get; set; } = string.Empty;
    }
}


[XmlRoot("Player")]
public class PlexPlayer
{
    [XmlAttribute("title")]
    public string Title { get; set; } = string.Empty;

    [XmlAttribute("machineIdentifier")]
    public string MachineIdentifier { get; set; } = string.Empty;

    [XmlAttribute("address")]
    public string Address { get; set; } = string.Empty;

    [XmlAttribute("device")]
    public string Device { get; set; } = string.Empty;

    [XmlAttribute("model")]
    public string Model { get; set; } = string.Empty;

    [XmlAttribute("platform")]
    public string Platform { get; set; } = string.Empty;

    [XmlAttribute("platformVersion")]
    public string PlatformVersion { get; set; } = string.Empty;

    [XmlAttribute("playbackId")]
    public string PlaybackId { get; set; } = string.Empty;

    [XmlAttribute("playbackSessionId")]
    public string PlaybackSessionId { get; set; } = string.Empty;

    [XmlAttribute("product")]
    public string Product { get; set; } = string.Empty;

    [XmlAttribute("profile")]
    public string Profile { get; set; } = string.Empty;

    [XmlAttribute("state")]
    public string State { get; set; } = string.Empty;

    [XmlAttribute("vendor")]
    public string Vendor { get; set; } = string.Empty;

    [XmlAttribute("version")]
    public string Version { get; set; } = string.Empty;

    [XmlAttribute("local")]
    public string Local { get; set; } = string.Empty;

    [XmlAttribute("relayed")]
    public string Relayed { get; set; } = string.Empty;

    [XmlAttribute("secure")]
    public string Secure { get; set; } = string.Empty;

    [XmlAttribute("userID")]
    public string UserID { get; set; } = string.Empty;

    // ------------------- Other properties that are not part of the XML mapping -------------------

    [XmlIgnore]
    public string Port { get; set; } = "32500"; // Assume this for now, but maybe we can get it from the XML for /resources if needed later

    [XmlIgnore]
    public string DirectUrlPath => $"http://{Address}:{Port}";
}

[XmlRoot("MediaContainer")]
public class TimelineMediaContainer
{
    [XmlElement("Timeline")]
    public List<PlexTimeline> Timeline { get; set; } = new(); // Added to hold timeline information
    [XmlAttribute("size")]
    public int Size { get; set; } = 0;
}

[XmlRoot("Timeline")]
public class PlexTimeline
{
    [XmlAttribute("containerKey")]
    public string ContainerKey { get; set; } = string.Empty;

    [XmlAttribute("state")]
    public string State { get; set; } = string.Empty;

    [XmlAttribute("repeat")]
    public string Repeat { get; set; } = string.Empty;

    [XmlAttribute("address")]
    public string Address { get; set; } = string.Empty;

    [XmlAttribute("duration")]
    public string Duration { get; set; } = string.Empty;

    [XmlAttribute("subtitleStreamID")]
    public string SubtitleStreamID { get; set; } = string.Empty;

    [XmlAttribute("key")]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute("playQueueVersion")]
    public string PlayQueueVersion { get; set; } = string.Empty;

    [XmlAttribute("time")]
    public string Time { get; set; } = string.Empty;

    [XmlAttribute("machineIdentifier")]
    public string MachineIdentifier { get; set; } = string.Empty;

    [XmlAttribute("type")]
    public string Type { get; set; } = string.Empty;

    [XmlAttribute("volume")]
    public string Volume { get; set; } = string.Empty;

    [XmlAttribute("controllable")]
    public string Controllable { get; set; } = string.Empty;

    [XmlAttribute("ratingKey")]
    public string RatingKey { get; set; } = string.Empty;

    [XmlAttribute("playQueueID")]
    public string PlayQueueID { get; set; } = string.Empty;

    [XmlAttribute("autoPlay")]
    public string AutoPlay { get; set; } = string.Empty;

    [XmlAttribute("seekRange")]
    public string SeekRange { get; set; } = string.Empty;

    [XmlAttribute("shuffle")]
    public string Shuffle { get; set; } = string.Empty;

    [XmlAttribute("playQueueItemID")]
    public string PlayQueueItemID { get; set; } = string.Empty;

    [XmlAttribute("port")]
    public string Port { get; set; } = string.Empty;

    [XmlAttribute("videoStreamID")]
    public string VideoStreamID { get; set; } = string.Empty;

    [XmlAttribute("providerIdentifier")]
    public string ProviderIdentifier { get; set; } = string.Empty;

    [XmlAttribute("guid")]
    public string Guid { get; set; } = string.Empty;

    [XmlAttribute("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [XmlAttribute("subtitlePosition")]
    public string SubtitlePosition { get; set; } = string.Empty;

    [XmlAttribute("audioStreamID")]
    public string AudioStreamID { get; set; } = string.Empty;
}

[XmlRoot("Media")]
public class Media
{
    [XmlAttribute("id")]
    public string Id { get; set; } = string.Empty;

    [XmlAttribute("duration")]
    public int Duration { get; set; }

    [XmlAttribute("videoCodec")]
    public string VideoCodec { get; set; } = string.Empty;

    [XmlAttribute("audioCodec")]
    public string AudioCodec { get; set; } = string.Empty;

    [XmlAttribute("container")]
    public string Container { get; set; } = string.Empty;

    [XmlElement("Part")]
    public List<MediaPart> Parts { get; set; } = [];
}

[XmlRoot("Part")]
public class MediaPart
{
    [XmlAttribute("id")]
    public string Id { get; set; } = string.Empty;

    [XmlAttribute("key")]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute("duration")]
    public int Duration { get; set; }

    [XmlAttribute("file")]
    public string File { get; set; } = string.Empty;

    [XmlElement("Stream")]
    public List<StreamData> AllStreams { get; set; } = [];

    // This exposes only subtitle streams as a computed property. They have stream type 3.
    [XmlIgnore]
    public List<SubtitleStream> Subtitles => AllStreams
        .Where(s => s.StreamType == 3) // Only subtitle streams
        .Select(s => new SubtitleStream
        {
            Id = s.Id,
            Index = s.Index,
            ExtendedDisplayTitle = s.ExtendedDisplayTitle,
            Language = s.Language,
            Selected = s.SelectedValue == "1",
            Format = s.Format,
            Title = s.Title,
            Location = s.Location,
            IsExternal = s.Location == "external"
        })
        .ToList();

    // Helper class for XML mapping
    public class StreamData
    {
        [XmlAttribute("id")]
        public int Id { get; set; }

        [XmlAttribute("streamType")]
        public int StreamType { get; set; }

        [XmlAttribute("index")]
        public int Index { get; set; }

        [XmlAttribute("extendedDisplayTitle")]
        public string ExtendedDisplayTitle { get; set; } = string.Empty;

        [XmlAttribute("language")]
        public string Language { get; set; } = string.Empty;

        [XmlAttribute("selected")]
        public string SelectedValue { get; set; } = string.Empty;

        [XmlAttribute("format")]
        public string Format { get; set; } = string.Empty;

        [XmlAttribute("title")]
        public string Title { get; set; } = string.Empty;

        [XmlAttribute("location")]
        public string Location { get; set; } = string.Empty;
    }
}

public class SubtitleStream
{
    public int Id { get; set; }
    public int Index { get; set; }
    public string ExtendedDisplayTitle { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool Selected { get; set; }
    public string Format { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsExternal { get; set; }
}

[XmlRoot("MediaContainer")]
public class MediaContainer
{
    [XmlElement("Video")]
    public List<PlexSession> Sessions { get; set; } = [];
}

// Class to hold session objects and associated subtitles
public class ActiveSession
{
    private PlexSession _session;
    private List<SubtitleStream> _availableSubtitles;
    private List<SubtitleStream> _activeSubtitles;

    public string DeviceName { get; }
    public string MachineID { get; }
    public string MediaTitle { get; } // MediaTitle is derived from GrandparentTitle or Title, whichever is available (not an empty string)
    public string SessionID { get; }
    public string RawXml { get; }
    public long? LastSeenTimeEpoch { get; set; } = null; // Used to decide when to remove from the active sessions list based on a grace period

    // ------------ Properties related to more accurate timeline data ------------
    // If we are sure subtitles are showing or not, it's true or false, otherwise null
    public bool? KnownIsShowingSubtitles {get; private set;} = null;
    // Whether we have the more accurate subtitle and view offset data. Can be used to determine minimum expected resolution of view offset
    public int? AccurateTime = null;
    public int SmallestResolutionExpected => AccurateTime != null ? MonitorManager.AccurateTimelineResolution : MonitorManager.DefaultSmallestResolution;
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

        GetAndApplyTimelineData(); // Initialize the known subtitle state and view offset if possible
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

    public bool HasActiveSubtitles()
    {
        if (KnownIsShowingSubtitles != null)
        {
            return KnownIsShowingSubtitles.Value; // If we know for sure, return that value
        }
        else
        {
            return ActiveSubtitles.Count > 0;
        }
    }

    public double GetPlayPositionSeconds()
    {
        int positionMilliseconds;

        if (AccurateTime != null)
            positionMilliseconds = AccurateTime.Value;
        else
            positionMilliseconds = Session.ViewOffset;

        double positionSec = Math.Round(positionMilliseconds / 1000.0, 2);
        return positionSec;
    }

    public void GetAndApplyTimelineData()
    {
        // Try getting the timeline container, which has more accuate info about current view time and subtitles
        TimelineMediaContainer? timelineContainer = PlexServer.GetTimelineAsync(MachineID, SessionID, DirectUrlPath).Result;

        // If we can't get the timeline container, we can't do any more here
        if (timelineContainer == null)
        {
            this.KnownIsShowingSubtitles = null;
            this.AccurateTime = null;
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
                AccurateTime = int.Parse(timeline.Time);
                this.Session.ViewOffset = AccurateTime.Value; // Update the view offset with the latest time from the timeline
            }

            // If we have the timeline info, we can know for sure if subtitles are showing
            if (timeline.SubtitleStreamID != null && timeline.SubtitleStreamID != "")
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
            this.AccurateTime = null;
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

    public async void EnableSubtitles()
    {
        if (AvailableSubtitles.Count > 0)
        {
            // Just use the first available subtitle stream for now
            SubtitleStream firstSubtitle = AvailableSubtitles[0];
            int subtitleID = firstSubtitle.Id;
            await PlexServer.SetSubtitleStreamAsync(machineID: MachineID, subtitleStreamID: subtitleID, activeSession:this);
        }
    }

    public async void DisableSubtitles()
    {
        await PlexServer.SetSubtitleStreamAsync(machineID: MachineID, subtitleStreamID: 0, activeSession:this);
    }

}

public class CommandResult(bool success, string responseErrorMessage, XmlDocument? responseXml)
{
    public bool Success { get; set; } = success;
    public string Message { get; set; } = responseErrorMessage;
    public XmlDocument? ResponseXml { get; set; } = responseXml;
}

public static class LaunchArgs
{
    public class Argument(string arg, string description)
    {
        public string Arg { get; } = arg;
        public string Description { get; } = description;
        public List<string> Variations { get; } = GetVariations(arg);

        // ------------------ Methods ------------------
        // Checks if the current argument matches any of the input arguments supplied in the parameter array
        public bool CheckIfMatchesInputArgs(string[] inputArgs)
        {
            return inputArgs.Any(a => this.Variations.Contains(a));
        }

        // ------------------ Implicit conversions ------------------
        public static implicit operator List<string>(Argument info) => info.Variations;
        public static implicit operator string(Argument info) => info.Arg;
        public override string ToString() => Arg; // Ensure argument string is returned properly when used in string interpolation
    }

    private static readonly Argument _background = new("background",    "Windows Only: The program runs in the background without showing a console.");
    private static readonly Argument _debug =      new("debug",         "Enables debug mode to show additional output.");
    private static readonly Argument _help =       new("help",          "Display help message with info including launch parameters.");
    private static readonly Argument _helpAlt =    new("?",             _help.Description);

    // -------------------------------------------------
    public static Argument Background => _background;
    public static Argument Debug => _debug;
    public static Argument Help => _help;
    public static Argument HelpAlt => _helpAlt;

    // --------------------------------------------------------
    // Get version starting with either hyphen or forward slash
    private static List<string> GetVariations(string arg)
    {
        List <string> variations = [];
        variations.Add("-" + arg);
        variations.Add("/" + arg);

        return variations;
    }
}