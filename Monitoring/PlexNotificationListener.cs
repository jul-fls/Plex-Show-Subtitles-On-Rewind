using System;
using System.Collections;
using System.Collections.Generic; // Required for Dictionary
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace RewindSubtitleDisplayerForPlex;

// Define the context for the source generator
[JsonSourceGenerationOptions(WriteIndented = true)] // Optional: for debugging output
// Add JsonSerializable attribute for each Dictionary<string, T> type you deserialize
[JsonSerializable(typeof(Dictionary<string, PlayingEvent>))]
[JsonSerializable(typeof(Dictionary<string, ActivityNotification>))]
[JsonSerializable(typeof(Dictionary<string, TranscodeSession>))]
[JsonSerializable(typeof(Dictionary<string, object>))] // For the fallback case
internal partial class PlexEventJsonContext : JsonSerializerContext
{
}

public class PlexNotificationListener : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _plexUrl;
    private readonly string _plexToken;
    private readonly string _filters;
    private readonly CancellationTokenSource _listenerCts;
    private bool _isDisposed;

    // Publicly expose the task for monitoring
    public Task? ListeningTask { get; private set; }

    // Event triggered when any Plex notification is received
    public event EventHandler<PlexEventInfo>? NotificationReceived;
    // Specific event for 'playing' state changes
    public event EventHandler<PlexEventInfo>? PlayingNotificationReceived;
    // Event triggered when the connection is lost/errored
    public event EventHandler? ConnectionLost;
    public PlexNotificationListener(string plexUrl, string plexToken, string? notificationFilters = "playing")
    {
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _plexToken = plexToken;
        _plexUrl = $"{plexUrl}/:/eventsource/notifications";
        _filters = string.IsNullOrWhiteSpace(notificationFilters) ? string.Empty : $"filters={Uri.EscapeDataString(notificationFilters)}";

        _httpClient.DefaultRequestHeaders.Add("X-Plex-Token", _plexToken);
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream");

        _listenerCts = new CancellationTokenSource();
        CancellationToken token = _listenerCts.Token;

        string requestUri = string.IsNullOrWhiteSpace(_filters) ? _plexUrl : $"{_plexUrl}?{_filters}";
        string requestUriNoToken = requestUri; // Store the URI without the token for logging

        // Add the token to the URI if there is one
        if (!string.IsNullOrWhiteSpace(_plexToken))
        {
            // Prepare the parameter part (key=encoded_value)
            string plexParameter = $"X-Plex-Token={Uri.EscapeDataString(_plexToken)}";

            // Check if the URI already has query parameters
            if (requestUri.Contains('?'))
            {
                // If yes, append with an ampersand (&)
                requestUri += "&" + plexParameter;
            }
            else
            {
                // If no, start the query string with a question mark (?)
                requestUri += "?" + plexParameter;
            }
        }

        LogDebug($"Starting Plex notification listener for:\n\t{requestUriNoToken}");

        // Start listening immediately in the constructor
        ListeningTask = Task.Run(async () => await ListenForEvents(requestUri, token), token);
    }


    private async Task ListenForEvents(string requestUri, CancellationToken token)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        // Headers added in constructor via DefaultRequestHeaders

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            using Stream stream = await response.Content.ReadAsStreamAsync(token);
            using StreamReader reader = new StreamReader(stream);

            LogSuccess("Established Plex event stream connection.");

            // Process events until cancellation or stream end/error
            await foreach (ServerSentEvent serverEvent in ProcessRawEventsAsync(reader, token).WithCancellation(token))
            {
                try
                {
                    ProcessEvent(serverEvent.Event, serverEvent.Data);
                }
                catch (JsonException jsonEx)
                {
                    LogError($"Listener: Error deserializing event data: {jsonEx.Message}");
                    if (Program.config.ConsoleLogLevel >= LogLevel.Debug) WriteLineSafe($"   Raw Data: {serverEvent.Data}");
                }
                catch (Exception ex)
                {
                    LogError($"Listener: Error handling event: {ex.Message}");
                    if (Program.config.ConsoleLogLevel >= LogLevel.Debug) WriteLineSafe($"   Raw Data: {serverEvent.Data}");
                }
            }

            LogWarning("Listener: Event stream finished gracefully (unexpected for infinite stream).");
            OnConnectionLost(); // Treat graceful end also as a lost connection for simplicity

        }
        catch (HttpRequestException ex)
        {
            LogError($"Listener: HTTP request error: {ex.Message}");
            OnConnectionLost();
        }
        catch (OperationCanceledException)
        {
            LogWarning("Listener: Event listening cancelled.");
            // Do not trigger ConnectionLost on explicit cancellation
        }
        catch (IOException ioEx)
        {
            LogError($"Listener: IO error (connection likely lost): {ioEx.Message}");
            OnConnectionLost();
        }
        catch (Exception ex)
        {
            LogError($"Listener: An unexpected error occurred: {ex.Message}");
            OnConnectionLost(); // Assume connection lost on any other error
        }

        LogDebug("Listener: Exiting ListenForEvents task.");
    }

    private static async IAsyncEnumerable<ServerSentEvent> ProcessRawEventsAsync(StreamReader reader, [EnumeratorCancellation] CancellationToken token)
    {
        string? currentEvent = null;
        string dataBuffer = string.Empty;

        LogInfo("Connected to Plex event stream. Waiting for events...");

        // Loop indefinitely until cancelled or the stream closes/errors
        while (!token.IsCancellationRequested)
        {
            string? line;
            try
            {
                // *** Wrap only the I/O operation in the try-catch ***
                line = await reader.ReadLineAsync(token);

                // If the stream closes from the server side or due to an error detected by ReadLineAsync
                if (line == null)
                {
                    LogWarning("Stream closed by server or read error.");
                    break; // Exit the loop
                }
            }
            catch (OperationCanceledException)
            {
                LogDebug("Event stream processing cancelled.");
                break; // Exit the loop gracefully
            }
            catch (IOException ex)
            {
                // Handle potential network errors or stream closure issues
                if (ex.InnerException is System.Net.Sockets.SocketException innerEx)
                {
                    if (innerEx.NativeErrorCode == 10054)
                        LogInfo($"Plex Server Closed the Connection (Did it shut down)? - IO Error reading event stream: {ex.InnerException.Message}");
                }
                else if (ex.InnerException?.Message != null)
                {
                    LogWarning($"IO Error reading event stream: {ex.InnerException.Message}");
                }
                else
                {
                    LogWarning($"IO Error reading event stream: {ex.Message}");
                }
                break; // Exit the loop on IO error
            }

            // The yield return must be outside the try-catch block
            if (string.IsNullOrEmpty(line)) // Blank line signifies end of an event
            {
                if (!string.IsNullOrEmpty(currentEvent) /* Allow empty data for ping */)
                {
                    ServerSentEvent serverEvent = new ServerSentEvent { Event = currentEvent, Data = dataBuffer };
                    yield return serverEvent; // Now this is valid
                }
                // Reset for next event
                currentEvent = null;
                dataBuffer = string.Empty;
            }
            else if (line.StartsWith("event:"))
            {
                currentEvent = line.Substring("event:".Length).Trim();
            }
            else if (line.StartsWith("data:"))
            {
                if (!string.IsNullOrEmpty(dataBuffer)) { dataBuffer += "\n"; }
                dataBuffer += line.Substring("data:".Length).Trim();
            }
            // Ignore comment lines (starting with ':') and other lines
        }

        LogDebug("Exiting event processing loop.");
        // No explicit return needed
    }

    private void ProcessEvent(string eventName, string jsonData)
    {
        // This class automatically deserializes the JSON data based on the event type
        PlexEventInfo plexEventInfo = new PlexEventInfo(eventName, jsonData);

        // Raise the general notification event for all successfully received messages
        NotificationReceived?.Invoke(this, plexEventInfo);
        
        // Log ping events separately if desired, or just ignore them
        if (plexEventInfo.EventName.Equals(EventType.Ping))
        {
            LogDebugExtra($"Received Ping From Plex Server.");
        }
        else if (plexEventInfo.EventName.Equals(EventType.Playing))
        {
            PlayingNotificationReceived?.Invoke(this, plexEventInfo);
        }
        else
        {
            LogDebug($"Received Plex Event: {plexEventInfo.EventName}");
        }
    }

    protected virtual void OnConnectionLost()
    {
        ConnectionLost?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                LogDebug("Disposing PlexNotificationListener...");
                // Cancel the listener task
                if (!_listenerCts.IsCancellationRequested)
                {
                    _listenerCts.Cancel();
                }

                // Wait briefly for the task to complete after cancellation
                try
                {
                    // Use Task.WhenAny with a delay to avoid blocking indefinitely
                    Task delayTask = Task.Delay(TimeSpan.FromSeconds(2)); // Short timeout
                    Task completed = Task.WhenAny(ListeningTask ?? Task.CompletedTask, delayTask).Result; // Use .Result carefully or make Dispose async
                    if (completed == delayTask)
                    {
                        LogDebug("Listener task did not complete quickly during dispose.");
                    }
                }
                catch (OperationCanceledException) { /* Expected if task was cancelled */ }
                catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException)) { /* Expected */ }
                catch (Exception ex) { LogError($"Error waiting for listener task during dispose: {ex.Message}"); }


                _listenerCts.Dispose();
                _httpClient.Dispose();
                ListeningTask = null; // Clear the task reference
            }
            _isDisposed = true;
            LogDebug("PlexNotificationListener disposed.");
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

