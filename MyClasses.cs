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
    public string Key { get; set; }
    public string SessionId { get; set; }
    public string RatingKey { get; set; }
    public string SessionKey { get; set; }
    public string Title { get; set; }
    public string GrandparentTitle { get; set; }
    public string Type { get; set; } // movie, episode, etc.
    public int ViewOffset { get; set; } // in milliseconds
    public PlexPlayer Player { get; set; }
    public List<Media> Media { get; set; } = [];
    private PlexMediaItem _cachedItem;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0028:Simplify collection initialization", Justification = "Simplifying obscures Media type")]
    public PlexSession()
    {
        Player = new PlexPlayer();
        Media = new List<Media>();
    }

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
    public string Title { get; set; }
    public string MachineIdentifier { get; set; }
}

public class PlexClient(string deviceName, string machineIdentifier, string clientAppName, string deviceClass, string platform, HttpClient httpClient, string baseUrl, PlexServer plexServer)
{
    public string DeviceName { get; private set; } = deviceName;
    public string MachineIdentifier { get; private set; } = machineIdentifier;
    public string ClientAppName { get; private set; } = clientAppName;
    public string DeviceClass { get; private set; } = deviceClass;
    public string Platform { get; private set; } = platform;
    public HttpClient HttpClient { get; private set; } = httpClient;
    public string BaseUrl { get; private set; } = baseUrl;
    public PlexServer PlexServer { get; private set; } = plexServer;

    /// <summary>
    /// Select multiple playback streams at once.
    /// </summary>
    /// <param name="audioStreamID">ID of the audio stream from the media object</param>
    /// <param name="subtitleStreamID">ID of the subtitle stream from the media object</param>
    /// <param name="videoStreamID">ID of the video stream from the media object</param>
    /// <param name="mediaType">Media type to take action against (default: video)</param>
    /// <param name="server">The PlexServer instance to send the command through</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task<CommandResult> SetStreamsAsync(
        PlexServer server,
        int? audioStreamID = null,
        int? subtitleStreamID = null,
        int? videoStreamID = null,
        string mediaType = "video")
    {
        // Create dictionary for additional parameters
        Dictionary<string, string> parameters = [];

        // Add parameters only if they're not null
        if (audioStreamID.HasValue)
            parameters["audioStreamID"] = audioStreamID.Value.ToString();

        if (subtitleStreamID.HasValue)
            parameters["subtitleStreamID"] = subtitleStreamID.Value.ToString();

        if (videoStreamID.HasValue)
            parameters["videoStreamID"] = videoStreamID.Value.ToString();

        if (!string.IsNullOrEmpty(mediaType))
            parameters["type"] = mediaType;

        // Send the command through the PlexServer
        return await server.SendCommandAsync(this, "playback/setStreams", additionalParams: parameters);
    }

    /// <summary>
    /// Select the subtitle stream for the current playback item (only video).
    /// </summary>
    /// <param name="subtitleStreamID">ID of the subtitle stream from the media object</param>
    /// <param name="mediaType">Media type to take action against (default: video)</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task<CommandResult> SetSubtitleStreamAsync(
        int subtitleStreamID,
        string mediaType = "video")
    {
        // Simply call the SetStreamsAsync method with only the subtitle stream ID parameter
        return await SetStreamsAsync(
            server: PlexServer,
            subtitleStreamID: subtitleStreamID,
            mediaType: mediaType);
    }
}

public class Media
{
    public string Id { get; set; }
    public int Duration { get; set; }
    public string VideoCodec { get; set; }
    public string AudioCodec { get; set; }
    public string Container { get; set; }
    public List<MediaPart> Parts { get; set; } = [];
}

public class MediaPart
{
    public string Id { get; set; }
    public string Key { get; set; }
    public int Duration { get; set; }
    public string File { get; set; }
    public List<SubtitleStream> Subtitles { get; set; } = [];
}

public class SubtitleStream
{
    public int Id { get; set; }
    public int Index { get; set; }
    public string ExtendedDisplayTitle { get; set; }
    public string Language { get; set; }
    public bool Selected { get; set; }
    public string Format { get; set; }
    public string Title { get; set; }
    public string Location { get; set; }
    public bool IsExternal { get; set; }
}

