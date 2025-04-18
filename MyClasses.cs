using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Serialization;
using static RewindSubtitleDisplayerForPlex.Props;

namespace RewindSubtitleDisplayerForPlex;

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
    public List<PlexMedia> Media { get; set; } = [];

    public List<SubtitleStream> GetSubtitleStreams()
    {
        List<SubtitleStream> subtitles = [];

        foreach (PlexMedia media in Media)
        {
            foreach (MediaPart part in media.Parts)
            {
                subtitles.AddRange(part.Subtitles);
            }
        }

        return subtitles;
    }
}

// Class with all possible property name strings (not organized, just for all of them) just to make it easier to rename and for type safety
// Case sensitive, so use the same case as in the XML
public enum Props
{
    // Outer Types
    Player,
    MediaContainer,
    Timeline,
    Part,
    Response,
    Stream,
    Track,
    Episode,

    // Connecton Test
    code,
    message,
    status,

    // Session / Video
    Video,
    key,
    ratingKey,
    sessionKey,
    title,
    grandparentTitle,
    librarySectionTitle,
    type,
    viewOffset,
    Media,
    Session,

    // Plex Inner Session
    id,
    bandwidth,
    location,

    // Player
    PlayerTitle,
    machineIdentifier,
    address,
    device,
    model,
    platform,
    platformVersion,
    playbackId,
    playbackSessionId,
    product,
    profile,
    state,
    vendor,
    version,
    local,
    relayed,
    secure,
    userID,

    // Media Container
    size,

    // Timeline
    containerKey,
    //state
    repeat,
    //address
    duration,
    subtitleStreamID,
    //key
    playQueueVersion,
    time,
    //machineIdentifier
    //type
    volume,
    controllable,
    //ratingKey
    playQueueID,
    autoPlay,
    seekRange,
    shuffle,
    playQueueItemID,
    port,
    videoStreamID,
    providerIdentifier,
    guid,
    protocol,
    subtitlePosition,
    audioStreamID,

    // Media
    //id,
    //duration,
    videoCodec,
    audioCodec,
    container,
    //Part,

    // Part
    //id
    //key
    //duration
    file,
    //Stream

    // Stream (StreamData)
    //id
    streamType,
    index,
    extendedDisplayTitle,
    language,
    selected,
    format,
    //title
    //location

}

[XmlRoot(nameof(Video))]
public class PlexSession
{
    // XML mapped properties
    [XmlAttribute(nameof(key))]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute(nameof(ratingKey))]
    public string RatingKey { get; set; } = string.Empty;

    [XmlAttribute(nameof(sessionKey))]
    public string SessionKey { get; set; } = string.Empty;

    [XmlAttribute(nameof(title))]
    public string Title { get; set; } = string.Empty;

    [XmlAttribute(nameof(grandparentTitle))]
    public string GrandparentTitle { get; set; } = string.Empty;

    [XmlAttribute(nameof(librarySectionTitle))]
    public string LibrarySectionTitle { get; set; } = string.Empty;

    [XmlAttribute(nameof(type))]
    public string Type { get; set; } = string.Empty;

    [XmlAttribute(nameof(viewOffset))]
    public int ViewOffset { get; set; }

    [XmlElement(nameof(Player))]
    public PlexPlayer Player { get; set; } = new();

    [XmlElement(nameof(Media))]
    public List<PlexMedia> Media { get; set; } = [];

    // The Session element is at the same level as Media, Player, etc.
    [XmlElement(nameof(Session))]
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
        [XmlAttribute(nameof(id))]
        public string Id { get; set; } = string.Empty;

        [XmlAttribute(nameof(bandwidth))]
        public string Bandwidth { get; set; } = string.Empty;

        [XmlAttribute(nameof(location))]
        public string Location { get; set; } = string.Empty;
    }
}


[XmlRoot(nameof(Player))]
public class PlexPlayer
{
    [XmlAttribute(nameof(title))]
    public string Title { get; set; } = string.Empty;

    [XmlAttribute(nameof(machineIdentifier))]
    public string MachineIdentifier { get; set; } = string.Empty;

    [XmlAttribute(nameof(address))]
    public string Address { get; set; } = string.Empty;

    [XmlAttribute(nameof(device))]
    public string Device { get; set; } = string.Empty;

    [XmlAttribute(nameof(model))]
    public string Model { get; set; } = string.Empty;

    [XmlAttribute(nameof(platform))]
    public string Platform { get; set; } = string.Empty;

    [XmlAttribute(nameof(platformVersion))]
    public string PlatformVersion { get; set; } = string.Empty;

    [XmlAttribute(nameof(playbackId))]
    public string PlaybackId { get; set; } = string.Empty;