// ---------------- EVENT RESPONSE CLASSES --------------------

public class PlexEventInfo : EventArgs
{
    public EventType EventName { get; }
    public string RawData { get; }
    public Type EventObjType { get; }
    public object? EventObj { get; } // Holds deserialized data like PlaySessionStateNotification

    public PlexEventInfo(string eventTypeName, string rawData)
    {
        EventName = StringToEventTypeEnum(eventTypeName);
        RawData = rawData;
        EventObjType = DetermineDeserializeType(EventName);
        EventObj = ParseToProperType(EventName, EventObjType, rawData);
    }

    private static EventType StringToEventTypeEnum(string eventType)
    {
        return eventType switch
        {
            EventType.Playing => EventType.Playing,
            EventType.Activity => EventType.Activity,
            EventType.TranscodeSessionStart => EventType.TranscodeSessionStart,
            EventType.TranscodeSessionUpdate => EventType.TranscodeSessionUpdate,
            EventType.TranscodeSessionEnd => EventType.TranscodeSessionEnd,
            EventType.Ping => EventType.Ping,
            _ => EventType.Unknown
        };
    }

    private static Type DetermineDeserializeType(EventType eventType)
    {
        Type deserializeType;

        if (eventType.Equals(EventType.Playing))
            deserializeType = typeof(PlayingEvent);
        else if (eventType.Equals(EventType.Activity))
            deserializeType = typeof(ActivityNotification);
        else if (eventType.Equals(EventType.TranscodeSessionStart) ||
                 eventType.Equals(EventType.TranscodeSessionUpdate) ||
                 eventType.Equals(EventType.TranscodeSessionEnd))
            deserializeType = typeof(TranscodeSession);
        else
            deserializeType = typeof(object);

        return deserializeType;
    }

