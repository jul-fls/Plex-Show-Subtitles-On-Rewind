using System.ComponentModel;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Serialization;

namespace PlexShowSubtitlesOnRewind
{
    public class PlexServer
    {
        private readonly string _url;
        private readonly string _token;
        private readonly string _appClientID;
        private readonly HttpClient _httpClient;
        private readonly HttpClient _httpClientShortTimeout;

        public PlexServer(string url, string token, string appClientID)
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

        // Using XmlSerializer to get sessions
        public async Task<List<PlexSession>?> GetSessionsAsync(bool printDebug = false, bool shortTimeout = false)
        {
            HttpClient httpClientToUse = shortTimeout ? _httpClientShortTimeout : _httpClient;

            try
            {
                //string response = await httpClientToUse.GetStringAsync($"{_url}/status/sessions");
                string responseString;
                HttpResponseMessage response = await httpClientToUse.GetAsync($"{_url}/status/sessions");

                if (!response.IsSuccessStatusCode)
                {
                    string statusCode = ((int)response.StatusCode).ToString();
                    string statusName = response.StatusCode.ToString();
                    string errorText = await response.Content.ReadAsStringAsync();
                    errorText = errorText.Replace("\n", " ");
                    WriteError($"Error getting sessions: {statusCode} ({statusName}), Error: {errorText}");
                    return null;
                }
                else
                {
                    responseString = await response.Content.ReadAsStringAsync();
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

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                string testUrl = $"{_url}/";
                HttpResponseMessage response = await _httpClient.GetAsync(testUrl);

                if (response.IsSuccessStatusCode)
                {
                    WriteGreen("Connection successful!\n");
                    return true;
                }
                else
                {
                    string statusCode = ((int)response.StatusCode).ToString();
                    string statusName = response.StatusCode.ToString();
                    string errorText = await response.Content.ReadAsStringAsync();
                    errorText = errorText.Replace("\n", " ");
                    Console.WriteLine($"Connection failed. Status Code: {statusCode} ({statusName}), Error: {errorText}\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error testing connection: {ex.Message}\n");
                return false;
            }
        }

        // This method is more complex due to the different possible root nodes
        public async Task<PlexMediaItem> FetchItemAsync(string key)
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
        public async Task<CommandResult> SetSubtitleStreamAsync(string machineID, int subtitleStreamID, string mediaType = "video", ActiveSession? activeSession = null)
        {
            // Simply call the SetStreamsAsync method with only the subtitle stream ID parameter
            return await SetStreamsAsync(
                server: this,
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
            PlexServer server,
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
            return await server.SendCommandAsync(machineID: machineID, command: "playback/setStreams", additionalParams: parameters, activeSession:activeSession);
        }

        // Overload
        public async Task<TimelineMediaContainer?> GetTimelineAsync(PlexPlayer player)
        {
            return await GetTimelineAsync(player.MachineIdentifier, player.Device, player.DirectUrlPath);
        }

        // Overload
        public async Task<TimelineMediaContainer?> GetTimelineAsync(ActiveSession activeSession)
        {
            return await GetTimelineAsync(activeSession.MachineID, activeSession.DeviceName, activeSession.DirectUrlPath);
        }

        public async Task<TimelineMediaContainer?> GetTimelineAsync(string machineID, string deviceName, string url)
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
            catch (Exception ex)
            {
                WriteError($"Error fetching device timeline: {ex.Message}");
                return null;
            }
        }

        public async Task<CommandResult> SendCommandAsync(string machineID, string command, bool? sendDirectToDevice = false, Dictionary<string, string>? additionalParams = null, bool needResponse = false, ActiveSession? activeSession = null)
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
        private int _commandId = 0;
        private string GetNextCommandId()
        {
            return (++_commandId).ToString();
        }

    } // ------------ End PlexServer Class ------------


} // ------------ End Namespace ------------