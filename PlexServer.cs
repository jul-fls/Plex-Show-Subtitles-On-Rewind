namespace PlexShowSubtitlesOnRewind
{
    // Represents a Plex server
    public class PlexServer
    {
        private readonly string _url;
        private readonly string _token;
        private readonly HttpClient _httpClient;

        public PlexServer(string url, string token)
        {
            _url = url;
            _token = token;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-Plex-Token", token);
        }

        // Fetches active sessions from the Plex server, parses the resulting XML, and returns a list of custom PlexSession objects
        public async Task<List<PlexSession>> GetSessionsAsync()
        {
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}/status/sessions");
                List<PlexSession> sessions = [];

                // Parse XML response
                System.Xml.XmlDocument xmlDoc = new();
                xmlDoc.LoadXml(response);

                System.Xml.XmlNodeList? videoNodes = xmlDoc.SelectNodes("//MediaContainer/Video");
                if (videoNodes != null)
                {
                    foreach (System.Xml.XmlNode videoNode in videoNodes)
                    {
                        PlexSession session = new()
                        {
                            // Extract video attributes
                            Key = GetAttribute(videoNode, "key"),
                            Title = GetAttribute(videoNode, "title"),
                            GrandparentTitle = GetAttribute(videoNode, "grandparentTitle"), // Usually the name of the show
                            Type = GetAttribute(videoNode, "type"),
                            RatingKey = GetAttribute(videoNode, "ratingKey"),
                            SessionKey = GetAttribute(videoNode, "sessionKey")
                        };

                        // Parse viewOffset as int. ViewOffset is the current position of the playhead in the video (in milliseconds)
                        _ = int.TryParse(GetAttribute(videoNode, "viewOffset"), out int viewOffset);
                        session.ViewOffset = viewOffset;

                        // Extract media information
                        System.Xml.XmlNodeList? mediaNodes = videoNode.SelectNodes("Media");
                        if (mediaNodes != null)
                        {
                            foreach (System.Xml.XmlNode mediaNode in mediaNodes)
                            {
                                Media media = new Media
                                {
                                    Id = GetAttribute(mediaNode, "id"),
                                    Duration = int.TryParse(GetAttribute(mediaNode, "duration"), out int duration) ? duration : 0,
                                    VideoCodec = GetAttribute(mediaNode, "videoCodec"),
                                    AudioCodec = GetAttribute(mediaNode, "audioCodec"),
                                    Container = GetAttribute(mediaNode, "container")
                                };

                                // Extract part information
                                System.Xml.XmlNodeList? partNodes = mediaNode.SelectNodes("Part");
                                if (partNodes != null)
                                {
                                    foreach (System.Xml.XmlNode partNode in partNodes)
                                    {
                                        MediaPart part = new MediaPart
                                        {
                                            Id = GetAttribute(partNode, "id"),
                                            Key = GetAttribute(partNode, "key"),
                                            Duration = int.TryParse(GetAttribute(partNode, "duration"), out int partDuration) ? partDuration : 0,
                                            File = GetAttribute(partNode, "file")
                                        };

                                        // Extract enabled subtitle streams (streamType='3') - Won't show available subtitles, only active ones
                                        System.Xml.XmlNodeList? streamNodes = partNode.SelectNodes("Stream[@streamType='3']");
                                        if (streamNodes != null)
                                        {
                                            foreach (System.Xml.XmlNode streamNode in streamNodes)
                                            {
                                                SubtitleStream subtitle = new SubtitleStream
                                                {
                                                    Id = int.TryParse(GetAttribute(streamNode, "id"), out int id) ? id : 0,
                                                    Index = int.TryParse(GetAttribute(streamNode, "index"), out int index) ? index : 0,
                                                    ExtendedDisplayTitle = GetAttribute(streamNode, "extendedDisplayTitle"), // Usually includes language and other info like if "SDH"
                                                    Language = GetAttribute(streamNode, "language"),
                                                    Selected = GetAttribute(streamNode, "selected") == "1"
                                                };
                                                part.Subtitles.Add(subtitle);
                                            }
                                        }

                                        media.Parts.Add(part);
                                    }
                                }

                                session.Media.Add(media);
                            }
                        }

                        sessions.Add(session);
                    }
                }

                Console.WriteLine($"Found {sessions.Count} active Plex sessions");
                return sessions;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting sessions: {ex.Message}");
                return new List<PlexSession>();
            }
        }

        public async Task<List<PlexClient>> GetClientsAsync()
        {
            try
            {
                string clientsResponse = await _httpClient.GetStringAsync($"{_url}/clients");
                List<PlexClient> clients = [];

                // Parse XML response
                System.Xml.XmlDocument xmlDoc = new();
                xmlDoc.LoadXml(clientsResponse);

                System.Xml.XmlNodeList? serverNodes = xmlDoc.SelectNodes("//MediaContainer/Server");
                if (serverNodes != null)
                {
                    foreach (System.Xml.XmlNode serverNode in serverNodes)
                    {
                        PlexClient client = new()
                        {
                            // Extract server attributes based on the XML example
                            DeviceName = GetAttribute(serverNode, "name"),
                            MachineIdentifier = GetAttribute(serverNode, "machineIdentifier"),
                            ClientAppName = GetAttribute(serverNode, "product"),
                            DeviceClass = GetAttribute(serverNode, "deviceClass"),
                            Platform = GetAttribute(serverNode, "platform") != string.Empty ?
                               GetAttribute(serverNode, "platform") :
                               GetAttribute(serverNode, "product"),
                            HttpClient = _httpClient,
                            BaseUrl = _url
                        };

                        clients.Add(client);
                    }
                }

                Console.WriteLine($"Found {clients.Count} connected Plex clients");
                return clients;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting clients: {ex.Message}");
                return new List<PlexClient>();
            }
        }

        private static string GetAttribute(System.Xml.XmlNode node, string attributeName)
        {
            if (node == null || node.Attributes == null)
                return string.Empty;

            System.Xml.XmlAttribute? attr = node.Attributes[attributeName];
            return attr?.Value ?? string.Empty;
        }

        public async Task<PlexMediaItem> FetchItemAsync(string key)
        {
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}{key}");
                PlexMediaItem mediaItem = new PlexMediaItem { Key = key };

                // Parse XML response
                System.Xml.XmlDocument xmlDoc = new System.Xml.XmlDocument();
                xmlDoc.LoadXml(response);

                // Get the media item node
                System.Xml.XmlNode? mediaContainer = xmlDoc.SelectSingleNode("//MediaContainer");
                System.Xml.XmlNode? videoNode = mediaContainer?.SelectSingleNode("Video") ??
                        mediaContainer?.SelectSingleNode("Track") ??
                        mediaContainer?.SelectSingleNode("Episode");

                if (videoNode != null)
                {
                    mediaItem.Title = GetAttribute(videoNode, "title");
                    mediaItem.Type = GetAttribute(videoNode, "type");

                    // Extract media information
                    System.Xml.XmlNodeList? mediaNodes = videoNode.SelectNodes("Media");
                    if (mediaNodes != null)
                    {
                        foreach (System.Xml.XmlNode mediaNode in mediaNodes)
                        {
                            Media media = new Media
                            {
                                Id = GetAttribute(mediaNode, "id"),
                                Duration = int.TryParse(GetAttribute(mediaNode, "duration"), out int duration) ? duration : 0,
                                VideoCodec = GetAttribute(mediaNode, "videoCodec"),
                                AudioCodec = GetAttribute(mediaNode, "audioCodec"),
                                Container = GetAttribute(mediaNode, "container")
                            };

                            // Extract part information
                            System.Xml.XmlNodeList? partNodes = mediaNode.SelectNodes("Part");
                            if (partNodes != null)
                            {
                                foreach (System.Xml.XmlNode partNode in partNodes)
                                {
                                    MediaPart part = new MediaPart
                                    {
                                        Id = GetAttribute(partNode, "id"),
                                        Key = GetAttribute(partNode, "key"),
                                        Duration = int.TryParse(GetAttribute(partNode, "duration"), out int partDuration) ? partDuration : 0,
                                        File = GetAttribute(partNode, "file")
                                    };

                                    // Extract ALL subtitle streams (streamType=3)
                                    System.Xml.XmlNodeList? streamNodes = partNode.SelectNodes("Stream[@streamType='3']");
                                    if (streamNodes != null)
                                    {
                                        foreach (System.Xml.XmlNode streamNode in streamNodes)
                                        {
                                            SubtitleStream subtitle = new SubtitleStream
                                            {
                                                Id = int.TryParse(GetAttribute(streamNode, "id"), out int id) ? id : 0,
                                                Index = int.TryParse(GetAttribute(streamNode, "index"), out int index) ? index : 0,
                                                ExtendedDisplayTitle = GetAttribute(streamNode, "extendedDisplayTitle"),
                                                Language = GetAttribute(streamNode, "language"),
                                                Selected = GetAttribute(streamNode, "selected") == "1"
                                            };
                                            part.Subtitles.Add(subtitle);
                                        }
                                    }

                                    media.Parts.Add(part);
                                }
                            }

                            mediaItem.Media.Add(media);
                        }
                    }
                }

                int subtitleCount = mediaItem.GetSubtitleStreams().Count;
                Console.WriteLine($"Fetched media item: {mediaItem.Title} with {subtitleCount} subtitle streams");

                return mediaItem;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching media item: {ex.Message}");
                return new PlexMediaItem { Key = key };
            }
        }
    }
}