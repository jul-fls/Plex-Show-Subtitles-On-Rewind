using System;
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
                    clients.Add(clientXml.ToPlexClient(_httpClient, _url));
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
    }
}