    private static object? ParseToProperType(EventType eventTypeName, Type deserializeType, string rawData)
    {
        // For pings we know it won't have any data
        if (eventTypeName.Equals(EventType.Ping) || string.IsNullOrWhiteSpace(rawData))
        {
            return null;
        }

        // Get the default instance of the source-generated context
        PlexEventJsonContext context = PlexEventJsonContext.Default;
        object? deserializedDictionary;

        try
        {
            // Determine the correct JsonTypeInfo based on the target type
            // and deserialize using the source-generated context
            if (deserializeType == typeof(PlayingEvent))
            {
                deserializedDictionary = JsonSerializer.Deserialize(rawData, context.DictionaryStringPlayingEvent);
            }
            else if (deserializeType == typeof(ActivityNotification))
            {
                deserializedDictionary = JsonSerializer.Deserialize(rawData, context.DictionaryStringActivityNotification);
            }
            else if (deserializeType == typeof(TranscodeSession))
            {
                deserializedDictionary = JsonSerializer.Deserialize(rawData, context.DictionaryStringTranscodeSession);
            }
            else // Fallback for unknown types (if any were expected)
            {
                // Using Dictionary<string, object> with source gen might still have limitations
                // if the 'object' represents completely unknown types at compile time.
                deserializedDictionary = JsonSerializer.Deserialize(rawData, context.DictionaryStringObject);
            }

            // Cast to non-generic IDictionary to access keys/values safely
            IDictionary? dataDict = (IDictionary?)deserializedDictionary;

            // Extract the first value from the dictionary
            if (dataDict != null && dataDict.Keys.Count > 0)
            {
                object? firstKey = dataDict.Keys.Cast<object>().First();
                return dataDict[firstKey];
            }
            else
            {
                LogError($"[{eventTypeName}] SourceGen Deserialization resulted in a null or empty dictionary. Raw Data: {rawData}");
                return null;
            }
        }
        catch (JsonException jsonEx)
        {
            LogError($"[{eventTypeName}] Error deserializing JSON with SourceGen: {jsonEx.Message}. Raw Data: {rawData}");
            return null;
        }
        // Catching NotSupportedException can be useful with source gen if a type wasn't included
        catch (NotSupportedException nse)
        {
            LogError($"[{eventTypeName}] Type not supported by SourceGen context: {nse.Message}. Ensure the required Dictionary<string, T> is in PlexEventJsonContext.");
            return null;
        }
        catch (Exception ex)
        {
            LogError($"[{eventTypeName}] Unexpected error during SourceGen parsing: {ex.Message}. Raw Data: {rawData}");
            return null;
        }
    }
}

