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

public class PlexSession
{
    public string Key { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RatingKey { get; set; } = string.Empty;
    public string SessionKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string GrandparentTitle { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // movie, episode, etc.
    public int ViewOffset { get; set; } // in milliseconds
    public PlexPlayer Player { get; set; }
    public List<Media> Media { get; set; } = [];
    private PlexMediaItem? _cachedItem;
    public required string RawXml { get; set; }

    public async Task<PlexMediaItem> FetchItemAsync(string key, PlexServer server)
    {
        if (_cachedItem == null)
        {
            _cachedItem = await server.FetchItemAsync(key);
        }
        return _cachedItem;
    }
}

public class PlexPlayer
{
    public string Title { get; set; } = string.Empty;
    public string MachineIdentifier { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string PlatformVersion { get; set; } = string.Empty;
    public string PlaybackId { get; set; } = string.Empty;
    public string PlaybackSessionId { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Local { get; set; } = string.Empty;
    public string Relayed { get; set; } = string.Empty;
    public string Secure { get; set; } = string.Empty;
    public string UserID { get; set; } = string.Empty;
}

public class PlexClient(string deviceName, string machineIdentifier, string clientAppName, string deviceClass, string platform, HttpClient httpClient, string baseUrl, PlexServer plexServer, string rawXml)
{
    public string DeviceName { get; private set; } = deviceName;
    public string MachineIdentifier { get; private set; } = machineIdentifier;
    public string ClientAppName { get; private set; } = clientAppName;
    public string DeviceClass { get; private set; } = deviceClass;
    public string Platform { get; private set; } = platform;
    public HttpClient HttpClient { get; private set; } = httpClient;
    public string BaseUrl { get; private set; } = baseUrl;
    public PlexServer PlexServer { get; private set; } = plexServer;
    public string RawXml { get; private set; } = rawXml;
}

public class Media
{
    public string Id { get; set; } = string.Empty;
    public int Duration { get; set; }
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public List<MediaPart> Parts { get; set; } = [];
}

public class MediaPart
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int Duration { get; set; }
    public string File { get; set; } = string.Empty;
    public List<SubtitleStream> Subtitles { get; set; } = [];
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

// ----------------- XML Classes -----------------

// XML-specific versions of your model classes
[XmlRoot("Video")]
public class PlexSessionXml
{
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
    public PlexPlayerXml Player { get; set; } = new();

    [XmlElement("Media")]
    public List<MediaXml> Media { get; set; } = [];

    [XmlElement("Session")]
    public PlexInnerSessionXml Session { get; set; } = new();

    public string RawXml { get; set; } = string.Empty;

    // Convert to your existing PlexSession class
    public PlexSession ToPlexSession()
    {
        PlexSession session = new PlexSession
        {
            Key = Key,
            RatingKey = RatingKey,
            SessionKey = SessionKey,
            Title = Title,
            GrandparentTitle = GrandparentTitle,
            Type = Type,
            ViewOffset = ViewOffset,
            SessionId = Session.Id,
            RawXml = RawXml,
            Player = new PlexPlayer() // Initialize Player property
        };

        if (Player != null)
        {
            session.Player.Title = Player.Title;
            session.Player.MachineIdentifier = Player.MachineIdentifier;
            session.Player.Address = Player.Address;
            session.Player.Device = Player.Device;
            session.Player.Model = Player.Model;
            session.Player.Platform = Player.Platform;
            session.Player.PlatformVersion = Player.PlatformVersion;
            session.Player.PlaybackId = Player.PlaybackId;
            session.Player.PlaybackSessionId = Player.PlaybackSessionId;
            session.Player.Product = Player.Product;
            session.Player.Profile = Player.Profile;
            session.Player.State = Player.State;
            session.Player.Vendor = Player.Vendor;
            session.Player.Version = Player.Version;
            session.Player.Local = Player.Local;
            session.Player.Relayed = Player.Relayed;
            session.Player.Secure = Player.Secure;
            session.Player.UserID = Player.UserID;
        }

        if (Media != null)
        {
            foreach (MediaXml mediaXml in Media)
            {
                session.Media.Add(mediaXml.ToMedia());
            }
        }

        return session;
    }
}

[XmlRoot("Player")]
public class PlexPlayerXml
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

    public string RawXml { get; set; }
}

[XmlRoot("Session")]
public class PlexInnerSessionXml
{
    [XmlAttribute("id")]
    public string Id { get; set; } = string.Empty;

    [XmlAttribute("bandwidth")]
    public string Bandwidth { get; set; } = string.Empty;

    [XmlAttribute("location")]
    public string Location { get; set; } = string.Empty;

    public string RawXml { get; set; }
}

[XmlRoot("Server")]
public class PlexClientXml
{
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

    public required string RawXml { get; set; }

    // Convert to your existing PlexClient class
    public PlexClient ToPlexClient(HttpClient httpClient, string baseUrl, PlexServer plexServer, string rawXml)
    {
        PlexClient newClient = new PlexClient
        (
            deviceName: DeviceName,
            machineIdentifier: MachineIdentifier,
            clientAppName: ClientAppName,
            deviceClass: DeviceClass,
            platform: Platform ?? ClientAppName, // Handle the fallback
            httpClient: httpClient,
            baseUrl: baseUrl,
            plexServer: plexServer,
            rawXml: rawXml
        );

        return newClient;
    }
}

[XmlRoot("Media")]
public class MediaXml
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
    public List<MediaPartXml> Parts { get; set; } = [];

    public string RawXml { get; set; }

    // Convert to your existing Media class
    public Media ToMedia()
    {
        Media media = new Media
        {
            Id = Id,
            Duration = Duration,
            VideoCodec = VideoCodec,
            AudioCodec = AudioCodec,
            Container = Container
        };

        if (Parts != null)
        {
            foreach (MediaPartXml partXml in Parts)
            {
                media.Parts.Add(partXml.ToMediaPart());
            }
        }

        return media;
    }
}

[XmlRoot("Part")]
public class MediaPartXml
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
    public List<SubtitleStreamXml> SubtitlesXml { get; set; } = [];

    public string RawXml { get; set; }

    // Convert to your existing MediaPart class
    public MediaPart ToMediaPart()
    {
        MediaPart part = new MediaPart
        {
            Id = Id,
            Key = Key,
            Duration = Duration,
            File = File
        };

        if (SubtitlesXml != null)
        {
            foreach (SubtitleStreamXml subtitleXml in SubtitlesXml)
            {
                if (subtitleXml.StreamType == 3) // Only add subtitle streams
                {
                    part.Subtitles.Add(subtitleXml.ToSubtitleStream());
                }
            }
        }

        return part;
    }
}

[XmlRoot("Stream")]
public class SubtitleStreamXml
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

    public string RawXml { get; set; }

    // Convert to your existing SubtitleStream class
    public SubtitleStream ToSubtitleStream()
    {
        return new SubtitleStream
        {
            Id = Id,
            Index = Index,
            ExtendedDisplayTitle = ExtendedDisplayTitle,
            Language = Language,
            Selected = SelectedValue == "1",
            Format = Format,
            Title = Title,
            Location = Location,
            IsExternal = Location == "external"
        };
    }
}

[XmlRoot("MediaContainer")]
public class MediaContainerXml
{
    [XmlElement("Video")]
    public List<PlexSessionXml> Sessions { get; set; } = [];

    [XmlElement("Server")]
    public List<PlexClientXml> Clients { get; set; } = [];

    public string RawXml { get; set; }
}
