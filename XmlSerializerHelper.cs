using System.Xml;
using System.Xml.Serialization;

namespace PlexShowSubtitlesOnRewind
{
    public static class XmlSerializerHelper
    {
        /// <summary>
        /// Deserializes XML string to a specific type
        /// </summary>
        public static T? DeserializeXml<T>(string xml) where T : class, new()
        {
            XmlSerializer serializer = new XmlSerializer(typeof(T));

            using StringReader reader = new StringReader(xml);
            return serializer.Deserialize(reader) as T;
        }

        /// <summary>
        /// Deserializes XML string to a specific type with a custom root element name
        /// </summary>
        public static T? DeserializeXml_WithSpecifiedRootElement<T>(string xml, string rootElementName) where T : class, new()
        {
            XmlRootAttribute root = new XmlRootAttribute(rootElementName);
            XmlSerializer serializer = new XmlSerializer(typeof(T), root);

            using StringReader reader = new StringReader(xml);
            return serializer.Deserialize(reader) as T;
        }

        /// <summary>
        /// Extracts specific nodes from XML using XPath and deserializes them
        /// </summary>
        public static List<T> DeserializeXmlNodes<T>(string xml, string xPath) where T : class, new()
        {
            List<T> results = [];

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);

            XmlNodeList? nodes = doc.SelectNodes(xPath);
            if (nodes != null)
            {
                XmlSerializer serializer = new XmlSerializer(typeof(T));

                foreach (XmlNode node in nodes)
                {
                    using StringReader reader = new StringReader(node.OuterXml);
                    if (serializer.Deserialize(reader) is T deserialized)
                    {
                        results.Add(deserialized);
                    }
                }
            }

            return results;
        }
    }

}