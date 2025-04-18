using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RewindSubtitleDisplayerForPlex; // Assuming MyClasses.cs is in this namespace
using static RewindSubtitleDisplayerForPlex.Props;

#nullable enable

namespace RewindSubtitleDisplayerForPlex;

public static class XmlResponseParser
{
    // --- Public Parsing Methods ---

    /// <summary>
    /// Parses the XML response for /status/sessions into a MediaContainer object.
    /// </summary>
    public static SessionMediaContainer? ParseMediaContainer(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            XDocument doc = XDocument.Parse(xml);
            XElement? mediaContainerElement = doc.Root;

            if (mediaContainerElement?.Name != nameof(MediaContainer)) return null; // Or throw

            List<PlexSession?> sessions = mediaContainerElement.Elements(nameof(Video))
                                               .Select(ParsePlexSession)
                                               .Where(s => s != null)
                                               .ToList();

            return new SessionMediaContainer
            {
                // Note: MediaContainer in MyClasses only has Sessions list, no size attribute parsed here.
                Sessions = sessions! // Non-null asserted due to Where clause
            };
        }
        catch (Exception ex)
        {
            LogError($"Error parsing MediaContainer XML: {ex.Message}");
            // Optionally log the XML: WriteLineSafe($"XML: {xml}");
            return null;
        }
    }

    /// <summary>
    /// Parses the XML response for timeline polls into a TimelineMediaContainer object.
    /// </summary>
    public static TimelineMediaContainer? ParseTimelineMediaContainer(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            XDocument doc = XDocument.Parse(xml);
            XElement? mediaContainerElement = doc.Root;

            if (mediaContainerElement?.Name != nameof(MediaContainer)) return null;

            var timelines = mediaContainerElement.Elements(nameof(Timeline))
                                                .Select(ParsePlexTimeline)
                                                .Where(t => t != null)
                                                .ToList();

            return new TimelineMediaContainer
            {
                Size = GetIntAttribute(mediaContainerElement, nameof(size)),
                Timeline = timelines! // Non-null asserted due to Where clause
            };
        }
        catch (Exception ex)
        {
            LogError($"Error parsing Timeline MediaContainer XML: {ex.Message}");
            // Optionally log the XML: WriteLineSafe($"XML: {xml}");
            return null;
        }
    }

    /// <summary>
    /// Parses the XML response for connection tests.
    /// </summary>
    public static PlexServer.ConnectionTestResponse? ParseConnectionTestResponse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            XDocument doc = XDocument.Parse(xml);
            XElement? responseElement = doc.Root;

            if (responseElement?.Name != nameof(Response)) return null;

            return new PlexServer.ConnectionTestResponse
            {
                Code = GetAttributeValue(responseElement, nameof(code)),
                Title = GetAttributeValue(responseElement, nameof(title)),
                Status = GetAttributeValue(responseElement, nameof(status))
            };
        }
        catch (Exception ex)
        {
            LogError($"Error parsing Connection Test Response XML: {ex.Message}");
            // Optionally log the XML: WriteLineSafe($"XML: {xml}");
            return null;
        }
    }

    /// <summary>
    /// Parses the XML response for fetching a media item (/library/metadata/...).
    /// Handles different media types (Video, Track, Episode) under MediaContainer.
    /// </summary>
    public static PlexMediaItem? ParsePlexMediaItem(string xml, string itemKey) // Pass itemKey for constructor
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            XDocument doc = XDocument.Parse(xml);
            XElement? mediaContainerElement = doc.Root;

            if (mediaContainerElement?.Name != nameof(MediaContainer)) return null;

            // Find the main media element (Video, Track, or Episode)
            XElement? mediaNode = mediaContainerElement.Element(nameof(Video)) ??
                                  mediaContainerElement.Element(nameof(Track)) ??
                                  mediaContainerElement.Element(nameof(Episode));

            if (mediaNode == null) return null; // No suitable media node found

            PlexMediaItem mediaItem = new PlexMediaItem(itemKey) // Use the passed key
            {
                Title = GetAttributeValue(mediaNode, nameof(title)),
                Type = GetAttributeValue(mediaNode, nameof(type)) ?? mediaNode.Name.LocalName, // Fallback to node name if 'type' attribute is missing
                                                                                               // Parse nested Media elements
                Media = mediaNode.Elements(nameof(Media))
                                           .Select(ParseMedia) // Use the helper
                                           .Where(m => m != null)
                                           .ToList()! // Non-null asserted
            };

            return mediaItem;
        }
        catch (Exception ex)
        {
            LogError($"Error parsing Plex Media Item XML: {ex.Message}");
            // Optionally log the XML: WriteLineSafe($"XML: {xml}");
            return null;
        }
    }


    // --- Private Helper Parsing Methods ---

    private static PlexSession? ParsePlexSession(XElement videoElement)
    {
        if (videoElement == null || videoElement.Name != nameof(Video)) return null;

        PlexSession session = new PlexSession
        {
            Key = GetAttributeValue(videoElement, nameof(key)),
            RatingKey = GetAttributeValue(videoElement, nameof(ratingKey)),
            SessionKey = GetAttributeValue(videoElement, nameof(sessionKey)),
            Title = GetAttributeValue(videoElement, nameof(title)),
            GrandparentTitle = GetAttributeValue(videoElement, nameof(grandparentTitle)),
            LibrarySectionTitle = GetAttributeValue(videoElement, nameof(librarySectionTitle)),
            Type = GetAttributeValue(videoElement, nameof(type)),
            ViewOffset = GetIntAttribute(videoElement, nameof(viewOffset)),
            // RawXml needs to be set externally if needed
        };

        // Parse nested elements
        XElement? playerElement = videoElement.Element(nameof(Player));
        if (playerElement != null)
        {
            session.Player = ParsePlexPlayer(playerElement) ?? new PlexPlayer(); // Assign or default
        }

        XElement? innerSessionElement = videoElement.Element(nameof(Session));
        if (innerSessionElement != null)
        {
            session.InnerSession = ParsePlexInnerSession(innerSessionElement) ?? new PlexSession.PlexInnerSession(); // Assign or default
        }

        session.Media = videoElement.Elements(nameof(Media))
                                    .Select(ParseMedia)
                                    .Where(m => m != null)
                                    .ToList()!; // Non-null asserted

        return session;
    }

    private static PlexPlayer? ParsePlexPlayer(XElement playerElement)
    {
        if (playerElement == null || playerElement.Name != nameof(Player)) return null;

        return new PlexPlayer
        {
            Title = GetAttributeValue(playerElement, nameof(title)),
            MachineIdentifier = GetAttributeValue(playerElement, nameof(machineIdentifier)),
            Address = GetAttributeValue(playerElement, nameof(address)),
            Device = GetAttributeValue(playerElement, nameof(device)),
            Model = GetAttributeValue(playerElement, nameof(model)),
            Platform = GetAttributeValue(playerElement, nameof(platform)),
            PlatformVersion = GetAttributeValue(playerElement, nameof(platformVersion)),
            PlaybackId = GetAttributeValue(playerElement, nameof(playbackId)),
            PlaybackSessionId = GetAttributeValue(playerElement, nameof(playbackSessionId)),
            Product = GetAttributeValue(playerElement, nameof(product)),
            Profile = GetAttributeValue(playerElement, nameof(profile)),
            State = GetAttributeValue(playerElement, nameof(state)),
            Vendor = GetAttributeValue(playerElement, nameof(vendor)),
            Version = GetAttributeValue(playerElement, nameof(version)),
            Local = GetAttributeValue(playerElement, nameof(local)),
            Relayed = GetAttributeValue(playerElement, nameof(relayed)),
            Secure = GetAttributeValue(playerElement, nameof(secure)),
            UserID = GetAttributeValue(playerElement, nameof(userID))
            // Port and DirectUrlPath are calculated properties
        };
    }

    private static PlexSession.PlexInnerSession? ParsePlexInnerSession(XElement sessionElement)
    {
        if (sessionElement == null || sessionElement.Name != nameof(Session)) return null;

        return new PlexSession.PlexInnerSession
        {
            Id = GetAttributeValue(sessionElement, nameof(id)),
            Bandwidth = GetAttributeValue(sessionElement, nameof(bandwidth)),
            Location = GetAttributeValue(sessionElement, nameof(location))
        };
    }

    private static PlexMedia? ParseMedia(XElement mediaElement)
    {
        if (mediaElement == null || mediaElement.Name != nameof(Media)) return null;

        PlexMedia media = new PlexMedia
        {
            Id = GetAttributeValue(mediaElement, nameof(id)),
            Duration = GetIntAttribute(mediaElement, nameof(duration)),
            VideoCodec = GetAttributeValue(mediaElement, nameof(videoCodec)),
            AudioCodec = GetAttributeValue(mediaElement, nameof(audioCodec)),
            Container = GetAttributeValue(mediaElement, nameof(container)),

            Parts = mediaElement.Elements(nameof(Part))
                                    .Select(ParseMediaPart)
                                    .Where(p => p != null)
                                    .ToList()! // Non-null asserted
        };

        return media;
    }

    private static MediaPart? ParseMediaPart(XElement partElement)
    {
        if (partElement == null || partElement.Name != nameof(Part)) return null;

        MediaPart part = new MediaPart
        {
            Id = GetAttributeValue(partElement, nameof(id)),
            Key = GetAttributeValue(partElement, nameof(key)),
            Duration = GetIntAttribute(partElement, nameof(duration)),
            File = GetAttributeValue(partElement, nameof(file)),

            AllStreams = partElement.Elements(nameof(Props.Stream))
                                        .Select(ParseStreamData)
                                        .Where(s => s != null)
                                        .ToList()! // Non-null asserted
        };
        // Subtitles list is calculated based on AllStreams, no direct parsing needed here.

        return part;
    }

    private static MediaPart.StreamData? ParseStreamData(XElement streamElement)
    {
        if (streamElement == null || streamElement.Name != nameof(Props.Stream)) return null;

        return new MediaPart.StreamData
        {
            Id = GetIntAttribute(streamElement, nameof(id)),
            StreamType = GetIntAttribute(streamElement, nameof(streamType)),
            Index = GetIntAttribute(streamElement, nameof(index)), // Assuming 'index' should be int
            ExtendedDisplayTitle = GetAttributeValue(streamElement, nameof(extendedDisplayTitle)),
            Language = GetAttributeValue(streamElement, nameof(language)),
            SelectedValue = GetAttributeValue(streamElement, nameof(selected)), // Keep as string for bool conversion later if needed
            Format = GetAttributeValue(streamElement, nameof(format)),
            Title = GetAttributeValue(streamElement, nameof(title)),
            Location = GetAttributeValue(streamElement, nameof(location))
        };
    }

    private static PlexTimeline? ParsePlexTimeline(XElement timelineElement)
    {
        if (timelineElement == null || timelineElement.Name != nameof(Timeline)) return null;

        return new PlexTimeline
        {
            ContainerKey = GetAttributeValue(timelineElement, nameof(containerKey)),
            State = GetAttributeValue(timelineElement, nameof(state)),
            Repeat = GetAttributeValue(timelineElement, nameof(repeat)),
            Address = GetAttributeValue(timelineElement, nameof(address)),
            Duration = GetAttributeValue(timelineElement, nameof(duration)),
            SubtitleStreamID = GetAttributeValue(timelineElement, nameof(subtitleStreamID)),
            Key = GetAttributeValue(timelineElement, nameof(key)),
            PlayQueueVersion = GetAttributeValue(timelineElement, nameof(playQueueVersion)),
            Time = GetAttributeValue(timelineElement, nameof(time)),
            MachineIdentifier = GetAttributeValue(timelineElement, nameof(machineIdentifier)),
            Type = GetAttributeValue(timelineElement, nameof(type)),
            Volume = GetAttributeValue(timelineElement, nameof(volume)),
            Controllable = GetAttributeValue(timelineElement, nameof(controllable)),
            RatingKey = GetAttributeValue(timelineElement, nameof(ratingKey)),
            PlayQueueID = GetAttributeValue(timelineElement, nameof(playQueueID)),
            AutoPlay = GetAttributeValue(timelineElement, nameof(autoPlay)),
            SeekRange = GetAttributeValue(timelineElement, nameof(seekRange)),
            Shuffle = GetAttributeValue(timelineElement, nameof(shuffle)),
            PlayQueueItemID = GetAttributeValue(timelineElement, nameof(playQueueItemID)),
            Port = GetAttributeValue(timelineElement, nameof(port)),
            VideoStreamID = GetAttributeValue(timelineElement, nameof(videoStreamID)),
            ProviderIdentifier = GetAttributeValue(timelineElement, nameof(providerIdentifier)),
            Guid = GetAttributeValue(timelineElement, nameof(guid)),
            Protocol = GetAttributeValue(timelineElement, nameof(protocol)),
            SubtitlePosition = GetAttributeValue(timelineElement, nameof(subtitlePosition)),
            AudioStreamID = GetAttributeValue(timelineElement, nameof(audioStreamID))
        };
    }


    // --- XML Parsing Utilities ---

    private static string GetAttributeValue(XElement element, string attributeName, string defaultValue = "")
    {
        return element.Attribute(attributeName)?.Value ?? defaultValue;
    }

    private static int GetIntAttribute(XElement element, string attributeName, int defaultValue = 0)
    {
        string? value = element.Attribute(attributeName)?.Value;
        return int.TryParse(value, out int result) ? result : defaultValue;
    }

    private static string GetElementValue(XElement element, string elementName, string defaultValue = "")
    {
        return element.Element(elementName)?.Value ?? defaultValue;
    }

    private static int GetIntElement(XElement element, string elementName, int defaultValue = 0)
    {
        string? value = element.Element(elementName)?.Value;
        return int.TryParse(value, out int result) ? result : defaultValue;
    }

    private static bool GetBoolAttribute(XElement element, string attributeName, bool defaultValue = false)
    {
        string? value = element.Attribute(attributeName)?.Value;
        // Handle common boolean representations in XML (e.g., "1", "true")
        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        // Try parsing if it's not a known string
        return bool.TryParse(value, out bool result) ? result : defaultValue;
    }

    private static bool GetBoolElement(XElement element, string elementName, bool defaultValue = false)
    {
        string? value = element.Element(elementName)?.Value;
        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return bool.TryParse(value, out bool result) ? result : defaultValue;
    }
}