public class ServerSentEvent
{
    public required string Event { get; set; }
    public string Data { get; set; } = string.Empty;
}

public class PlayingEvent
{
    [JsonPropertyName("sessionKey")]
    public string? SessionKey { get; set; }

    [JsonPropertyName("clientIdentifier")]
    public string? ClientIdentifier { get; set; }

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("ratingKey")]
    public string? RatingKey { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("viewOffset")]
    public double? ViewOffset { get; set; }

    [JsonPropertyName("playQueueID")]
    public int? PlayQueueID { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("transcodeSession")]
    public string? TranscodeSession { get; set; }

    [JsonIgnore]
    public PlexPlayState? StateEnum => GetState(State);

    private static PlexPlayState? GetState(string? state)
    {
        if (state is string stateStr)
        {
            // Try to parse the string value directly to enum using Enum.TryParse
            // This will handle case-insensitively matching the string to enum names
            if (Enum.TryParse<PlexPlayState>(stateStr, true, out var result))
            {
                return result;
            }

            // If parsing failed, return _Unknown
            return PlexPlayState._Unknown;
        }

        return null; // Return null if state is null
    }
}

public static class PlexEventStrings
{
    public class NotificationNames
    {
        public const string ActivityNotification = "ActivityNotification";
        public const string PlaySessionStateNotification = "PlaySessionStateNotification";
        public const string TranscodeSession = "TranscodeSession";
    }
}

