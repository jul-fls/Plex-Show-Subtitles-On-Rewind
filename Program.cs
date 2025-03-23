namespace PlexSubtitleMonitor
{
    class Program
    {
        // Replace with your Plex server details
        private const string PLEX_URL = "http://192.168.1.103:32400";
        private static string PLEX_APP_TOKEN;
        private static string PLEX_PERSONAL_TOKEN;

        static async Task Main(string[] args)
        {
            try
            {
                LoadTokens();

                Console.WriteLine($"Connecting to Plex server at {PLEX_URL}");
                PlexServer plexServer = new PlexServer(PLEX_URL, PLEX_APP_TOKEN);

                // Setup periodic session refresh
                CancellationTokenSource tokenSource = new CancellationTokenSource();
                Task refreshTask = Task.Run(async () =>
                {
                    while (!tokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            Console.WriteLine("Loading active sessions...");
                            await SessionManager.LoadActiveSessionsAsync(plexServer);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error refreshing sessions: {ex.Message}");
                        }
                        
                        // Wait 30 seconds before refreshing again
                        await Task.Delay(TimeSpan.FromSeconds(30), tokenSource.Token);
                    }
                }, tokenSource.Token);

                // Keep program running
                Console.WriteLine("Monitoring active Plex sessions for rewinding. Press any key to exit...");
                Console.ReadKey();

                // Clean up when exiting
                Console.WriteLine("Shutting down...");
                tokenSource.Cancel();
                MonitorManager.StopAllMonitors();

                try
                {
                    await refreshTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation token is triggered
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        static void LoadTokens()
        {
            string tokenFilePath = "tokens.config";

            if (!File.Exists(tokenFilePath))
            {
                // Create tokens file if it doesn't exist
                File.WriteAllText(tokenFilePath, "AppToken=whatever_your_app_token_is\nPersonalToken=whatever_your_personal_token_is");
                Console.WriteLine("Please edit the tokens.config file with your Plex app and/or personal tokens.");
                Console.WriteLine("Exiting...");
                Environment.Exit(0);
            }
            else
            {
                // Read tokens from file
                string[] lines = File.ReadAllLines(tokenFilePath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("AppToken="))
                        PLEX_APP_TOKEN = line.Substring("AppToken=".Length);
                    else if (line.StartsWith("PersonalToken="))
                        PLEX_PERSONAL_TOKEN = line.Substring("PersonalToken=".Length);
                }
            }
        }


    }



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

        public async Task<List<PlexSession>> GetSessionsAsync()
        {
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}/status/sessions");
                List<PlexSession> sessions = new List<PlexSession>();

                // Parse XML response
                System.Xml.XmlDocument xmlDoc = new System.Xml.XmlDocument();
                xmlDoc.LoadXml(response);

                System.Xml.XmlNodeList? videoNodes = xmlDoc.SelectNodes("//MediaContainer/Video");
                if (videoNodes != null)
                {
                    foreach (System.Xml.XmlNode videoNode in videoNodes)
                    {
                        PlexSession session = new PlexSession();

                        // Extract video attributes
                        session.Key = GetAttribute(videoNode, "key");
                        session.Title = GetAttribute(videoNode, "title");
                        session.GrandparentTitle = GetAttribute(videoNode, "grandparentTitle");
                        session.Type = GetAttribute(videoNode, "type");
                        session.RatingKey = GetAttribute(videoNode, "ratingKey");
                        session.SessionKey = GetAttribute(videoNode, "sessionKey");

                        // Parse viewOffset as int
                        int.TryParse(GetAttribute(videoNode, "viewOffset"), out int viewOffset);
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
            string response = await _httpClient.GetStringAsync($"{_url}/clients");
            // Here you would parse the XML response from Plex
            // For simplicity, we'll simulate clients
            List<PlexClient> clients = new List<PlexClient>();

            // In a real implementation, you would parse XML and create proper clients
            clients.Add(new PlexClient
            {
                Title = "Apple TV",
                MachineIdentifier = "sample-machine-id-1",
                HttpClient = _httpClient,
                BaseUrl = _url
            });

            return clients;
        }

        public async Task<List<SubtitleStream>> GetAvailableSubtitlesForMediaAsync(string mediaKey)
        {
            try
            {
                // Make a direct call to the media metadata endpoint
                string response = await _httpClient.GetStringAsync($"{_url}{mediaKey}");
                List<SubtitleStream> subtitles = new List<SubtitleStream>();

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
                    // Find all Media nodes
                    System.Xml.XmlNodeList? mediaNodes = videoNode.SelectNodes("Media");
                    if (mediaNodes != null)
                    {
                        foreach (System.Xml.XmlNode mediaNode in mediaNodes)
                        {
                            // Find all Part nodes
                            System.Xml.XmlNodeList? partNodes = mediaNode.SelectNodes("Part");
                            if (partNodes != null)
                            {
                                foreach (System.Xml.XmlNode partNode in partNodes)
                                {
                                    // Find all Stream nodes with streamType=3 (subtitles)
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

                                            // Add additional subtitle details
                                            subtitle.Format = GetAttribute(streamNode, "format");
                                            subtitle.Title = GetAttribute(streamNode, "title");
                                            subtitle.Location = GetAttribute(streamNode, "location");
                                            subtitle.IsExternal = GetAttribute(streamNode, "external") == "1";

                                            subtitles.Add(subtitle);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                Console.WriteLine($"Found {subtitles.Count} available subtitle streams for media key: {mediaKey}");
                return subtitles;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting available subtitles: {ex.Message}");
                return new List<SubtitleStream>();
            }
        }

        private string GetAttribute(System.Xml.XmlNode node, string attributeName)
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

    public class PlexMediaItem
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public List<Media> Media { get; set; } = new List<Media>();

        public List<SubtitleStream> GetSubtitleStreams()
        {
            List<SubtitleStream> subtitles = new List<SubtitleStream>();

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
        public List<Media> Media { get; set; } = new List<Media>();
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
        public List<MediaPart> Parts { get; set; } = new List<MediaPart>();
    }

    public class MediaPart
    {
        public string Id { get; set; }
        public string Key { get; set; }
        public int Duration { get; set; }
        public string File { get; set; }
        public List<SubtitleStream> Subtitles { get; set; } = new List<SubtitleStream>();
    }

    public class SubtitleStream
    {
        public int Id { get; set; }
        public int Index { get; set; }
        public string ExtendedDisplayTitle { get; set; }
        public string Language { get; set; }
        public bool Selected { get; set; }
        public string Format { get; set; }  // Added
        public string Title { get; set; }   // Added
        public string Location { get; set; } // Added
        public bool IsExternal { get; set; } // Added
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

        public void SetActiveSubtitles(List<SubtitleStream> activeSubtitles)
        {
            ActiveSubtitles = activeSubtitles;
        }

        public void SetAvailableSubtitles(List<SubtitleStream> availableSubtitles)
        {
            AvailableSubtitles = availableSubtitles;
        }

        public List<SubtitleStream> GetActiveSubtitles()
        {
            return ActiveSubtitles;
        }

        public List<SubtitleStream> GetAvailableSubtitles()
        {
            return AvailableSubtitles;
        }
    }

    // Monitors a single session for rewinding
    public class SessionRewindMonitor
    {
        // Shared values
        public static int DefaultMaxRewindAmount = 60;
        public static int DefaultFrequency = 1;
        public static int DefaultSmallestResolution = 5; // iPhone has 5 second resolution apparently

        private readonly ActiveSession _activeSession;
        private readonly PlexClient _client;
        private readonly int _frequency;
        private readonly int _maxRewindAmount;
        private readonly bool _printDebug;

        private bool _isMonitoring;
        private Thread _monitorThread;
        private bool _subtitlesUserEnabled;
        private double _latestWatchedPosition;
        private double _previousPosition;
        private bool _temporarilyDisplayingSubtitles;
        private readonly int _smallestResolution;

        public SessionRewindMonitor(
            ActiveSession session,
            PlexClient client = null,
            int? frequency = null,
            int? maxRewindAmount = null,
            bool printDebug = false)
        {
            _activeSession = session;
            _client = client;
            _frequency = frequency ?? DefaultFrequency;
            _maxRewindAmount = maxRewindAmount ?? DefaultMaxRewindAmount;
            _printDebug = printDebug;

            _isMonitoring = false;
            _subtitlesUserEnabled = false;
            _latestWatchedPosition = 0;
            _previousPosition = 0;
            _temporarilyDisplayingSubtitles = false;
            _smallestResolution = Math.Max(_frequency, DefaultSmallestResolution);

            StartMonitoring();
        }

        private void RewindOccurred()
        {
            if (_printDebug)
            {
                Console.WriteLine($"Rewind occurred on {_activeSession.DeviceName} for {_activeSession.MediaTitle}");
            }
            ClientManager.EnableSubtitlesBySession(_activeSession);
            _temporarilyDisplayingSubtitles = true;
        }

        private void ReachedOriginalPosition()
        {
            if (_printDebug)
            {
                Console.WriteLine($"Reached original position on {_activeSession.DeviceName} for {_activeSession.MediaTitle}");
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
                Console.WriteLine($"Force stopping subtitles on {_activeSession.DeviceName} for {_activeSession.MediaTitle}");
            }
            ClientManager.DisableSubtitlesBySession(_activeSession);
        }

        private void MonitoringLoop()
        {
            try
            {
                while (_isMonitoring)
                {
                    // Refresh
                    double positionSec = _activeSession.GetPlayPositionSeconds();

                    try
                    {

                        if (_printDebug)
                        {
                            Console.WriteLine($"Loop iteration - position: {positionSec} -- Previous: {_previousPosition} -- Latest: {_latestWatchedPosition} -- UserEnabledSubtitles: {_subtitlesUserEnabled}\n");
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
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error in monitor thread iteration: {e.Message}");
                        // Add a small delay to avoid tight loop on errors
                        Thread.Sleep(1000);
                    }

                    _previousPosition = positionSec;
                    // Wait for the next iteration
                    Thread.Sleep(_frequency * 1000);
                }

                if (_printDebug)
                {
                    Console.WriteLine("Monitoring thread stopped normally.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Fatal error in monitoring thread: {e.Message}");
                Console.WriteLine(e.StackTrace);
            }
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            if (_temporarilyDisplayingSubtitles)
            {
                ForceStopShowingSubtitles();
            }
            if (_monitorThread != null && _monitorThread.IsAlive)
            {
                _monitorThread.Join(2000); // Wait up to 2 seconds for thread to finish
            }
        }

        public void StartMonitoring()
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

                if (_printDebug)
                {
                    Console.WriteLine("About to start thread with target: MonitoringLoop");
                }

                _monitorThread = new Thread(MonitoringLoop);
                _monitorThread.IsBackground = false; // Continue running. If the main thread is forced to stop, this will also stop though
                _monitorThread.Start();

                if (_printDebug)
                {
                    Console.WriteLine($"Thread started successfully with ID: {_monitorThread.ManagedThreadId}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error during thread startup: {e.Message}");
                Console.WriteLine(e.StackTrace);
            }
        }
    }

    public static class MonitorManager
    {
        private static List<SessionRewindMonitor> _allMonitors = new List<SessionRewindMonitor>();

        public static void StartMonitoringAllSessions(
            List<ActiveSession> activeSessionList,
            int? frequency = null,
            int? maxRewindAmount = null)
        {
            foreach (ActiveSession activeSession in activeSessionList)
            {
                bool printDebug = activeSession.DeviceName == "Apple TV";
                SessionRewindMonitor monitor = new SessionRewindMonitor(activeSession, printDebug: printDebug);
                _allMonitors.Add(monitor);
            }
        }

        public static void StopAllMonitors()
        {
            foreach (SessionRewindMonitor monitor in _allMonitors)
            {
                monitor.StopMonitoring();
            }
        }
    }

    public static class ClientManager
    {
        private static List<PlexClient> _clientList = new List<PlexClient>();
        private static readonly object _lockObject = new object();

        public static async Task<List<PlexClient>> LoadClientsAsync(PlexServer plexServer)
        {
            try
            {
                List<PlexClient> clientList = await plexServer.GetClientsAsync();

                lock (_lockObject)
                {
                    _clientList.Clear();
                    foreach (PlexClient client in clientList)
                    {
                        _clientList.Add(client);
                    }
                }

                Console.WriteLine($"Loaded {_clientList.Count} Plex clients");
                return _clientList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading clients: {ex.Message}");
                return new List<PlexClient>();
            }
        }

        public static List<PlexClient> Get()
        {
            lock (_lockObject)
            {
                return new List<PlexClient>(_clientList);
            }
        }

        public static async Task EnableSubtitlesAsync(PlexClient client, int subtitleStreamID)
        {
            await client.SetSubtitleStreamAsync(subtitleStreamID);
        }

        public static async Task DisableSubtitlesAsync(PlexClient client)
        {
            await client.SetSubtitleStreamAsync(0);
        }

        public static async Task EnableSubtitlesByClientAsync(PlexClient client, int subtitleStreamID)
        {
            await EnableSubtitlesAsync(client, subtitleStreamID);
        }

        public static async Task DisableSubtitlesByClientAsync(PlexClient client)
        {
            await DisableSubtitlesAsync(client);
        }

        public static PlexClient GetClient(object inputObj)
        {
            string machineID;

            if (inputObj is ActiveSession activeSession)
            {
                machineID = activeSession.MachineID;
            }
            else if (inputObj is PlexSession plexSession)
            {
                machineID = plexSession.Player.MachineIdentifier;
            }
            else
            {
                throw new Exception("Invalid input object type into ClientManager.GetClient().");
            }

            lock (_lockObject)
            {
                return _clientList.FirstOrDefault(client => client.MachineIdentifier == machineID);
            }
        }

        public static async Task DisableSubtitlesBySessionAsync(object session)
        {
            PlexClient client = GetClient(session);
            if (client != null)
            {
                await DisableSubtitlesAsync(client);
            }
            else
            {
                Console.WriteLine("No client found for this session");
            }
        }

        // For simplicity, also providing non-async versions
        public static void DisableSubtitlesBySession(object session)
        {
            DisableSubtitlesBySessionAsync(session).Wait();
        }

        public static async Task EnableSubtitlesBySessionAsync(
            object session,
            int? subtitleStreamID = null,
            SubtitleStream subtitleStream = null)
        {
            PlexClient client = GetClient(session);
            if (client == null)
            {
                Console.WriteLine("No client found for this session");
                return;
            }

            if (subtitleStreamID.HasValue)
            {
                await EnableSubtitlesAsync(client, subtitleStreamID.Value);
            }
            else if (subtitleStream != null)
            {
                await EnableSubtitlesAsync(client, subtitleStream.Id);
            }
            else if (session is ActiveSession activeSession && activeSession.AvailableSubtitles.Count > 0)
            {
                await EnableSubtitlesAsync(client, activeSession.AvailableSubtitles[0].Id);
            }
            else
            {
                Console.WriteLine("No subtitle stream provided.");
            }
        }

        // For simplicity, also providing non-async versions
        public static void EnableSubtitlesBySession(
            object session,
            int? subtitleStreamID = null,
            SubtitleStream subtitleStream = null)
        {
            EnableSubtitlesBySessionAsync(session, subtitleStreamID, subtitleStream).Wait();
        }
    }

    public static class SessionManager
    {
        private static List<ActiveSession> _activeSessionList = new List<ActiveSession>();
        private static readonly object _lockObject = new object();

        public static async Task<List<ActiveSession>> LoadActiveSessionsAsync(PlexServer plexServer)
        {
            var sessionsList = await plexServer.GetSessionsAsync();
            var newActiveSessionList = new List<ActiveSession>();

            foreach (var session in sessionsList)
            {
                string deviceName = session.Player.Title;
                string machineID = session.Player.MachineIdentifier;

                // Get active subtitles directly from the session Media
                var activeSubs = GetActiveSubtitlesFromMedia(session);

                // Get ALL available subtitles with a separate metadata call
                var availableSubs = await GetAllAvailableSubtitlesAsync(session, plexServer);

                string mediaTitle = session.GrandparentTitle ?? session.Title;

                newActiveSessionList.Add(new ActiveSession(
                    session: session,
                    availableSubtitles: availableSubs,
                    activeSubtitles: activeSubs,
                    deviceName: deviceName,
                    machineID: machineID,
                    mediaTitle: mediaTitle
                ));
            }

            lock (_lockObject)
            {
                _activeSessionList.Clear();
                _activeSessionList.AddRange(newActiveSessionList);
            }

            await ClientManager.LoadClientsAsync(plexServer);
            MonitorManager.StartMonitoringAllSessions(_activeSessionList);

            Console.WriteLine($"Loaded {_activeSessionList.Count} active sessions");
            PrintSubtitles(); // Print initial subtitle status

            return _activeSessionList;
        }

        public static List<ActiveSession> Get()
        {
            lock (_lockObject)
            {
                return new List<ActiveSession>(_activeSessionList);
            }
        }

        private static async Task<List<SubtitleStream>> GetAllAvailableSubtitlesAsync(PlexSession session, PlexServer plexServer)
        {
            try
            {
                // Make a separate call to get the full media metadata including all subtitle tracks
                string mediaKey = session.Key; // Like '/library/metadata/20884'
                var mediaItem = await plexServer.FetchItemAsync(mediaKey);

                // Get all subtitle streams from the media item
                var subtitles = mediaItem.GetSubtitleStreams();

                Console.WriteLine($"Found {subtitles.Count} available subtitle tracks for {session.Title}");
                return subtitles;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting available subtitles: {ex.Message}");
                return new List<SubtitleStream>();
            }
        }

        public static async Task<List<SubtitleStream>> GetAvailableSubtitlesAsync(PlexSession session, PlexServer plexServer)
        {
            try
            {
                // First check if we already have this information in the session
                List<SubtitleStream> subsFromMedia = GetAvailableSubtitlesFromMedia(session);
                if (subsFromMedia.Count > 0)
                {
                    return subsFromMedia;
                }

                // Otherwise fetch all available subtitles from the server
                string mediaKey = session.Key; // Like '/library/metadata/20884'
                return await plexServer.GetAvailableSubtitlesForMediaAsync(mediaKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting available subtitles: {ex.Message}");
                return new List<SubtitleStream>();
            }
        }

        private static List<SubtitleStream> GetAvailableSubtitlesFromMedia(PlexSession session)
        {
            List<SubtitleStream> result = new List<SubtitleStream>();

            if (session.Media != null && session.Media.Count > 0)
            {
                foreach (Media media in session.Media)
                {
                    if (media.Parts != null && media.Parts.Count > 0)
                    {
                        foreach (MediaPart part in media.Parts)
                        {
                            result.AddRange(part.Subtitles);
                        }
                    }
                }
            }

            return result;
        }

        private static List<SubtitleStream> GetActiveSubtitlesFromMedia(PlexSession session)
        {
            List<SubtitleStream> result = new List<SubtitleStream>();

            if (session.Media != null && session.Media.Count > 0)
            {
                foreach (Media media in session.Media)
                {
                    if (media.Parts != null && media.Parts.Count > 0)
                    {
                        foreach (MediaPart part in media.Parts)
                        {
                            // Only add subtitles that are marked as selected
                            result.AddRange(part.Subtitles.Where(s => s.Selected));
                        }
                    }
                }
            }

            return result;
        }

        public static async Task<List<SubtitleStream>> GetActiveSubtitlesAsync(PlexSession session, PlexServer plexServer)
        {
            try
            {
                // First check if we already have this information in the session
                List<SubtitleStream> subsFromMedia = GetActiveSubtitlesFromMedia(session);
                if (subsFromMedia.Count > 0)
                {
                    return subsFromMedia;
                }

                // Otherwise fetch from the server
                if (session.Media != null && session.Media.Count > 0)
                {
                    Media media = session.Media[0];
                    if (media.Parts != null && media.Parts.Count > 0)
                    {
                        return media.Parts[0].Subtitles.Where(s => s.Selected).ToList();
                    }
                }

                return new List<SubtitleStream>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting active subtitles: {ex.Message}");
                return new List<SubtitleStream>();
            }
        }

        public static void PrintSubtitles()
        {
            foreach (ActiveSession activeSession in _activeSessionList)
            {
                List<SubtitleStream> activeSubtitles = activeSession.ActiveSubtitles;
                List<SubtitleStream> availableSubtitles = activeSession.AvailableSubtitles;
                string deviceName = activeSession.DeviceName;
                string mediaTitle = activeSession.MediaTitle;

                Console.WriteLine("\n-------------------------------------");
                Console.WriteLine($"Active Subtitles for {mediaTitle} on {deviceName}:");
                if (activeSubtitles.Count == 0)
                {
                    Console.WriteLine("[None]");
                }
                else
                {
                    foreach (SubtitleStream subtitle in activeSubtitles)
                    {
                        Console.WriteLine(subtitle.ExtendedDisplayTitle);
                    }
                }

                Console.WriteLine($"\nAvailable Subtitles for {mediaTitle} on {deviceName}:");
                if (availableSubtitles.Count == 0)
                {
                    Console.WriteLine("[None]");
                }
                else
                {
                    foreach (SubtitleStream subtitle in availableSubtitles)
                    {
                        Console.WriteLine(subtitle.ExtendedDisplayTitle);
                    }
                }
            }
        }
    }
}