    [XmlAttribute(nameof(playbackSessionId))]
    public string PlaybackSessionId { get; set; } = string.Empty;

    [XmlAttribute(nameof(product))]
    public string Product { get; set; } = string.Empty;

    [XmlAttribute(nameof(profile))]
    public string Profile { get; set; } = string.Empty;

    [XmlAttribute(nameof(state))]
    public string State { get; set; } = string.Empty;

    [XmlAttribute(nameof(vendor))]
    public string Vendor { get; set; } = string.Empty;

    [XmlAttribute(nameof(version))]
    public string Version { get; set; } = string.Empty;

    [XmlAttribute(nameof(local))]
    public string Local { get; set; } = string.Empty;

    [XmlAttribute(nameof(relayed))]
    public string Relayed { get; set; } = string.Empty;

    [XmlAttribute(nameof(secure))]
    public string Secure { get; set; } = string.Empty;

    [XmlAttribute(nameof(userID))]
    public string UserID { get; set; } = string.Empty;

    // ------------------- Other properties that are not part of the XML mapping -------------------

    [XmlIgnore]
    public string Port { get; set; } = "32500"; // Assume this for now, but maybe we can get it from the XML for /resources if needed later

    [XmlIgnore]
    public string DirectUrlPath => $"http://{Address}:{Port}";
}

[XmlRoot(nameof(MediaContainer))]
public class TimelineMediaContainer
{
    [XmlElement(nameof(Timeline))]
    public List<PlexTimeline> Timeline { get; set; } = [];
    [XmlAttribute(nameof(size))]
    public int Size { get; set; } = 0;
}

[XmlRoot(nameof(Timeline))]
public class PlexTimeline
{
    [XmlAttribute(nameof(containerKey))]
    public string ContainerKey { get; set; } = string.Empty;

    [XmlAttribute(nameof(state))]
    public string State { get; set; } = string.Empty;

    [XmlAttribute(nameof(repeat))]
    public string Repeat { get; set; } = string.Empty;

    [XmlAttribute(nameof(address))]
    public string Address { get; set; } = string.Empty;

    [XmlAttribute(nameof(duration))]
    public string Duration { get; set; } = string.Empty;

    [XmlAttribute(nameof(subtitleStreamID))]
    public string SubtitleStreamID { get; set; } = string.Empty;

    [XmlAttribute(nameof(key))]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute(nameof(playQueueVersion))]
    public string PlayQueueVersion { get; set; } = string.Empty;

    [XmlAttribute(nameof(time))]
    public string Time { get; set; } = string.Empty;

    [XmlAttribute(nameof(machineIdentifier))]
    public string MachineIdentifier { get; set; } = string.Empty;

    [XmlAttribute(nameof(type))]
    public string Type { get; set; } = string.Empty;

    [XmlAttribute(nameof(volume))]
    public string Volume { get; set; } = string.Empty;

    [XmlAttribute(nameof(controllable))]
    public string Controllable { get; set; } = string.Empty;

    [XmlAttribute(nameof(ratingKey))]
    public string RatingKey { get; set; } = string.Empty;

    [XmlAttribute(nameof(playQueueID))]
    public string PlayQueueID { get; set; } = string.Empty;

    [XmlAttribute(nameof(autoPlay))]
    public string AutoPlay { get; set; } = string.Empty;

    [XmlAttribute(nameof(seekRange))]
    public string SeekRange { get; set; } = string.Empty;

    [XmlAttribute(nameof(shuffle))]
    public string Shuffle { get; set; } = string.Empty;

    [XmlAttribute(nameof(playQueueItemID))]
    public string PlayQueueItemID { get; set; } = string.Empty;

    [XmlAttribute(nameof(port))]
    public string Port { get; set; } = string.Empty;

    [XmlAttribute(nameof(videoStreamID))]
    public string VideoStreamID { get; set; } = string.Empty;

    [XmlAttribute(nameof(providerIdentifier))]
    public string ProviderIdentifier { get; set; } = string.Empty;

    [XmlAttribute(nameof(guid))]
    public string Guid { get; set; } = string.Empty;

    [XmlAttribute(nameof(protocol))]
    public string Protocol { get; set; } = string.Empty;

    [XmlAttribute(nameof(subtitlePosition))]
    public string SubtitlePosition { get; set; } = string.Empty;

    [XmlAttribute(nameof(audioStreamID))]
    public string AudioStreamID { get; set; } = string.Empty;
}

[XmlRoot(nameof(Media))]
public class PlexMedia
{
    [XmlAttribute(nameof(id))]
    public string Id { get; set; } = string.Empty;

    [XmlAttribute(nameof(duration))]
    public int Duration { get; set; }

