using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic; // Required for Dictionary

namespace PlexShowSubtitlesOnRewind
{
    // Define classes to represent the JSON data structure for Plex events
    // Based on the documentation provided.

    public class PlaySessionStateNotification
    {
        public string? sessionKey { get; set; }
        public string? clientIdentifier { get; set; }
        public string? guid { get; set; }
        public string? ratingKey { get; set; }
        public string? url { get; set; }
        public string? key { get; set; }
        public long? viewOffset { get; set; }
        public int? playQueueID { get; set; }
        public string? state { get; set; } // playing, paused, stopped
        public string? transcodeSession { get; set; }
    }

    public class PlexEventArgs : EventArgs
    {
        public string EventType { get; }
        public string RawData { get; }
        public object? ParsedData { get; } // Holds deserialized data like PlaySessionStateNotification

        public PlexEventArgs(string eventType, string rawData, object? parsedData = null)
        {
            EventType = eventType;
            RawData = rawData;
            ParsedData = parsedData;
        }
    }

    public class PlexNotificationListener : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _plexUrl;
        private readonly string _plexToken;
        private readonly string _filters;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _listeningTask;
        private bool _disposedValue;

        // Event triggered when any Plex notification is received
        public event EventHandler<PlexEventArgs>? NotificationReceived;

        // Specific event for 'playing' state changes
        public event EventHandler<PlexEventArgs>? PlayingNotificationReceived;

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
            var token = _cancellationTokenSource.Token;

            string requestUri = string.IsNullOrWhiteSpace(_filters) ? _plexUrl : $"{_plexUrl}?{_filters}";
            requestUri += (requestUri.Contains("?") ? "&" : "?") + $"X-Plex-Token={Uri.EscapeDataString(_plexToken)}"; // Also pass token in query for SSE


            Console.WriteLine($"Starting Plex notification listener for: {requestUri.Replace(_plexToken, "***TOKEN***")}"); // Avoid logging token

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
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                    // Add headers again in case they are needed per-request by some HttpClient handlers/proxies
                    request.Headers.TryAddWithoutValidation("X-Plex-Token", _plexToken);
                    request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");


                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); // Read headers first
                    response.EnsureSuccessStatusCode(); // Throw if non-2xx

                    using var stream = await response.Content.ReadAsStreamAsync(token);
                    using var reader = new StreamReader(stream);

                    string? currentEvent = null;
                    string dataBuffer = string.Empty;

                    Console.WriteLine("Connected to Plex event stream. Waiting for events...");

                    while (!reader.EndOfStream && !token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(token);

                        if (string.IsNullOrEmpty(line)) // Blank line signifies end of an event
                        {
                            if (!string.IsNullOrEmpty(currentEvent) && !string.IsNullOrEmpty(dataBuffer))
                            {
                                ProcessEvent(currentEvent, dataBuffer);
                            }
                            currentEvent = null;
                            dataBuffer = string.Empty;
                        }
                        else if (line.StartsWith("event:"))
                        {
                            currentEvent = line.Substring("event:".Length).Trim();
                        }
                        else if (line.StartsWith("data:"))
                        {
                            // Append data, potentially across multiple lines if Plex ever uses that SSE feature
                            dataBuffer += line.Substring("data:".Length).Trim();
                        }
                        // Ignore comment lines (starting with ':') and other lines
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine($"ERROR: Plex connection failed - Unauthorized (401). Check Plex Token. Details: {ex.Message}");
                    // Stop trying if unauthorized
                    StopListening(); // Trigger stop logic
                    break; // Exit the loop
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Listener cancellation requested.");
                    break; // Exit loop if cancellation was requested
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR listening for Plex events: {ex.Message}");
                    // Optional: Implement retry logic with backoff
                    if (!token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(15), token); // Wait before retrying
                        Console.WriteLine("Attempting to reconnect...");
                    }
                }
            }
            Console.WriteLine("Exited event listening loop.");
        }

        private void ProcessEvent(string eventType, string jsonData)
        {
            // Raise the generic event first
            PlexEventArgs? eventArgs = null;

            try
            {
                object? parsedData = null;
                // Attempt to parse specific known event types
                if (eventType.Equals("playing", StringComparison.OrdinalIgnoreCase))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var playData = JsonSerializer.Deserialize<Dictionary<string, PlaySessionStateNotification>>(jsonData, options);

                    if (playData != null && playData.TryGetValue("PlaySessionStateNotification", out var notification))
                    {
                        parsedData = notification;
                        eventArgs = new PlexEventArgs(eventType, jsonData, parsedData);
                        // Raise the specific 'playing' event
                        PlayingNotificationReceived?.Invoke(this, eventArgs);
                    }

                }
                // Add else if blocks here to parse other event types (activity, transcodeSession.update, etc.) if needed
                // else if (eventType.Equals("activity", StringComparison.OrdinalIgnoreCase)) { ... }

                // If eventArgs wasn't created above (e.g., unknown type or parsing failure), create a basic one
                eventArgs ??= new PlexEventArgs(eventType, jsonData);


                // Raise the general notification event for all successfully received messages
                NotificationReceived?.Invoke(this, eventArgs);

                // Log ping events separately if desired, or just ignore them
                if (eventType.Equals("ping", StringComparison.OrdinalIgnoreCase))
                {
                    // Console.WriteLine("Received Plex ping."); // Can be noisy
                }
                else
                {
                    Console.WriteLine($"Received Plex Event: {eventType}"); // Log non-ping events
                }

            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"ERROR deserializing JSON for event '{eventType}': {jsonEx.Message}");
                Console.WriteLine($"Raw Data: {jsonData}");
                // Still raise the generic event but with null ParsedData
                eventArgs = new PlexEventArgs(eventType, jsonData);
                NotificationReceived?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR processing event '{eventType}': {ex.Message}");
                // Still raise the generic event but with null ParsedData
                eventArgs = new PlexEventArgs(eventType, jsonData);
                NotificationReceived?.Invoke(this, eventArgs);
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
}