using System.Xml;
using System.Xml.Serialization;

#pragma warning disable IDE0074 // Use compound assignment
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
    public string SessionId => InnerSession.Id;

    [XmlIgnore]
    public string RawXml { get; set; } = string.Empty;

    [XmlIgnore]
    private PlexMediaItem? _cachedItem;

    // Business logic methods
    public async Task<PlexMediaItem> FetchItemAsync(string key, PlexServer server)
    {
        if (_cachedItem == null)
        {
            _cachedItem = await server.FetchItemAsync(key);
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

    // Other attributes...
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
}

[XmlRoot("Server")]
public class PlexClient
{
    // XML Properties
    [XmlAttribute("name")]
    public string DeviceName { get; set; } = string.Empty;

    [XmlAttribute("machineIdentifier")]
    public string MachineIdentifier { get; set; } = string.Empty;

    [XmlAttribute("product")]
    public string ClientAppName { get; set; } = string.Empty;

    [XmlAttribute("deviceClass")]
    public string DeviceClass { get; set; } = string.Empty;

    [XmlAttribute("platform")]
    public string Platform { get; set; } = string.Empty;

    // Non-XML properties for business logic
    [XmlIgnore]
    public HttpClient HttpClient { get; set; }

    [XmlIgnore]
    public string BaseUrl { get; set; } = string.Empty;

    [XmlIgnore]
    public PlexServer PlexServer { get; set; }

    [XmlIgnore]
    public string RawXml { get; set; } = string.Empty;

    // Default constructor for XML deserialization
    public PlexClient() { }

    // Call this after deserialization to complete initialization
    public void Initialize(HttpClient httpClient, string baseUrl, PlexServer plexServer, string rawXml)
    {
        HttpClient = httpClient;
        BaseUrl = baseUrl;
        PlexServer = plexServer;
        RawXml = rawXml;

        // Handle fallbacks
        if (string.IsNullOrEmpty(Platform))
        {
            Platform = ClientAppName;
        }
    }
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

    [XmlElement("Server")]
    public List<PlexClient> Clients { get; set; } = [];
}

// Class to hold session objects and associated subtitles
public class ActiveSession(PlexSession session, List<SubtitleStream> availableSubtitles, List<SubtitleStream> activeSubtitles, PlexServer plexServer)
{
    private PlexSession _session = session;
    private List<SubtitleStream> _availableSubtitles = availableSubtitles;
    private List<SubtitleStream> _activeSubtitles = activeSubtitles;
    private PlexServer _plexServer = plexServer;

    public string DeviceName { get; } = session.Player.Title;
    public string MachineID { get; } = session.Player.MachineIdentifier;
    public string MediaTitle { get; } = session.GrandparentTitle ?? session.Title;
    public string SessionID { get; } = session.SessionId;
    public string RawXml { get; } = session.RawXml;

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
        //Session.Reload(); // Otherwise it won't update
        int positionMilliseconds = Session.ViewOffset;
        double positionSec = Math.Round(positionMilliseconds / 1000.0, 2);
        return positionSec;
    }

    public ActiveSession Refresh(PlexSession session, List<SubtitleStream> activeSubtitles)
    {
        Session = session;
        AvailableSubtitles = _availableSubtitles; // Don't bother updating available subtitles
        ActiveSubtitles = activeSubtitles; // Don't bother updating active subtitles
        return this;
    }

    public async void EnableSubtitles()
    {
        if (AvailableSubtitles.Count > 0)
        {
            // Just use the first available subtitle stream for now
            SubtitleStream firstSubtitle = AvailableSubtitles[0];
            int subtitleID = firstSubtitle.Id;
            await plexServer.SetSubtitleStreamAsync(machineID: MachineID, subtitleStreamID: subtitleID);
        }
    }

    public async void DisableSubtitles()
    {
        await plexServer.SetSubtitleStreamAsync(machineID: MachineID, subtitleStreamID: 0);
    }

}

public class CommandResult(bool success, string responseErrorMessage, XmlDocument? responseXml)
{
    public bool Success { get; set; } = success;
    public string Message { get; set; } = responseErrorMessage;
    public XmlDocument? ResponseXml { get; set; } = responseXml;
}