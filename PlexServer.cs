using System.ComponentModel;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Serialization;

namespace PlexShowSubtitlesOnRewind
{
    public static class PlexServer
    {
        private static string _url;
        private static string _token;
        private static string _appClientID;
        private static HttpClient _httpClient;
        private static HttpClient _httpClientShortTimeout;
        private static CancellationTokenSource? _cancellationTokenSource;
        private static PlexNotificationListener? _currentListener;
        private static bool _alreadyRetrying = false;

        // Add these static fields near the top of the PlexServer class
        private static bool _isAttemptingReconnection = false;
        private static readonly object _reconnectionLock = new object(); // For thread safety

        public static void SetupPlexServer(string url, string token, string appClientID)
        {
            Dictionary<string, string> defaultHeadersDict = new()
            {
                { "X-Plex-Token", token },
                { "X-Plex-Client-Identifier", appClientID }
            };

            _url = url;
            _token = token;
            _appClientID = appClientID;

            _httpClient = new HttpClient();
            _httpClientShortTimeout = new HttpClient();

            _httpClient = Utils.AddHttpClientHeaders(_httpClient, defaultHeadersDict);
            _httpClientShortTimeout = Utils.AddHttpClientHeaders(_httpClientShortTimeout, defaultHeadersDict);
            _httpClientShortTimeout.Timeout = TimeSpan.FromMilliseconds(Program.config.ShortTimeoutLimit); // Will be used between loop iterations which only last a second
        }

        public static async Task<bool> InitializeMonitoring()
        {
            bool debugMode = Program.debugMode;
            // Load active sessions and start monitoring
            try
            {
                if (debugMode)
                    Console.WriteLine("Loading active sessions...");

                List<ActiveSession> activeSessionList = await SessionHandler.ClearAndLoadActiveSessionsAsync();

                if (debugMode)
                    SessionHandler.PrintSubtitles();

                _currentListener = MonitorManager.CreatePlexListener(plexUrl: Program.config.ServerURL, plexToken: _token);

                Console.WriteLine($"Found {activeSessionList.Count} active session(s). Future sessions will be added. Beginning monitoring...\n");
                MonitorManager.CreateAllMonitoringAllSessions(
                    activeSessionList,
                    activeFrequency: Program.config.ActiveMonitorFrequency,
                    idleFrequency: Program.config.IdleMonitorFrequency,
                    printDebugAll: debugMode);

                return true;
            }
            catch (Exception ex)
            {
                WriteError($"Error getting sessions: {ex.Message}");
                return false;
            }
        }

        // Shouldn't need this since ListenForEvents() stops these if it fails
        private static void KillAllListening()
        {
            MonitorManager.StopAllMonitoring();
            _currentListener?.StopListening();
        }

        // Using XmlSerializer to get sessions
        public static async Task<List<PlexSession>?> GetSessionsAsync(bool printDebug = false, bool shortTimeout = false)
        {
            HttpClient httpClientToUse = shortTimeout ? _httpClientShortTimeout : _httpClient;

            try
            {
                //string response = await httpClientToUse.GetStringAsync($"{_url}/status/sessions");
                string responseString;
                HttpResponseMessage response = await httpClientToUse.GetAsync($"{_url}/status/sessions");
                responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string statusCode = ((int)response.StatusCode).ToString();
                    string statusName = response.StatusCode.ToString();
                    string errorText = await response.Content.ReadAsStringAsync();
                    errorText = errorText.Replace("\n", " ");
                    WriteError($"Error getting sessions: {statusCode} ({statusName}), Error: {errorText}");
                    return null;
                }

                // Deserialize directly to your model
                MediaContainer? container = XmlSerializerHelper.DeserializeXml<MediaContainer>(responseString);

                if (container != null)
                {
                    // Store raw XML in case it's useful
                    foreach (PlexSession session in container.Sessions)
                    {
                        session.RawXml = responseString;
                    }

                    if (printDebug)
                        Console.WriteLine($"Found {container.Sessions.Count} active Plex sessions");

                    return container.Sessions;
                }
                else
                {
                    Console.WriteLine("Something went wrong deserializing the sessions MediaContainer. It returned null.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting sessions (Will return what it can): {ex.Message}");
                return null;
            }
        }

