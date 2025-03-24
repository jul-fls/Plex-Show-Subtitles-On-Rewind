using System.Xml;

namespace PlexShowSubtitlesOnRewind
{
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

        // Using XmlSerializer to get sessions
        public async Task<List<PlexSession>> GetSessionsAsync(bool printDebug = false)
        {
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}/status/sessions");

                // Deserialize directly to your model
                MediaContainer container = XmlSerializerHelper.DeserializeXml<MediaContainer>(response);

                // Store raw XML if needed
                foreach (PlexSession session in container.Sessions)
                {
                    session.RawXml = response;
                }

                if (printDebug)
                    Console.WriteLine($"Found {container.Sessions.Count} active Plex sessions");

                return container.Sessions;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting sessions (Will return what it can): {ex.Message}");
                return [];
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
                    Console.WriteLine("Connection successful!\n");
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
            string rawXml = "";
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}{key}");
                rawXml = response;
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
                Console.WriteLine($"Fetched media item: {mediaItem.Title} with {subtitleCount} subtitle streams");

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
        public async Task<CommandResult> SetSubtitleStreamAsync(string machineID, int subtitleStreamID, string mediaType = "video")
        {
            // Simply call the SetStreamsAsync method with only the subtitle stream ID parameter
            return await SetStreamsAsync(
                server: this,
                machineID: machineID,
                subtitleStreamID: subtitleStreamID,
                mediaType: mediaType);
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
            return await server.SendCommandAsync(machineID: machineID, command: "playback/setStreams", additionalParams: parameters);
        }

        public async Task<CommandResult> SendCommandAsync(string machineID, string command, bool? proxy = null, Dictionary<string, string>? additionalParams = null, bool needResponse = false)
        {
            // Strip any leading/trailing slashes from command
            command = command.Trim('/');

            // Get the controller from the command
            //string controller = command.Split('/')[0];

            if (proxy == null || proxy == false)
            {
                // TODO: Maybe implement 'proxy through server' functionality like in the python API
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
            string url = $"{_url}/player/{command}{queryString}";

            try
            {
                // Send the request
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

                // Add headers
                foreach (KeyValuePair<string, string> header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }

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
                    string message = $"({statusCode}) {statusName}; URL: {url} \nError: {errorText}";

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
            #pragma warning disable IDE0305 // Simplify collection initialization
            string cleanedXml = new string(xml.Where(c =>
                (c >= 0x0020 && c <= 0xD7FF) ||
                (c >= 0xE000 && c <= 0xFFFD) ||
                c == 0x0009 || c == 0x000A || c == 0x000D).ToArray());
            #pragma warning restore IDE0305

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