using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

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
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}/status/sessions");

                // Deserialize the XML response to the MediaContainer
                var mediaContainer = XmlSerializerHelper.DeserializeXml<MediaContainerXml>(response);

                // Convert to your existing PlexSession objects
                List<PlexSession> sessions = new();
                foreach (var sessionXml in mediaContainer.Sessions)
                {
                    sessions.Add(sessionXml.ToPlexSession());
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

        // Using XmlSerializer to get clients
        public async Task<List<PlexClient>> GetClientsAsync()
        {
            try
            {
                string response = await _httpClient.GetStringAsync($"{_url}/clients");

                // Deserialize the XML response to the MediaContainer
                var mediaContainer = XmlSerializerHelper.DeserializeXml<MediaContainerXml>(response);

                // Convert to your existing PlexClient objects
                List<PlexClient> clients = new();
                foreach (var clientXml in mediaContainer.Clients)
                {
                    clients.Add(clientXml.ToPlexClient(httpClient:_httpClient, baseUrl:_url, plexServer:this));
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
                    var plexSessionXml = XmlSerializerHelper.DeserializeXmlNodes<PlexSessionXml>(
                        response, $"//MediaContainer/{nodeType}").FirstOrDefault();

                    if (plexSessionXml != null && plexSessionXml.Media != null)
                    {
                        foreach (var mediaXml in plexSessionXml.Media)
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

        public async Task<XmlDocument?> SendCommandAsync(PlexClient client, string command, bool? proxy = null, Dictionary<string, string>? additionalParams = null)
        {
            // Strip any leading/trailing slashes from command
            command = command.Trim('/');

            // Get the controller from the command
            string controller = command.Split('/')[0];

            // Create headers with machine identifier
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "X-Plex-Target-Client-Identifier", client.MachineIdentifier }
            };

            // Build query parameters
            Dictionary<string, object> parameters = new Dictionary<string, object>();

            // Add additional parameters if provided
            if (additionalParams != null)
            {
                foreach (var param in additionalParams)
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
                foreach (var header in headers)
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
                    string message = $"({statusCode}) {statusName}; {response.RequestMessage.RequestUri} {errorText}";

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new UnauthorizedAccessException(message);
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new FileNotFoundException(message);
                    }
                    else
                    {
                        throw new InvalidOperationException(message);
                    }
                }

                // Get the response data as string
                string responseData = await response.Content.ReadAsStringAsync();

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
            catch (Exception ex)
            {
                // Log the error and rethrow
                Console.WriteLine($"Error sending command: {ex.Message}");
                throw;
            }
        }

        // Helper methods that would be needed
        private string CleanXmlString(string xml)
        {
            // Implement XML cleaning similar to Python's utils.cleanXMLString
            // This is a simple implementation - you might need to enhance it
            if (string.IsNullOrEmpty(xml))
                return string.Empty;

            // Remove invalid XML characters
            var cleanedXml = new string(xml.Where(c =>
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