    [XmlAttribute(nameof(videoCodec))]
    public string VideoCodec { get; set; } = string.Empty;

    [XmlAttribute(nameof(audioCodec))]
    public string AudioCodec { get; set; } = string.Empty;

    [XmlAttribute(nameof(container))]
    public string Container { get; set; } = string.Empty;

    [XmlElement(nameof(Part))]
    public List<MediaPart> Parts { get; set; } = [];
}

[XmlRoot(nameof(Part))]
public class MediaPart
{
    [XmlAttribute(nameof(id))]
    public string Id { get; set; } = string.Empty;

    [XmlAttribute(nameof(key))]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute(nameof(duration))]
    public int Duration { get; set; }

    [XmlAttribute(nameof(file))]
    public string File { get; set; } = string.Empty;

    [XmlElement(nameof(Props.Stream))]
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
            IsExternal = CheckIsExternal(s.Location, s.ExtendedDisplayTitle)
        })
        .ToList();

    // Helper class for XML mapping
    public class StreamData
    {
        [XmlAttribute(nameof(id))]
        public int Id { get; set; }

        [XmlAttribute(nameof(streamType))]
        public int StreamType { get; set; }

        [XmlAttribute(nameof(index))]
        public int Index { get; set; }

        [XmlAttribute(nameof(extendedDisplayTitle))]
        public string ExtendedDisplayTitle { get; set; } = string.Empty;

        [XmlAttribute(nameof(language))]
        public string Language { get; set; } = string.Empty;

        [XmlAttribute(nameof(selected))]
        public string SelectedValue { get; set; } = string.Empty;

        [XmlAttribute(nameof(format))]
        public string Format { get; set; } = string.Empty;

        [XmlAttribute(nameof(title))]
        public string Title { get; set; } = string.Empty;

        [XmlAttribute(nameof(location))]
        public string Location { get; set; } = string.Empty;
    }

    private static bool CheckIsExternal(string location, string extendedDisplayName)
    {
        if (location == "external")
        {
            return true;
        }
        else if (string.IsNullOrEmpty(location) && extendedDisplayName.Contains("external", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        else
        {
            return false;
        }
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

[XmlRoot(nameof(MediaContainer))]
public class SessionMediaContainer
{
    [XmlElement(nameof(Video))]
    public List<PlexSession> Sessions { get; set; } = [];
}

public class PlexResource
{
    [JsonPropertyName("name")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("product")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("productVersion")]
    public string ProductVersion { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("platformVersion")]
    public string PlatformVersion { get; set; } = string.Empty;

    [JsonPropertyName("device")]
    public string Device { get; set; } = string.Empty;

    [JsonPropertyName("clientIdentifier")]
    public string ClientIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("lastSeenAt")]
    public string LastSeenAt { get; set; } = string.Empty;

    [JsonPropertyName("provides")]
    public string Provides { get; set; } = string.Empty;

    [JsonPropertyName("ownerId")]
    public string? OwnerId { get; set; }

    [JsonPropertyName("sourceTitle")]
    public string? SourceTitle { get; set; }

    [JsonPropertyName("publicAddress")]
    public string PublicAddress { get; set; } = string.Empty;

    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("owned")]
    public bool Owned { get; set; }

    [JsonPropertyName("home")]
    public bool Home { get; set; }

    [JsonPropertyName("synced")]
    public bool Synced { get; set; }

    [JsonPropertyName("relay")]
    public bool Relay { get; set; }

    [JsonPropertyName("presence")]
    public bool Presence { get; set; }

    [JsonPropertyName("httpsRequired")]
    public bool HttpsRequired { get; set; }

    [JsonPropertyName("publicAddressMatches")]
    public bool PublicAddressMatches { get; set; }

    [JsonPropertyName("dnsRebindingProtection")]
    public bool DnsRebindingProtection { get; set; }

    [JsonPropertyName("connections")]
    public List<PlexConnection> Connections { get; set; } = [];

    public class PlexConnection
    {
        [JsonPropertyName("protocol")]
        public string Protocol { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("uri")]
        public string Uri { get; set; } = string.Empty;

        [JsonPropertyName("local")]
        public bool Local { get; set; }

        [JsonPropertyName("relay")]
        public bool Relay { get; set; }

        [JsonPropertyName("IPv6")]
        public bool IPv6 { get; set; }
    }
}

// Need to have source generator for the PlexResource class
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<PlexResource>))]
internal partial class PlexResourceJsonContext : JsonSerializerContext
{
}

public class CommandResult(bool success, string responseErrorMessage, XmlDocument? responseXml)
{
    public bool Success { get; set; } = success;
    public string Message { get; set; } = responseErrorMessage;
    public XmlDocument? ResponseXml { get; set; } = responseXml;
}