// Class to hold session objects and associated subtitles
public class ActiveSession(PlexSession session, List<SubtitleStream> availableSubtitles, List<SubtitleStream> activeSubtitles)
{
    private PlexSession _session = session;
    private List<SubtitleStream> _availableSubtitles = availableSubtitles;
    private List<SubtitleStream> _activeSubtitles = activeSubtitles;

    public string DeviceName { get; } = session.Player.Title;
    public string MachineID { get; } = session.Player.MachineIdentifier;
    public string MediaTitle { get; } = session.GrandparentTitle ?? session.Title;
    public string SessionID { get; } = session.SessionId;

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
    public string Key { get; set; }

    [XmlAttribute("ratingKey")]
    public string RatingKey { get; set; }

    [XmlAttribute("sessionKey")]
    public string SessionKey { get; set; }

    [XmlAttribute("title")]
    public string Title { get; set; }

    [XmlAttribute("grandparentTitle")]
    public string GrandparentTitle { get; set; }

    [XmlAttribute("type")]
    public string Type { get; set; }

    [XmlAttribute("viewOffset")]
    public int ViewOffset { get; set; }

    [XmlElement("Player")]
    public PlexPlayerXml Player { get; set; }

    [XmlElement("Media")]
    public List<MediaXml> Media { get; set; } = [];

    [XmlElement("id")]
    public string Id { get; set; }

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
            SessionId = Id
        };

        if (Player != null)
        {
            session.Player.Title = Player.Title;
            session.Player.MachineIdentifier = Player.MachineIdentifier;
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
    public string Title { get; set; }

    [XmlAttribute("machineIdentifier")]
    public string MachineIdentifier { get; set; }
}

[XmlRoot("Server")]
public class PlexClientXml
{
    [XmlAttribute("name")]
    public string DeviceName { get; set; }

    [XmlAttribute("machineIdentifier")]
    public string MachineIdentifier { get; set; }

    [XmlAttribute("product")]
    public string ClientAppName { get; set; }

    [XmlAttribute("deviceClass")]
    public string DeviceClass { get; set; }

    [XmlAttribute("platform")]
    public string Platform { get; set; }

    // Convert to your existing PlexClient class
    public PlexClient ToPlexClient(HttpClient httpClient, string baseUrl, PlexServer plexServer)
    {
        return new PlexClient
        (
            deviceName: DeviceName,
            machineIdentifier: MachineIdentifier,
            clientAppName: ClientAppName,
            deviceClass: DeviceClass,
            platform: Platform ?? ClientAppName, // Handle the fallback
            httpClient: httpClient,
            baseUrl: baseUrl,
            plexServer: plexServer
        );
    }
}

[XmlRoot("Media")]
public class MediaXml
{
    [XmlAttribute("id")]
    public string Id { get; set; }

    [XmlAttribute("duration")]
    public int Duration { get; set; }

    [XmlAttribute("videoCodec")]
    public string VideoCodec { get; set; }

    [XmlAttribute("audioCodec")]
    public string AudioCodec { get; set; }

    [XmlAttribute("container")]
    public string Container { get; set; }

    [XmlElement("Part")]
    public List<MediaPartXml> Parts { get; set; } = [];

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
    public string Id { get; set; }

    [XmlAttribute("key")]
    public string Key { get; set; }

    [XmlAttribute("duration")]
    public int Duration { get; set; }

    [XmlAttribute("file")]
    public string File { get; set; }

    [XmlElement("Stream")]
    public List<SubtitleStreamXml> Subtitles { get; set; } = [];

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

        if (Subtitles != null)
        {
            foreach (SubtitleStreamXml subtitleXml in Subtitles)
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
    public string ExtendedDisplayTitle { get; set; }

    [XmlAttribute("language")]
    public string Language { get; set; }

    [XmlAttribute("selected")]
    public string SelectedValue { get; set; }

    [XmlAttribute("format")]
    public string Format { get; set; }

    [XmlAttribute("title")]
    public string Title { get; set; }

    [XmlAttribute("location")]
    public string Location { get; set; }

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
}