        public static async Task<ConnectionResult> TestConnectionAsync()
        {
            try
            {
                string testUrl = $"{_url}/";
                HttpResponseMessage response = await _httpClient.GetAsync(testUrl);

                if (response.IsSuccessStatusCode)
                {
                    WriteGreen("Connection successful!\n");
                    return ConnectionResult.Success;
                }
                else
                {
                    string statusCode = ((int)response.StatusCode).ToString();
                    string statusName = response.StatusCode.ToString();
                    string errorText = await response.Content.ReadAsStringAsync();
                    errorText = errorText.Replace("\n", " ");

                    // Try to parse the errorText XML to a ConnectionTestResponse object
                    ConnectionTestResponse? testResponse = XmlSerializerHelper.DeserializeXml<ConnectionTestResponse>(errorText);

                    if (testResponse != null)
                    {
                        // Show more specific error/status if available
                        string statusText = testResponse.Status ?? errorText;
                        string titleText = testResponse.Title ?? string.Empty;

                        if (titleText != string.Empty)
                            titleText = $"{titleText} - ";

                        // If it's maintenance, return that specifically so we know to check more often
                        if (testResponse.Code == "503" && String.Equals(testResponse.Title, "maintenance", StringComparison.CurrentCultureIgnoreCase))
                        {
                            Console.WriteLine($"Connection failed. Status Code: {statusCode} ({statusName}), Reason: {titleText}{statusText}\n");
                            return ConnectionResult.Maintenance;
                        }
                        else
                        {
                            Console.WriteLine($"Connection failed. Status Code: {statusCode} ({statusName}), Reason: {titleText}{statusText}\n");
                            return ConnectionResult.Failure;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Connection failed. Status Code: {statusCode} ({statusName}), Error: {errorText}\n");
                        return ConnectionResult.Failure;
                    }
                    
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error testing connection: {ex.Message}\n");
                return ConnectionResult.Failure;
            }
        }

        public enum ConnectionResult
        {
            Success,
            Failure,
            Maintenance,
            Cancelled
        }

        [XmlRoot("Response")]
        public class ConnectionTestResponse
        {
            [XmlAttribute("code")]
            public string? Code { get; set; } = null;

            [XmlAttribute("title")]
            public string? Title { get; set; } = null;

            [XmlAttribute("status")]
            public string? Status { get; set; } = null;
        }

        // Replace the existing StartServerConnectionTestLoop method in PlexServer.cs with this:
        public static async Task<bool> StartServerConnectionTestLoop()
        {
            lock (_reconnectionLock)
            {
                if (_isAttemptingReconnection)
                {
                    Console.WriteLine("Reconnection attempt already in progress by another task.");
                    return false; // Indicate that another loop is handling it
                }
                _isAttemptingReconnection = true; // Set the flag *inside* the lock
            }

            try
            {

                ConnectionResult connected = await TestConnectionAsync();
                if (connected != ConnectionResult.Success)
                {
                    // Dispose previous token source if exists before creating a new one
                    _cancellationTokenSource?.Cancel(); // Cancel previous first
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = new CancellationTokenSource();
                    CancellationToken token = _cancellationTokenSource.Token;

                    // Enter the retry loop ONLY if the initial test failed
                    connected = await ServerConnectionTestLoop(token); // The actual retry loop
                }

                // After attempting connection (either initial or via loop)
                if (connected == ConnectionResult.Success)
                {
                    // Successfully connected (or reconnected), initialize monitoring
                    bool monitorInitSuccess = await InitializeMonitoring();
                    return monitorInitSuccess; // Return success/failure of monitoring init
                }
                else
                {
                    // Failed to connect even after retries (or cancelled)
                    return false;
                }
            }
            catch (Exception ex) // Catch potential errors during connection or monitoring init
            {
                WriteError($"Error during connection/monitoring startup: {ex.Message}");
                // Ensure flag is reset even if InitializeMonitoring fails
                lock (_reconnectionLock) { _isAttemptingReconnection = false; }
                return false;
            }
            finally
            {
                // Reset the flag ONLY if the connection ultimately failed or monitoring init failed
                // If connection succeeded AND monitoring succeeded, the flag remains true until explicitly stopped or another error occurs.
                // Correction: Flag should be reset when the entire *attempt* sequence finishes, regardless of success/failure.
                lock (_reconnectionLock)
                {
                    _isAttemptingReconnection = false;
                }
            }
        }

        // Add this new method to the PlexServer.cs class
        public static void StopServerConnectionTestLoop()
        {
            lock (_reconnectionLock)
            {
                // Set flag to false *first* to prevent new loops starting immediately after cancel
                _isAttemptingReconnection = false;
            }
            _cancellationTokenSource?.Cancel(); // Primary mechanism
            _cancellationTokenSource?.Dispose(); // Dispose the source after cancellation
            _cancellationTokenSource = null;
            Console.WriteLine("StopServerConnectionTestLoop called: Reconnection attempts stopped.");
        }

        // If not able to initially connect, or if the connection is lost, this method can be used to attempt reconnection with exponential backoff
        // Replace the existing private ServerConnectionTestLoop method in PlexServer.cs with this:
        private static async Task<ConnectionResult> ServerConnectionTestLoop(CancellationToken token)
        {
            int retryCount = 0;
            int minimumDelay = 5; // Minimum delay in seconds
            bool forceShortDelay = false; // Flag to force a short delay if necessary

            // Delay tiers (key is attempt number *before* which this delay applies)
            Dictionary<int, int> DelayTiers = new()
            {
                { 0, minimumDelay },   // Attempts 1-12 (first minute): 5 seconds delay
                { 12, 30 }, // Attempts 13-22 (next 5 mins): 30 seconds delay
                { 22, 60 }, // Attempts 23-32 (next 10 mins): 60 seconds delay
                { 32, 120 } // Attempts 33+ : 120 seconds delay
            };

            WriteWarning("\nConnection lost or failed. Attempting to reconnect...\n");

            while (!token.IsCancellationRequested) // Primary exit condition
            {
                // Secondary check: If another thread reset the global flag, exit.
                lock (_reconnectionLock)
                {
                    if (!_isAttemptingReconnection)
                    {
                        Console.WriteLine("Reconnection loop externally stopped via flag.");
                        return ConnectionResult.Cancelled;
                    }
                }

                try
                {
                    ConnectionResult connectionSuccess = await TestConnectionAsync();
                    // If TestConnectionAsync doesn't return success, fall through to delay and retry.
                    if (connectionSuccess == ConnectionResult.Success)
                    {
                        // Reconnection successful, InitializeMonitoring will be called by the caller (StartServerConnectionTestLoop)
                        forceShortDelay = false; // Reset the flag
                        return ConnectionResult.Success; // Return true on success
                    }
                    else if (connectionSuccess == ConnectionResult.Maintenance)
                    {
                        if (Program.debugMode)
                            Console.WriteLine("Forcing short retry delay due to maintenance mode. Server should be available soon.");
                        forceShortDelay = true;
                    }
                    else
                    {
                        forceShortDelay = false; // Reset the flag
                    }

                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Reconnection attempt cancelled by token during test.");
                    return ConnectionResult.Cancelled; // Return false on cancellation
                }
                catch (Exception ex)
                {
                    // TestConnectionAsync should handle its own specific exceptions,
                    // but catch broader errors here if they occur outside TestConnectionAsync
                    Console.WriteLine($"Unexpected error during reconnection test: {ex.Message}");
                    // Fall through to delay and retry
                }

                // If connection failed or threw non-cancellation exception

                // Determine delay based on retry count
                int delaySeconds;
                if (forceShortDelay)
                {
                    delaySeconds = minimumDelay;
                }
                else
                {
                    delaySeconds = DelayTiers.OrderByDescending(t => t.Key).FirstOrDefault(t => retryCount >= t.Key).Value;
                    if (delaySeconds == 0) delaySeconds = DelayTiers.First().Value; // Fallback for initial state
                }

                Console.WriteLine($"Reconnecting attempt #{retryCount + 1} in {delaySeconds} seconds...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Reconnection delay cancelled.");
                    return ConnectionResult.Cancelled; // Return false on cancellation during delay
                }
                retryCount++;
            }

            // If the loop exits due to cancellation request before connecting
            Console.WriteLine("Reconnection loop cancelled before success.");
            return ConnectionResult.Cancelled;
        }



        // This method is more complex due to the different possible root nodes
        public static async Task<PlexMediaItem> FetchItemAsync(string key)
        {
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}{key}");
                string rawXml = response;
                PlexMediaItem mediaItem = new PlexMediaItem(key:key);

                // We need special handling to determine the media type first
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(response);

                // Find the content node (Video, Track, or Episode)
                XmlNode? mediaNode = doc.SelectSingleNode("//MediaContainer/Video") ??
                   doc.SelectSingleNode("//MediaContainer/Track") ??
                   doc.SelectSingleNode("//MediaContainer/Episode");

                if (mediaNode != null)
                {
                    // Get basic attributes
                    mediaItem.Title = GetAttributeValue(mediaNode, "title");
                    mediaItem.Type = GetAttributeValue(mediaNode, "type");

                    // Determine the correct node type for deserialization
                    string nodeType = mediaNode.Name;

                    // Deserialize the node using XPath to get its media children
                    // Now we use the unified PlexSession class instead of PlexSessionXml
                    PlexSession? plexSession = XmlSerializerHelper.DeserializeXmlNodes<PlexSession>(response, $"//MediaContainer/{nodeType}").FirstOrDefault();

                    if (plexSession != null && plexSession.Media != null)
                    {
                        plexSession.RawXml = rawXml;

                        // We can now directly add the Media objects without conversion
                        mediaItem.Media.AddRange(plexSession.Media);
                    }
                }

                int subtitleCount = mediaItem.GetSubtitleStreams().Count;
                //Console.WriteLine($"Fetched media item: {mediaItem.Title} with {subtitleCount} subtitle streams");

                return mediaItem;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching media item: {ex.Message}");
                return new PlexMediaItem(key: key);
            }
        }

