using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

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
            LoadTokens();

            var plexServer = new PlexServer(PLEX_URL, PLEX_APP_TOKEN);
            await SessionManager.LoadActiveSessionsAsync(plexServer);

            // Keep program running
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();

            // Clean up when exiting
            MonitorManager.StopAllMonitors();
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
                var lines = File.ReadAllLines(tokenFilePath);
                foreach (var line in lines)
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
            string response = await _httpClient.GetStringAsync($"{_url}/status/sessions");
            // Here you would parse the XML response from Plex
            // For simplicity, we'll simulate sessions
            List<PlexSession> sessions = new List<PlexSession>();

            // In a real implementation, you would parse XML and create proper sessions
            Console.WriteLine("USING PLACEHOLDER DATA...");
            sessions.Add(new PlexSession
            {
                Key = "/library/metadata/12345",
                Title = "Sample Media",
                GrandparentTitle = "Sample Show",
                ViewOffset = 100000, // 100 seconds
                Player = new PlexPlayer
                {
                    Title = "Apple TV",
                    MachineIdentifier = "sample-machine-id-1"
                }
            });

            return sessions;
        }

        public async Task<List<PlexClient>> GetClientsAsync()
        {
            var response = await _httpClient.GetStringAsync($"{_url}/clients");
            // Here you would parse the XML response from Plex
            // For simplicity, we'll simulate clients
            var clients = new List<PlexClient>();

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

        public async Task<PlexMediaItem> FetchItemAsync(string key)
        {
            var response = await _httpClient.GetStringAsync($"{_url}{key}");
            // Here you would parse the XML response from Plex
            // For simplicity, we'll simulate media items
            var mediaItem = new PlexMediaItem
            {
                Key = key,
                Media = new List<Media> {
                    new Media {
                        Parts = new List<MediaPart> {
                            new MediaPart {
                                Subtitles = new List<SubtitleStream> {
                                    new SubtitleStream { Id = 1, ExtendedDisplayTitle = "English (SRT)" },
                                    new SubtitleStream { Id = 2, ExtendedDisplayTitle = "Spanish (SRT)" }
                                }
                            }
                        }
                    }
                }
            };

            return mediaItem;
        }
    }

    public class PlexMediaItem
    {
        public string Key { get; set; }
        public List<Media> Media { get; set; } = new List<Media>();

        public List<SubtitleStream> GetSubtitleStreams()
        {
            var subtitles = new List<SubtitleStream>();

            foreach (var media in Media)
            {
                foreach (var part in media.Parts)
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
        public string Title { get; set; }
        public string GrandparentTitle { get; set; }
        public int ViewOffset { get; set; } // in milliseconds
        public PlexPlayer Player { get; set; }
        private PlexMediaItem _cachedItem;

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
        public HttpClient HttpClient { get; set; }
        public string BaseUrl { get; set; }

        public async Task SetSubtitleStreamAsync(int streamId, string mediaType = "video")
        {
            // In a real implementation, this would send a command to the Plex client
            await HttpClient.GetAsync($"{BaseUrl}/player/playback/setSubtitleStream?id={streamId}&type={mediaType}&machineIdentifier={MachineIdentifier}");
            Console.WriteLine($"Setting subtitle stream {streamId} on client {Title}");
        }
    }

    public class Media
    {
        public List<MediaPart> Parts { get; set; } = new List<MediaPart>();
    }

    public class MediaPart
    {
        public List<SubtitleStream> Subtitles { get; set; } = new List<SubtitleStream>();
    }

    public class SubtitleStream
    {
        public int Id { get; set; }
        public string ExtendedDisplayTitle { get; set; }
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
                    double positionSec = _latestWatchedPosition;
                    try
                    {
                        // Refresh
                        positionSec = _activeSession.GetPlayPositionSeconds();
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
            foreach (var activeSession in activeSessionList)
            {
                bool printDebug = activeSession.DeviceName == "Apple TV";
                var monitor = new SessionRewindMonitor(activeSession, printDebug: printDebug);
                _allMonitors.Add(monitor);
            }
        }

        public static void StopAllMonitors()
        {
            foreach (var monitor in _allMonitors)
            {
                monitor.StopMonitoring();
            }
        }
    }

    public static class ClientManager
    {
        private static List<PlexClient> _clientList = new List<PlexClient>();

        public static async Task<List<PlexClient>> LoadClientsAsync(PlexServer plexServer)
        {
            var clientList = await plexServer.GetClientsAsync();
            _clientList.Clear();
            foreach (var client in clientList)
            {
                _clientList.Add(client);
            }
            return _clientList;
        }

        public static List<PlexClient> Get()
        {
            return _clientList;
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

            return _clientList.FirstOrDefault(client => client.MachineIdentifier == machineID);
        }

        public static async Task DisableSubtitlesBySessionAsync(object session)
        {
            var client = GetClient(session);
            await DisableSubtitlesAsync(client);
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
            var client = GetClient(session);

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

        public static async Task<List<ActiveSession>> LoadActiveSessionsAsync(PlexServer plexServer)
        {
            var sessionsList = await plexServer.GetSessionsAsync();
            _activeSessionList.Clear();

            foreach (var session in sessionsList)
            {
                string deviceName = session.Player.Title;
                string machineID = session.Player.MachineIdentifier;
                var activeSubs = await GetActiveSubtitlesAsync(session, plexServer);
                var availableSubs = await GetAvailableSubtitlesAsync(session, plexServer);
                string mediaTitle = session.GrandparentTitle ?? session.Title;

                _activeSessionList.Add(new ActiveSession(
                    session: session,
                    availableSubtitles: availableSubs,
                    activeSubtitles: activeSubs,
                    deviceName: deviceName,
                    machineID: machineID,
                    mediaTitle: mediaTitle
                ));
            }

            await ClientManager.LoadClientsAsync(plexServer);
            MonitorManager.StartMonitoringAllSessions(_activeSessionList);
            return _activeSessionList;
        }

        public static List<ActiveSession> Get()
        {
            return _activeSessionList;
        }

        public static async Task<List<SubtitleStream>> GetAvailableSubtitlesAsync(PlexSession session, PlexServer plexServer)
        {
            string mediaKey = session.Key; // Like '/library/metadata/20884'
            var mediaItem = await session.FetchItemAsync(mediaKey, plexServer);
            return mediaItem.GetSubtitleStreams();
        }

        public static async Task<List<SubtitleStream>> GetActiveSubtitlesAsync(PlexSession session, PlexServer plexServer)
        {
            // In a real implementation, this would get the active subtitles
            // For simplicity, we'll just return an empty list
            return new List<SubtitleStream>();
        }

        public static void PrintSubtitles()
        {
            foreach (var activeSession in _activeSessionList)
            {
                var activeSubtitles = activeSession.ActiveSubtitles;
                var availableSubtitles = activeSession.AvailableSubtitles;
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
                    foreach (var subtitle in activeSubtitles)
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
                    foreach (var subtitle in availableSubtitles)
                    {
                        Console.WriteLine(subtitle.ExtendedDisplayTitle);
                    }
                }
            }
        }
    }
}