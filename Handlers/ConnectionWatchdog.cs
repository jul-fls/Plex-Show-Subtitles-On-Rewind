using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RewindSubtitleDisplayerForPlex
{
    public class ConnectionWatchdog : IDisposable
    {
        private readonly string _plexUrl;
        private readonly string _plexToken;
        private readonly string _appClientId;
        private CancellationTokenSource? _watchdogCts;
        private Task? _watchdogTask;
        private PlexNotificationListener? _currentListener;
        private bool _isDisposed;
        private bool _stopRequested = false;
        private readonly Lock _lock = new Lock();

        // Event to notify when the listener loses connection
        public event EventHandler? ListenerConnectionLost;
        // Event to notify when a 'playing' event is received from the listener
        public event EventHandler<PlexEventInfo>? PlayingNotificationReceived;
        private static readonly CancellationTokenSource _appShutdownCts = new CancellationTokenSource();

        // Constructor
        public ConnectionWatchdog(string plexUrl, string plexToken, string appClientId)
        {
            _plexUrl = plexUrl;
            _plexToken = plexToken;
            _appClientId = appClientId;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_watchdogTask != null && !_watchdogTask.IsCompleted)
                {
                    Console.WriteLine("Connection Watcher is already running.");
                    return;
                }

                _stopRequested = false;
                _watchdogCts?.Dispose(); // Dispose previous CTS if any
                _watchdogCts = new CancellationTokenSource();
                CancellationToken token = _watchdogCts.Token;

                LogDebug("Starting Connection Watcher...");
                _watchdogTask = Task.Run(() => RunWatchdogLoop(token), token);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_watchdogCts == null || _watchdogCts.IsCancellationRequested)
                {
                    return; // Already stopping or stopped
                }
                _stopRequested = true;
                LogDebug("Stopping Connection Watcher...");
                // Cancel the main watchdog loop token *first*.
                _watchdogCts.Cancel();
            }

            // This tells the listener to stop its operations (including cancelling its own task)
            DisposeListener();
            // --------------------------------------------------------------------

            Task? taskToWaitFor = null;
            lock (_lock) // Re-acquire lock briefly to safely get the task reference
            {
                taskToWaitFor = _watchdogTask;
            }

            // Now, wait for the watchdog task. It should exit much faster because the listener it was potentially awaiting is now disposed.
            if (taskToWaitFor != null)
            {
                try
                {
                    // Wait for the task to finish. You might still keep a timeout as a safety net,
                    // or reduce it significantly.
                    bool completedGracefully = taskToWaitFor.Wait(TimeSpan.FromSeconds(5)); // Reduced timeout example
                    if (!completedGracefully)
                    {
                        LogDebug("Connection watcher main task did not finish quickly after listener disposal.");
                    }
                }
                catch (OperationCanceledException) { LogDebug("Task cancelled.", ConsoleColor.DarkGray); }
                catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException)) { LogDebug("Tasks cancelled."); }
                catch (Exception ex)
                {
                    LogError($"Exception waiting for connection watcher task to stop: {ex.Message}");
                }
            }

            // Final cleanup of watchdog resources
            lock (_lock)
            {
                _watchdogTask = null; // Clear task reference
                _watchdogCts?.Dispose();
                _watchdogCts = null;
                LogDebug("Connection watcher stopped.");
            }
        }

        private async Task RunWatchdogLoop(CancellationToken token)
        {
            int delayToAvoidTightLoop = 750; // Milliseconds

            while (!token.IsCancellationRequested && !_stopRequested)
            {
                PlexServer.ConnectionResult connectionResult;
                List<ActiveSession> initialSessions; // To hold sessions for MonitorManager

                try
                {
                    // 1. Ensure Connection
                    LogDebug("Connection Watcher: Testing connection...");
                    connectionResult = await TestAndRetryConnectionAsync(token);

                    if (connectionResult != PlexServer.ConnectionResult.Success)
                    {
                        LogError($"Connection Watcher: Failed to establish connection ({connectionResult}). Exiting connection watcher loop.");
                        break; // Exit loop if connection cannot be established
                    }

                    // --- START: Initialize Monitoring ---
                    LogInfo("Connection Watcher: Connection successful. Initializing monitoring...");
                    try
                    {
                        // Load initial sessions
                        initialSessions = await SessionHandler.ClearAndLoadActiveSessionsAsync();
                        LogInfo($"Connection Watcher: Found {initialSessions.Count} initial session(s).", ConsoleColor.Cyan);

                        // Create monitors for these sessions
                        MonitorManager.CreateAllMonitoringAllSessions(initialSessions, printDebugAll: Program.debugMode);

                        // Start the MonitorManager's polling loop
                        MonitorManager.StartMonitoringLoop();

                    }
                    catch (Exception ex)
                    {
                        LogError($"Connection Watcher: Error during monitoring initialization: {ex.Message}");
                        // Decide how to handle this - maybe retry the loop after a delay?
                        await Task.Delay(delayToAvoidTightLoop, token); // Delay before potentially retrying outer loop
                        continue; // Retry the main loop
                    }
                    // --- END: Initialize Monitoring ---


                    // 2. Start Listener
                    LogDebug("Connection Watcher: Starting listener...");
                    if (!StartNewListener(token))
                    {
                        LogError("Connection Watcher: Failed to start listener even after successful connection test.");
                        await Task.Delay(delayToAvoidTightLoop, token);
                        continue; // Retry the main loop
                    }

                    LogDebug("Connection Watcher: Listener started. Monitoring listener status...");

                    // 3. Monitor Listener
                    if (_currentListener?.ListeningTask != null)
                    {
                        await _currentListener.ListeningTask; // Wait for the listener to complete
                        LogDebug("Connection Watcher: Listener task completed or faulted.");
                        // ConnectionLost event should handle notification if it was an error
                    }
                    else
                    {
                        LogError("Connection Watcher: Listener or its task became null unexpectedly.");
                    }
                }
                catch (OperationCanceledException)
                {
                    LogDebug("Connection watcher loop cancellation requested.");
                    break; // Exit loop on cancellation
                }
                catch (Exception ex)
                {
                    LogError($"Connection Watcher: Unexpected error in main loop: {ex.Message}");
                    // Decide if to retry or break. For now, retry after delay.
                }
                finally
                {
                    // Clean up the listener *before* potentially retrying connection
                    DisposeListener();
                }

                // If the loop didn't break due to cancellation, add a delay before the next cycle
                if (!token.IsCancellationRequested && !_stopRequested)
                {
                    LogDebug("Connection Watcher: Listener stopped or connection lost.");

                    // Stop the monitor manager's loop if the connection is lost
                    MonitorManager.PauseMonitoringManager(); // Ensure monitors are fully stopped //TODO: Make sure the ismonitoring gets re-enabled on reconnection

                    try
                    {
                        await Task.Delay(delayToAvoidTightLoop, token); // Short delay to avoid tight loop
                    }
                    catch (OperationCanceledException)
                    {
                        LogDebug("Connection Watcher: Delay cancelled.");
                        break;
                    }
                }
            }
            LogDebug("Exiting connection watcher main loop.");

            DisposeListener(); // Final cleanup
            MonitorManager.RemoveAllMonitors(); // Ensure monitors are fully stopped on final exit
        }


        private bool StartNewListener(CancellationToken token)
        {
            lock (_lock)
            {
                if (token.IsCancellationRequested) return false;

                DisposeListener(); // Dispose any existing listener first

                try
                {
                    _currentListener = new PlexNotificationListener(_plexUrl, _plexToken, notificationFilters: "playing");
                    _currentListener.ConnectionLost += HandleListenerConnectionLost; // Subscribe internal handler
                    _currentListener.PlayingNotificationReceived += HandlePlayingNotificationReceived; // Forward event

                    // Listener constructor now starts listening immediately. Check ListeningTask.
                    if (_currentListener.ListeningTask == null || _currentListener.ListeningTask.IsFaulted)
                    {
                        LogError("Listener task failed to initialize or faulted immediately.");
                        DisposeListener();
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    LogError($"Failed to create or start PlexNotificationListener: {ex.Message}");
                    DisposeListener();
                    return false;
                }
            }
        }

        private void HandleListenerConnectionLost(object? sender, EventArgs e)
        {
            LogWarning("Connection Watcher: Listener connection lost. Will attempt reconnection.");
            // The main watchdog loop will detect the listener task completion and handle reconnection.
            // We might trigger an external event if needed.
            ListenerConnectionLost?.Invoke(this, EventArgs.Empty);

            // Optionally, clean up the listener reference here immediately
            // DisposeListener(); // Be careful with thread safety if DisposeListener is called elsewhere
        }

        private void HandlePlayingNotificationReceived(object? sender, PlexEventInfo e)
        {
            // Forward the event to external subscribers
            PlayingNotificationReceived?.Invoke(this, e);
        }

        private void DisposeListener()
        {
            lock (_lock)
            {
                if (_currentListener != null)
                {
                    LogDebug("Connection Watcher: Disposing current listener...", ConsoleColor.DarkGray);
                    _currentListener.ConnectionLost -= HandleListenerConnectionLost;
                    _currentListener.PlayingNotificationReceived -= HandlePlayingNotificationReceived;
                    _currentListener.Dispose();
                    _currentListener = null;
                }
            }
        }

        // --- Connection Testing and Retry Logic ---

        private static async Task<PlexServer.ConnectionResult> TestAndRetryConnectionAsync(CancellationToken token)
        {
            PlexServer.ConnectionResult result = await PlexServer.TestConnectionAsync();
            if (result == PlexServer.ConnectionResult.Success)
            {
                return result;
            }

            // If initial test failed, enter retry loop
            LogWarning("Connection Watcher: Initial connection test failed. Entering retry loop...");
            return await ServerConnectionTestLoop(token);
        }


        private static async Task<PlexServer.ConnectionResult> ServerConnectionTestLoop(CancellationToken token)
        {
            int retryCount = 0;
            const int minimumDelay = 5; // Minimum delay in seconds
            bool forceShortDelay = false; // Flag to force a short delay if necessary

            // Delay tiers (key is attempt number *before* which this delay applies)
            Dictionary<int, int> delayTiers = new()
            {
                { 0, minimumDelay },   // Attempts 1-12 (first minute): 5 seconds delay
                { 12, 30 }, // Attempts 13-22 (next 5 mins): 30 seconds delay
                { 22, 60 }, // Attempts 23-32 (next 10 mins): 60 seconds delay
                { 32, 120 } // Attempts 33+ : 120 seconds delay
            };

            // No initial "Connection lost" message here, handled by caller

            while (!token.IsCancellationRequested)
            {
                try
                {
                    PlexServer.ConnectionResult connectionSuccess = await PlexServer.TestConnectionAsync();

                    if (connectionSuccess == PlexServer.ConnectionResult.Success)
                    {
                        LogDebug("Connection Watcher: Reconnection successful!");
                        return PlexServer.ConnectionResult.Success;
                    }
                    else if (connectionSuccess == PlexServer.ConnectionResult.Maintenance)
                    {
                        LogDebug("Connection Watcher: Forcing short retry delay due to maintenance mode.");
                        forceShortDelay = true;
                    }
                    else
                    {
                        forceShortDelay = false; // Reset the flag
                    }
                }
                catch (OperationCanceledException)
                {
                    LogDebug("Connection Watcher: Reconnection attempt cancelled by token during test.");
                    return PlexServer.ConnectionResult.Cancelled;
                }
                catch (Exception ex)
                {
                    LogError($"Connection Watcher: Unexpected error during reconnection test: {ex.Message}");
                    // Fall through to delay and retry
                }

                // Determine delay based on retry count
                int delaySeconds;
                if (forceShortDelay)
                {
                    delaySeconds = minimumDelay;
                }
                else
                {
                    // Find the largest key less than or equal to the current retry count
                    int applicableTierKey = delayTiers.Keys.Where(k => k <= retryCount).DefaultIfEmpty(-1).Max();
                    delaySeconds = applicableTierKey != -1 ? delayTiers[applicableTierKey] : delayTiers.Last().Value; // Use last tier if somehow no key matches
                }


                LogDebug($"Connection Watcher: Reconnecting attempt #{retryCount + 1} in {delaySeconds} seconds...", ConsoleColor.Yellow);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }
                catch (OperationCanceledException)
                {
                    LogDebug("Connection Watcher: Reconnection delay cancelled.");
                    return PlexServer.ConnectionResult.Cancelled;
                }
                retryCount++;
            }

            LogDebug("Connection Watcher: Reconnection loop cancelled before success.");
            return PlexServer.ConnectionResult.Cancelled;
        }


        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    LogDebug("Disposing Connection Watcher...");
                    // Signal stop and wait for the task to complete
                    Stop();
                    _watchdogCts?.Dispose();
                    DisposeListener(); // Ensure listener is cleaned up
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}