        private static string GetAttributeValue(XmlNode node, string attributeName)
        {
            if (node?.Attributes == null)
                return string.Empty;

            XmlAttribute? attr = node.Attributes[attributeName];
            return attr?.Value ?? string.Empty;
        }

        /// <summary>
        /// Select the subtitle stream for the current playback item (only video).
        /// </summary>
        /// <param name="subtitleStreamID">ID of the subtitle stream from the media object</param>
        /// <param name="mediaType">Media type to take action against (default: video)</param>
        /// <returns>Task representing the asynchronous operation</returns>
        public static async Task<CommandResult> SetSubtitleStreamAsync(string machineID, int subtitleStreamID, string mediaType = "video", ActiveSession? activeSession = null)
        {
            // Simply call the SetStreamsAsync method with only the subtitle stream ID parameter
            return await SetStreamsAsync(
                machineID: machineID,
                subtitleStreamID: subtitleStreamID,
                mediaType: mediaType,
                activeSession: activeSession
                );
        }

        /// <summary>
        /// Select multiple playback streams at once.
        /// </summary>
        /// <param name="audioStreamID">ID of the audio stream from the media object</param>
        /// <param name="subtitleStreamID">ID of the subtitle stream from the media object</param>
        /// <param name="videoStreamID">ID of the video stream from the media object</param>
        /// <param name="mediaType">Media type to take action against (default: video)</param>
        /// <param name="server">The PlexServer instance to send the command through</param>
        /// <returns>Task representing the asynchronous operation</returns>
        public static async Task<CommandResult> SetStreamsAsync(
            string machineID,
            int? audioStreamID = null,
            int? subtitleStreamID = null,
            int? videoStreamID = null,
            string mediaType = "video",
            ActiveSession? activeSession = null
            )
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
            return await SendCommandAsync(machineID: machineID, command: "playback/setStreams", additionalParams: parameters, activeSession:activeSession);
        }

        // Overload
        public static async Task<TimelineMediaContainer?> GetTimelineAsync(PlexPlayer player)
        {
            return await GetTimelineAsync(player.MachineIdentifier, player.Device, player.DirectUrlPath);
        }

        // Overload
        public static async Task<TimelineMediaContainer?> GetTimelineAsync(ActiveSession activeSession)
        {
            return await GetTimelineAsync(activeSession.MachineID, activeSession.DeviceName, activeSession.DirectUrlPath);
        }

        public static async Task<TimelineMediaContainer?> GetTimelineAsync(string machineID, string deviceName, string url)
        {
            // Create headers with machine identifier
            Dictionary<string, string> headers = new()
            {
                { "X-Plex-Target-Client-Identifier", machineID },
                { "X-Plex-Device-Name", deviceName },
            };

            // Build the command URL
            string paramString = "?wait=0";
            string path = $"{url}/player/timeline/poll{paramString}";

            try
            {
                HttpRequestMessage timelineRequest = new HttpRequestMessage(HttpMethod.Get, path);
                timelineRequest = Utils.AddHttpRequestHeaders(timelineRequest, headers);

                TimelineMediaContainer? container = new();
                HttpResponseMessage timelineResponse = await _httpClientShortTimeout.SendAsync(timelineRequest);

                if (timelineResponse.IsSuccessStatusCode)
                {
                    string responseBody = timelineResponse.Content.ReadAsStringAsync().Result;
                    XmlSerializer serializer = new(typeof(TimelineMediaContainer));
                    using StringReader reader = new(responseBody);
                    container = (TimelineMediaContainer?)serializer.Deserialize(reader);
                    return container;
                }
                else
                {
                    // Handle error
                    WriteError($"Error fetching timeline: {timelineResponse.ReasonPhrase}");
                    return null;
                }
            }
            catch (TaskCanceledException ex)
            {
                // Timeouts aren't a critical error so don't emphasize as error
                if (ex.InnerException is TimeoutException)
                {
                    Console.WriteLine($"Timeout fetching timeline from {deviceName} (Player app may have closed or device shut down.)");
                }
                else
                {
                    WriteError($"Error fetching device timeline: {ex.Message}");
                }
                return null;
            }
            // Display other exceptions as errors
            catch (Exception ex)
            {
                WriteError($"Error fetching device timeline: {ex.Message}");
                return null;
            }
        }

        public static async Task<CommandResult> SendCommandAsync(string machineID, string command, bool? sendDirectToDevice = false, Dictionary<string, string>? additionalParams = null, bool needResponse = false, ActiveSession? activeSession = null)
        {
            string mainUrlBase;
            string? retryUrlBase;

            if (sendDirectToDevice == true)
            {
                if (activeSession != null && activeSession.Session != null)
                {
                    mainUrlBase = activeSession.Session.Player.DirectUrlPath;
                    retryUrlBase = _url; // Fallback to the main URL if sending directly fails
                }
                else
                {
                    // If no active session, we can't send directly
                    Console.WriteLine("No active session found to send command directly.");
                    mainUrlBase = _url;
                    retryUrlBase = null;
                }
            }
            else
            {
                mainUrlBase = _url;

                if (activeSession != null && activeSession.Session != null)
                {
                    retryUrlBase = activeSession.Session.Player.DirectUrlPath;
                }
                else
                {
                    // If no active session, we can't send directly
                    retryUrlBase = null;
                }
            }

            // Create headers with machine identifier
            Dictionary<string, string> headers = new()
            {
                { "X-Plex-Target-Client-Identifier", machineID }
            };

            // Build query parameters
            Dictionary<string, object> parameters = [];

            // Add additional parameters if provided
            if (additionalParams != null)
            {
                foreach (KeyValuePair<string, string> param in additionalParams)
                {
                    parameters[param.Key] = param.Value;
                }
            }

            // Add command ID
            parameters["commandID"] = GetNextCommandId();

            // Build the query string using JoinArgs
            string queryString = Utils.JoinArgs(parameters);

            // Create the final command URL
            command = command.Trim('/');
            string mainUrl = $"{mainUrlBase}/player/{command}{queryString}";

            try
            {
                // Send the request
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, mainUrl);
                request = Utils.AddHttpRequestHeaders(request, headers);

                // Send the command and get the response
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                // The rest of the method remains unchanged
                // Check if the response was successful
                if (!response.IsSuccessStatusCode)
                {
                    string statusCode = ((int)response.StatusCode).ToString();
                    string statusName = response.StatusCode.ToString();
                    string errorText = await response.Content.ReadAsStringAsync();
                    errorText = errorText.Replace("\n", " ");
                    string message = $"({statusCode}) {statusName}; URL: {mainUrl} \nError: {errorText}";

                    CommandResult originalResult = new CommandResult(success: false, responseErrorMessage: message, responseXml: null);

                    // Retry by sending the command directly to the device (or the server URL if the function call was to send to device directly)
                    if (retryUrlBase != null)
                    {
                        string retryUrl = $"{retryUrlBase}/player/{command}{queryString}";
                        HttpRequestMessage retryRequest = new HttpRequestMessage(HttpMethod.Get, retryUrl);
                        retryRequest = Utils.AddHttpRequestHeaders(retryRequest, headers);

                        HttpResponseMessage retryResponse = await _httpClient.SendAsync(retryRequest);
                        if (retryResponse.IsSuccessStatusCode)
                        {
                            // Process the XML response from the device
                            string responseData = await retryResponse.Content.ReadAsStringAsync();
                            return new CommandResult(success: true, responseErrorMessage: "", responseXml: ProcessXMLResponse(responseData));
                        }
                        else
                        {
                            // Log the retry failure
                            string retryErrorText = await retryResponse.Content.ReadAsStringAsync();
                            retryErrorText = retryErrorText.Replace("\n", " ");
                            Console.WriteLine($"Retry failed. Error: {retryErrorText}");
                            return originalResult;
                        }
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        //throw new UnauthorizedAccessException(message);
                        Console.WriteLine("Unauthorized Error. Error Message: " + message);
                        return new CommandResult(success: false, responseErrorMessage: message, responseXml: null);
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        //throw new FileNotFoundException(message);
                        Console.WriteLine("Not Found Error. Error Message: " + message);
                        return new CommandResult(success: false, responseErrorMessage: message, responseXml: null);
                    }
                    else
                    {
                        //throw new InvalidOperationException(message);
                        Console.WriteLine("Invalid Operation Error. Error Message: " + message);
                        return new CommandResult(success: false, responseErrorMessage: message, responseXml: null);
                    }
                }

                if (needResponse)
                {
                    // Process the XML response
                    string responseData = await response.Content.ReadAsStringAsync();
                    return new CommandResult(success: true, responseErrorMessage: "", responseXml: ProcessXMLResponse(responseData));
                }
                else
                {
                    return new CommandResult(success: true, responseErrorMessage: "", responseXml: null);
                }

            }
            catch (Exception ex)
            {
                // Log the error and rethrow
                Console.WriteLine($"Error sending command: {ex.Message}");
                return new CommandResult(success: false, responseErrorMessage: "[Exception while sending request, no response error to get]", responseXml: null);
            }
        }

        private static XmlDocument? ProcessXMLResponse(string responseData)
        {
            // Clean XML string and handle potential parsing issues
            responseData = CleanXmlString(responseData);

            // If there's no data, return null
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return null;
            }

            try
            {
                // Parse the XML
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(responseData);
                return xmlDoc;
            }
            catch (XmlException)
            {
                Console.WriteLine("Invalid response from server. Command may have been successful anyway.");
                return null;
            }
        }

        // Helper methods that would be needed
        private static string CleanXmlString(string xml)
        {
            // Implement XML cleaning similar to Python's utils.cleanXMLString
            // This is a simple implementation - you might need to enhance it
            if (string.IsNullOrEmpty(xml))
                return string.Empty;

            // Remove invalid XML characters
            string cleanedXml = new string(xml.Where(c =>
                (c >= 0x0020 && c <= 0xD7FF) ||
                (c >= 0xE000 && c <= 0xFFFD) ||
                c == 0x0009 || c == 0x000A || c == 0x000D).ToArray());

            return cleanedXml;
        }

        // Command ID tracking
        private static int _commandId = 0;
        private static string GetNextCommandId()
        {
            return (++_commandId).ToString();
        }

    } // ------------ End PlexServer Class ------------


} // ------------ End Namespace ------------