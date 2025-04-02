using System;
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

namespace PlexShowSubtitlesOnRewind;

public class PlexNotificationListener : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _plexUrl;
    private readonly string _plexToken;
    private readonly string _filters;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listeningTask;
    private bool _disposedValue;
    private static bool _lastEventStoppedUnexpectedly = false;

    // Event triggered when any Plex notification is received
    public event EventHandler<PlexEventInfo>? NotificationReceived;

    // Specific event for 'playing' state changes
    public event EventHandler<PlexEventInfo>? PlayingNotificationReceived;

    // Optional: Add more specific events if needed (e.g., ActivityNotificationReceived)

    /// <summary>
    /// Initializes a new instance of the PlexNotificationListener class.
    /// </summary>
    /// <param name="plexUrl">The IP address of the Plex server.</param>
    /// <param name="plexToken">The Plex authentication token.</param>
    /// <param name="useHttps">Whether to use HTTPS (default is false).</param>
    /// <param name="notificationFilters">Comma-separated list of events to listen for (e.g., "playing,activity"). Null or empty for all.</param>
    /// 
    public PlexNotificationListener(string plexUrl, string plexToken, bool useHttps = false, string? notificationFilters = "playing")
    {
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // Important for long-running streams
        _plexToken = plexToken;

        _plexUrl = $"{plexUrl}/:/eventsource/notifications";

        _filters = string.IsNullOrWhiteSpace(notificationFilters) ? string.Empty : $"filters={Uri.EscapeDataString(notificationFilters)}";

        // Add required Plex headers
        _httpClient.DefaultRequestHeaders.Add("X-Plex-Token", _plexToken);
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream"); // Crucial for SSE
    }

    /// <summary>
    /// Starts listening for Plex notifications.
    /// </summary>
    public void StartListening()
    {
        if (_listeningTask != null && !_listeningTask.IsCompleted)
        {
            Console.WriteLine("Notification listener is already running.");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        CancellationToken token = _cancellationTokenSource.Token;

        string requestUri = string.IsNullOrWhiteSpace(_filters) ? _plexUrl : $"{_plexUrl}?{_filters}";
        requestUri += (requestUri.Contains("?") ? "&" : "?") + $"X-Plex-Token={Uri.EscapeDataString(_plexToken)}"; // Also pass token in query for SSE

        if (Program.debugMode)
            Console.WriteLine($"Starting Plex notification listener for:\n\t{requestUri.Replace(_plexToken, "[TOKEN]")}"); // Avoid logging token

        _listeningTask = Task.Run(async () => await ListenForEvents(requestUri, token), token);
    }

    /// <summary>
    /// Stops listening for Plex notifications.
    /// </summary>
    public void StopListening()
    {
        if (_cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        Console.WriteLine("Stopping Plex notification listener...");
        _cancellationTokenSource.Cancel();

        try
        {
            // Wait briefly for the task to acknowledge cancellation
            _listeningTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // Expected exception on cancellation
            Console.WriteLine("Listener stopped successfully.");
        }
        catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException or TaskCanceledException))
        {
            // Expected exception(s) on cancellation
            Console.WriteLine("Listener stopped successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception while stopping listener: {ex.Message}");
        }
        finally
        {
            _listeningTask = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            Console.WriteLine("Listener fully stopped.");
        }
    }

    private async Task ListenForEvents(string requestUri, CancellationToken token)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        // Add headers again in case they are needed per-request by some HttpClient handlers/proxies
        request.Headers.TryAddWithoutValidation("X-Plex-Token", _plexToken);
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); // Read headers first
            response.EnsureSuccessStatusCode(); // Throw if non-2xx

            using Stream stream = await response.Content.ReadAsStreamAsync(token);
            using StreamReader reader = new StreamReader(stream);

            // *** Consume the events using await foreach ***
            await foreach (ServerSentEvent serverEvent in ProcessRawEventsAsync(reader, token).WithCancellation(token))
            {
                //Console.WriteLine($"Received Event Type: {serverEvent.Event}");

                try
                {
                    //DeserializeAndHandleEvent(serverEvent);
                    ProcessEvent(serverEvent.Event, serverEvent.Data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"!! Error deserializing/handling event data: {ex.Message}");
                    Console.WriteLine($"   Raw Data: {serverEvent.Data}");
                }
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request error: {ex.Message}");
            // Handle connection errors, invalid token (401), etc.
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Event listening cancelled by user.");
        }
        catch (Exception ex) // Catch other potential errors
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
        }
        finally
        {
            if (_lastEventStoppedUnexpectedly)
            {
                MonitorManager.StopAllMonitoring();
                this.StopListening();
                //this.Dispose(); // StopListening already disposes

                // Try to restart everything
                Console.WriteLine("Attempting to reconnect");
                await PlexServer.StartServerConnectionTestLoop();
            }
        }
    }

    private static async IAsyncEnumerable<ServerSentEvent> ProcessRawEventsAsync(StreamReader reader, [EnumeratorCancellation] CancellationToken token)
    {
        string? currentEvent = null;
        string dataBuffer = string.Empty;
        bool stoppedUnexpectedly = false;

        Console.WriteLine("Connected to Plex event stream. Waiting for events...");

        // Loop indefinitely until cancelled or the stream closes/errors
        while (!token.IsCancellationRequested)
        {
            string? line = null;
            try
            {
                // *** Wrap only the I/O operation in the try-catch ***
                line = await reader.ReadLineAsync(token);

                // If the stream closes from the server side or due to an error detected by ReadLineAsync
                if (line == null)
                {
                    Console.WriteLine("Stream closed by server or read error.");
                    stoppedUnexpectedly = true;
                    break; // Exit the loop
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Event stream processing cancelled.");
                break; // Exit the loop gracefully
            }
            catch (IOException ex)
            {
                stoppedUnexpectedly = true;
                // Handle potential network errors or stream closure issues
                if (ex.InnerException is System.Net.Sockets.SocketException innerEx)
                {
                    if (innerEx.NativeErrorCode == 10054)
                        Console.WriteLine($"Plex Server Closed the Connection (Did it shut down)? - IO Error reading event stream: {ex.InnerException.Message}");
                }
                else if (ex.InnerException?.Message != null)
                {
                    Console.WriteLine($"IO Error reading event stream: {ex.InnerException.Message}");
                }
                else
                {
                    Console.WriteLine($"IO Error reading event stream: {ex.Message}");
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

        _lastEventStoppedUnexpectedly = stoppedUnexpectedly;

        Console.WriteLine("Exiting event processing loop.");
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
            //WriteColor("Received Plex ping.", ConsoleColor.DarkGray); // Can be noisy
        }
        else if (plexEventInfo.EventName.Equals(EventType.Playing))
        {
            PlayingNotificationReceived?.Invoke(this, plexEventInfo);
        }
        else
        {
            Console.WriteLine($"Received Plex Event: {plexEventInfo.EventName}");
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // Stop listening and release managed resources
                StopListening();
                _httpClient.Dispose();
                _cancellationTokenSource?.Dispose(); // Ensure disposal if StopListening didn't complete fully
            }
            // Release unmanaged resources if any
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
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
        if (eventTypeName.Equals(EventType.Ping))
        {
            return null;
        }

        // Deserialize the JSON data based on the event type.
        // We need to get rid of the top-level key, so we'll create a dictionary in each case then get the first value.
        if (deserializeType == typeof(PlayingEvent))
        {
            Dictionary<string, PlayingEvent>? dataDict = JsonSerializer.Deserialize<Dictionary<string, PlayingEvent>>(rawData);
            return dataDict?[dataDict.Keys.First()];
        }
        else if (deserializeType == typeof(ActivityNotification))
        {
            Dictionary<string, ActivityNotification>? dataDict = JsonSerializer.Deserialize<Dictionary<string, ActivityNotification>>(rawData);
            return dataDict?[dataDict.Keys.First()];
        }
        else if (deserializeType == typeof(TranscodeSession))
        {
            Dictionary<string, TranscodeSession>? dataDict = JsonSerializer.Deserialize<Dictionary<string, TranscodeSession>>(rawData);
            return dataDict?[dataDict.Keys.First()];
        }
        else
        {
            Dictionary<string, object>? dataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(rawData);
            return dataDict?[dataDict.Keys.First()];
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
    [JsonPropertyName("sessionKey")] // Map "sessionKey" JSON field to sessionKey property
    public string? sessionKey { get; set; }

    [JsonPropertyName("clientIdentifier")]
    public string? clientIdentifier { get; set; }

    [JsonPropertyName("guid")]
    public string? guid { get; set; }

    [JsonPropertyName("ratingKey")]
    public string? ratingKey { get; set; }

    [JsonPropertyName("url")]
    public string? url { get; set; }

    [JsonPropertyName("key")]
    public string? key { get; set; }

    [JsonPropertyName("viewOffset")]
    public long? viewOffset { get; set; }

    [JsonPropertyName("playQueueID")]
    public int? playQueueID { get; set; }

    [JsonPropertyName("state")]
    public string? state { get; set; }

    [JsonPropertyName("transcodeSession")]
    public string? transcodeSession { get; set; }

    [JsonIgnore]
    public PlayState? playState => GetState(state);

    private static PlayState? GetState(string? state)
    {
        return state?.ToLowerInvariant() switch
        {
            PlexEventStrings.States.Playing => (PlayState?)PlayState.Playing,
            PlexEventStrings.States.Paused => (PlayState?)PlayState.Paused,
            PlexEventStrings.States.Stopped => (PlayState?)PlayState.Stopped,
            _ => (PlayState?)PlayState._Unknown,
        };
    }
}

public static class PlexEventStrings
{
    public class States
    {
        public const string Playing = "playing";
        public const string Paused = "paused";
        public const string Stopped = "stopped";
        public const string Unknown = "unknown"; // For any unrecognized state

        public PlayState ToEnum()
        {
            return this switch
            {
                { } when Equals(Playing, StringComparison.OrdinalIgnoreCase) => PlayState.Playing,
                { } when Equals(Paused, StringComparison.OrdinalIgnoreCase) => PlayState.Paused,
                { } when Equals(Stopped, StringComparison.OrdinalIgnoreCase) => PlayState.Stopped,
                _ => PlayState._Unknown
            };
        }
    }

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
    public const string Ping = "ping"; // Used to keep the connection alive
    public const string Unknown = "unknown"; // For any unrecognized event types

    private string _value;

    private EventType(string value)
    {
        _value = value;
    }

    public static implicit operator EventType(string v)
    {
        return new EventType(v);
    }

    public override string ToString()
    {
        return _value;
    }

    public override bool Equals([NotNullWhen(true)] object? testObj)
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
}

public enum PlayState
{
    Playing,
    Paused,
    Stopped,
    _Unknown, // For any unrecognized state. Not an actual JSON value.
}

// ------------- Activity Event Classes -------------

/// <summary>
/// Represents the 'Context' object within an Activity.
/// Provides additional context for the activity.
/// </summary>
public class Context
{
    public bool? accessible { get; set; }
    public bool? analyzed { get; set; }
    public bool? exists { get; set; }
    public string? key { get; set; }
    public bool? refreshed { get; set; }
}

/// <summary>
/// Represents the 'Activity' object within an ActivityNotification.
/// Contains detailed information about the activity being performed.
/// </summary>
public class Activity
{
    public string? uuid { get; set; }
    public string? type { get; set; }
    public bool? cancellable { get; set; }
    public int? userID { get; set; }
    public string? title { get; set; }
    public string? subtitle { get; set; }
    public int? progress { get; set; } // Progress percentage
    public Context? Context { get; set; }
}

/// <summary>
/// Corresponds to the data payload for the 'activity' event.
/// Contains fields about the activity event.
/// Top-level object in JSON: ActivityNotification
/// </summary>
public class ActivityNotification
{
    public string? @event { get; set; } // Using "@" because 'event' is a reserved keyword in C#
    public string? uuid { get; set; }
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
    public string? key { get; set; }
    public bool? throttled { get; set; }
    public bool? complete { get; set; }
    public double? progress { get; set; }
    public long? size { get; set; } // Use long? as size could potentially be large
    public double? speed { get; set; }
    public bool? error { get; set; }
    public long? duration { get; set; } // Use long? for duration
    public int? remaining { get; set; } // Assuming integer units, could potentially be double?
    public string? context { get; set; }
    public string? sourceVideoCodec { get; set; }
    public string? sourceAudioCodec { get; set; }
    public string? videoDecision { get; set; }
    public string? audioDecision { get; set; }
    public string? protocol { get; set; }
    public string? container { get; set; }
    public string? videoCodec { get; set; }
    public string? audioCodec { get; set; }
    public int? audioChannels { get; set; }
    public int? width { get; set; }
    public int? height { get; set; }
    public bool? transcodeHwRequested { get; set; } // Note: Doc listed this twice, included once.
    public bool? transcodeHwFullPipeline { get; set; }
    public double? timeStamp { get; set; } // Unix timestamp, potentially with fractions
    public double? maxOffsetAvailable { get; set; }
    public double? minOffsetAvailable { get; set; }
}

// ------------- Ping Event -------------
// The 'ping' event has 'data: {}'. No specific class is needed for the data payload itself,
// as it's an empty JSON object. Your handling code would simply check if the 'event' field
// is "ping".

