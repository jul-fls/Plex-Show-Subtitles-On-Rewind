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
        public async Task<List<PlexSession>> GetSessionsAsync()
        {
            List<PlexSession> sessions = [];

            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}/status/sessions");

                // Deserialize the XML response to the MediaContainer
                MediaContainerXml mediaContainer = XmlSerializerHelper.DeserializeXml<MediaContainerXml>(response);

                // Convert to PlexSession objects
                foreach (PlexSessionXml sessionXml in mediaContainer.Sessions)
                {
                    sessions.Add(sessionXml.ToPlexSession());
                }

                Console.WriteLine($"Found {sessions.Count} active Plex sessions");
                return sessions;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting sessions (Will return what it can): {ex.Message}");
                return sessions;
            }
        }

        // Using XmlSerializer to get clients
        public async Task<List<PlexClient>> GetClientsAsync()
        {
            List<PlexClient> clients = [];
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}/clients");

                // Deserialize the XML response to the MediaContainer
                MediaContainerXml mediaContainer = XmlSerializerHelper.DeserializeXml<MediaContainerXml>(response);

                // Convert to your existing PlexClient objects
                foreach (PlexClientXml clientXml in mediaContainer.Clients)
                {
                    clients.Add(clientXml.ToPlexClient(httpClient: _httpClient, baseUrl: _url, plexServer: this));
                }

                Console.WriteLine($"Found {clients.Count} connected Plex clients");
                return clients;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting clients: {ex.Message}");
                return clients;
            }
        }

        // This method is more complex due to the different possible root nodes
        public async Task<PlexMediaItem> FetchItemAsync(string key)
        {
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}{key}");
                PlexMediaItem mediaItem = new PlexMediaItem { Key = key };

                // We need special handling to determine the media type first
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(response);

                // Find the content node (Video, Track, or Episode)
                XmlNode mediaNode = doc.SelectSingleNode("//MediaContainer/Video") ??
                                   doc.SelectSingleNode("//MediaContainer/Track") ??
                                   doc.SelectSingleNode("//MediaContainer/Episode");

                if (mediaNode != null)
                {
                    // Get basic attributes
                    mediaItem.Title = GetAttributeValue(mediaNode, "title");
                    mediaItem.Type = GetAttributeValue(mediaNode, "type");

                    // Parse media elements
                    List<MediaXml> mediaElements;

                    // Determine the correct node type for deserialization
                    string nodeType = mediaNode.Name;

                    // Deserialize the node using XPath to get its media children
                    PlexSessionXml? plexSessionXml = XmlSerializerHelper.DeserializeXmlNodes<PlexSessionXml>(
                        response, $"//MediaContainer/{nodeType}").FirstOrDefault();

                    if (plexSessionXml != null && plexSessionXml.Media != null)
                    {
                        foreach (MediaXml mediaXml in plexSessionXml.Media)
                        {
                            mediaItem.Media.Add(mediaXml.ToMedia());
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

        private static string GetAttributeValue(XmlNode node, string attributeName)
        {
            if (node?.Attributes == null)
                return string.Empty;

            XmlAttribute attr = node.Attributes[attributeName];
            return attr?.Value ?? string.Empty;
        }

        public async Task<CommandResult> SendCommandAsync(PlexClient client, string command, bool? proxy = null, Dictionary<string, string>? additionalParams = null, bool needResponse = false)
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
                { "X-Plex-Target-Client-Identifier", client.MachineIdentifier }
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
                        return new CommandResult(success:false, responseErrorMessage:message, responseXml:null);
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