public struct EventType
{
    public const string Playing = "playing";
    public const string Activity = "activity";
    public const string TranscodeSessionStart = "transcodeSession.start";
    public const string TranscodeSessionUpdate = "transcodeSession.update";
    public const string TranscodeSessionEnd = "transcodeSession.end";
    public const string Ping = "ping";
    public const string Unknown = "unknown";

    private readonly string _value;

    private EventType(string value)
    {
        _value = value;
    }

    public static implicit operator EventType(string v)
    {
        return new EventType(v);
    }

    public override readonly string ToString()
    {
        return _value;
    }

    public override readonly bool Equals(object? testObj)
    {
        if (testObj is EventType eventType)
        {
            return string.Equals(_value, eventType._value, StringComparison.OrdinalIgnoreCase);
        }

        if (testObj is string str)
        {
            return string.Equals(_value, str, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public override readonly int GetHashCode()
    {
        return _value.ToLowerInvariant().GetHashCode();
    }
    public static bool operator ==(EventType left, EventType right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EventType left, EventType right)
    {
        return !(left == right);
    }
}

public enum PlexPlayState
{
    Playing,
    Paused,
    Stopped,
    Buffering,
    _Unknown, // For any unrecognized state. Not an actual JSON value.
}

// ------------- Activity Event Classes -------------

/// <summary>
/// Represents the 'Context' object within an Activity.
/// Provides additional context for the activity.
/// </summary>
public class Context
{
    public bool? Accessible { get; set; }
    public bool? Analyzed { get; set; }
    public bool? Exists { get; set; }
    public string? Key { get; set; }
    public bool? Refreshed { get; set; }
}

/// <summary>
/// Represents the 'Activity' object within an ActivityNotification.
/// Contains detailed information about the activity being performed.
/// </summary>
public class Activity
{
    public string? Uuid { get; set; }
    public string? Type { get; set; }
    public bool? Cancellable { get; set; }
    public int? UserID { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public int? Progress { get; set; } // Progress percentage
    public Context? Context { get; set; }
}

/// <summary>
/// Corresponds to the data payload for the 'activity' event.
/// Contains fields about the activity event.
/// Top-level object in JSON: ActivityNotification
/// </summary>
public class ActivityNotification
{
    public string? Event { get; set; } // Using "@" because 'event' is a reserved keyword in C#
    public string? Uuid { get; set; }
    public Activity? Activity { get; set; }
}

// ------------- Transcode Session Event Classes -------------

/// <summary>
/// Corresponds to the data payload for 'transcodeSession.update'
/// and 'transcodeSession.end' events.
/// Contains information about the transcode session.
/// Top-level object in JSON: TranscodeSession
/// </summary>
public class TranscodeSession
{
    public string? Key { get; set; }
    public bool? Throttled { get; set; }
    public bool? Complete { get; set; }
    public double? Progress { get; set; }
    public long? Size { get; set; } // Use long? as size could potentially be large
    public double? Speed { get; set; }
    public bool? Error { get; set; }
    public long? Duration { get; set; } // Use long? for duration
    public int? Remaining { get; set; } // Assuming integer units, could potentially be double?
    public string? Context { get; set; }
    public string? SourceVideoCodec { get; set; }
    public string? SourceAudioCodec { get; set; }
    public string? VideoDecision { get; set; }
    public string? AudioDecision { get; set; }
    public string? Protocol { get; set; }
    public string? Container { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public int? AudioChannels { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool? TranscodeHwRequested { get; set; } // Note: Doc listed this twice, included once.
    public bool? TranscodeHwFullPipeline { get; set; }
    public double? TimeStamp { get; set; } // Unix timestamp, potentially with fractions
    public double? MaxOffsetAvailable { get; set; }
    public double? MinOffsetAvailable { get; set; }
}

// ------------- Ping Event -------------
// The 'ping' event has 'data: {}'. No specific class is needed for the data payload itself,
// as it's an empty JSON object. Your handling code would simply check if the 'event' field
// is "ping".

