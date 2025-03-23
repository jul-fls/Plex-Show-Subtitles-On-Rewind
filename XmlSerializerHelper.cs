using System.Xml;
using System.Xml.Serialization;

namespace PlexShowSubtitlesOnRewind
{
    public static class XmlSerializerHelper
    {
        /// <summary>
        /// Deserializes XML string to a specific type
        /// </summary>
        public static T DeserializeXml<T>(string xml) where T : class, new()
        {
            XmlSerializer serializer = new XmlSerializer(typeof(T));

            using StringReader reader = new StringReader(xml);
            return (T)serializer.Deserialize(reader);
        }

        /// <summary>
        /// Deserializes XML string to a specific type with a custom root element name
        /// </summary>
        public static T DeserializeXml<T>(string xml, string rootElementName) where T : class, new()
        {
            XmlRootAttribute root = new XmlRootAttribute(rootElementName);
            XmlSerializer serializer = new XmlSerializer(typeof(T), root);

            using StringReader reader = new StringReader(xml);
            return (T)serializer.Deserialize(reader);
        }

        /// <summary>
        /// Extracts specific nodes from XML using XPath and deserializes them
        /// </summary>
        public static List<T> DeserializeXmlNodes<T>(string xml, string xPath) where T : class, new()
        {
            List<T> results = new();

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);

            XmlNodeList nodes = doc.SelectNodes(xPath);
            if (nodes != null)
            {
                XmlSerializer serializer = new XmlSerializer(typeof(T));

                foreach (XmlNode node in nodes)
                {
                    using StringReader reader = new StringReader(node.OuterXml);
                    results.Add((T)serializer.Deserialize(reader));
                }
            }

            return results;
        }

        /// <summary>
        /// Serializes an object to XML string
        /// </summary>
        public static string SerializeToXml<T>(T obj) where T : class
        {
            XmlSerializer serializer = new XmlSerializer(typeof(T));

            using StringWriter writer = new StringWriter();
            serializer.Serialize(writer, obj);
            return writer.ToString();
        }

        /// <summary>
        /// Creates a wrapper class for XML deserialization when the XML has a container element
        /// </summary>
        public static XmlWrapper<T> CreateWrapper<T>(List<T> items) where T : class
        {
            return new XmlWrapper<T> { Items = items };
        }
    }

    /// <summary>
    /// Generic wrapper class for XML serialization/deserialization
    /// </summary>
    [XmlRoot("MediaContainer")]
    public class XmlWrapper<T>
    {
        [XmlElement("Video", typeof(PlexSession))]
        [XmlElement("Server", typeof(PlexClient))]
        [XmlElement("Media", typeof(Media))]
        [XmlElement("Part", typeof(MediaPart))]
        [XmlElement("Stream", typeof(SubtitleStream))]
        public List<T> Items { get; set; } = new List<T>();
    }
}