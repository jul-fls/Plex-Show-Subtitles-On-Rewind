namespace PlexShowSubtitlesOnRewind;
public class PlexMediaItem
{
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
    public string RatingKey { get; set; }
    public string SessionKey { get; set; }
    public string Title { get; set; }
    public string GrandparentTitle { get; set; }
    public string Type { get; set; } // movie, episode, etc.
    public int ViewOffset { get; set; } // in milliseconds
    public PlexPlayer Player { get; set; }
    public List<Media> Media { get; set; } = [];
    private PlexMediaItem _cachedItem;

    public PlexSession()
    {
        Player = new PlexPlayer();
        Media = new List<Media>();
    }

    public void Reload()
    {
        // In a real implementation, this would refresh the session data
        // For the simulation, we'll just increment the view offset
        ViewOffset += 1000; // move forward 1 second
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

public class PlexClient
{
    public string Title { get; set; }
    public string MachineIdentifier { get; set; }
    public string Device { get; set; }
    public string Model { get; set; }
    public string Platform { get; set; }
    public HttpClient HttpClient { get; set; }
    public string BaseUrl { get; set; }

    public async Task SetSubtitleStreamAsync(int streamId, string mediaType = "video")
    {
        try
        {
            // Send command to the Plex client
            string command = $"{BaseUrl}/player/playback/setSubtitleStream?id={streamId}&type={mediaType}&machineIdentifier={MachineIdentifier}";
            Console.WriteLine($"Sending command: {command}");

            HttpResponseMessage response = await HttpClient.GetAsync(command);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Successfully set subtitle stream {streamId} on client {Title}");
            }
            else
            {
                Console.WriteLine($"Failed to set subtitle stream {streamId} on client {Title}. Status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting subtitle stream: {ex.Message}");
        }
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
public class ActiveSession
{
    public PlexSession Session { get; }
    public List<SubtitleStream> AvailableSubtitles { get; private set; }
    public List<SubtitleStream> ActiveSubtitles { get; private set; }
    public string DeviceName { get; }
    public string MachineID { get; }
    public string MediaTitle { get; }

    public ActiveSession(
        PlexSession session,
        List<SubtitleStream> availableSubtitles,
        List<SubtitleStream> activeSubtitles,
        string deviceName,
        string machineID,
        string mediaTitle)
    {
        Session = session;
        AvailableSubtitles = availableSubtitles;
        ActiveSubtitles = activeSubtitles;
        DeviceName = deviceName;
        MachineID = machineID;
        MediaTitle = mediaTitle;
    }

    public double GetPlayPositionSeconds()
    {
        Session.Reload(); // Otherwise it won't update
        int positionMilliseconds = Session.ViewOffset;
        double positionSec = Math.Round(positionMilliseconds / 1000.0, 2);
        return positionSec;
    }

}
