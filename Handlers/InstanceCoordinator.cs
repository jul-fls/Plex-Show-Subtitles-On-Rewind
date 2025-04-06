using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RewindSubtitleDisplayerForPlex
{
    // This class creates OS Event Wait Handles and Named Pipes to coordinate between multiple instances of the app as they launch.
    // It ensures that only one instance of the app per server URL is running at a time by checking for existing instances and signaling them to respond with their server URL.
    // ----------------------------------------------------
    //
    // Functionality:
    //
    //   1. New Instance:       Checks for existing instances and signals them to respond with their server URL (via event wait handles with well-known names)
    //   
    //   2. Existing Instance:  Listens for signals from new instances and responds with its server URL (via named pipes of well-known names)
    //                              - The existing instances all try to connect to the same named pipe, so the connections are queued and handled one at a time
    //   
    //   3. Shutdown:           Signals all instances to shut down gracefully. Useful because the app can be launched purely to the background with no GUI/Console
    //                              - Therefore this allows the user to close the app without needing to kill it in Task Manager
    //                              - Signalling is done via the Shutdown event wait handle, which all instances listen for
    //   
    //   4. Cleanup:            Disposes of handles and pipes when no longer needed
    // ----------------------------------------------------
    //
    // Typical Flow:
    //
    //   1. New Instance starts up and triggers the AnyoneHere wait handle event to 'ask' if any existing instances are running
    //   2. OS signals all instances waiting on the AnyoneHere event so they know to respond
    //   3. Existing Instances attempt to connect to the well-known named pipe and send their server URL to the new instance
    //   4. New Instance receives the server URL from the existing instance and checks if it matches its own, and by default closes itself if it does
    //   5. If applicable (if it hasn't closed), new instance goes through the other queued pipe connections and receives their server URLs
    // ----------------------------------------------------

    public static class InstanceCoordinator
    {
        // --- Unique Identifiers ---
        private static readonly string AppNameDashed = MyStrings.AppNameDashed;
        private const string AppGuid = "{391AE04C-125D-11F0-8C20-2A0F2161BBC3}";
        // ----- Event And Pipe Well-Known Names ---
        private static readonly string AnyoneHereEventName = $"Global\\{AppNameDashed}_{AppGuid}_AnyoneHere";
        private static readonly string ShutdownEventName = $"Global\\{AppNameDashed}_{AppGuid}_Shutdown";
        private const string PipeName = $"RewindSubtitleDisplayer_{AppGuid}_InstanceCheckPipe";

        // --- Event Handles ---
        // Event Wait Handles are registered with the OS and can be used for inter-process signaling
        //      - They are basically 'flags' that can be set/reset and seen by other processes to know when a certain event has occurred, and can be handled appropriately
        //      - They cannot be used to pass data, but Named Pipes can be used for that
        private static EventWaitHandle? _anyoneHereEvent;
        private static EventWaitHandle? _shutdownEvent;

        // Named pipes are created elsewhere in the code and are used to pass data between processes
        //      - They can only be used for one-to-one communication, but connections can be queued
        //      - The event handles 

        // --- Configuration ---
        private static readonly TimeSpan ConnectionAttemptTimeout = TimeSpan.FromMilliseconds(1500); // How long New Instance waits for ANY connection after signaling AnyoneHere
        private static readonly TimeSpan OverallCheckTimeout = TimeSpan.FromSeconds(10); // Max time New Instance spends checking

        // --- State for Existing Instances ---
        private static CancellationTokenSource? _eiListenerCts;

        // --- Initialization and Global Handle Management ---
        // In InitializeHandles: Change the EventResetMode for AnyoneHere BACK to ManualReset
        public static bool InitializeHandles()
        {
            try
            {
                // Use ManualReset for AnyoneHere (to signal all) and Shutdown
                _anyoneHereEvent = CreateOrOpenEvent(AnyoneHereEventName, EventResetMode.ManualReset);
                _shutdownEvent = CreateOrOpenEvent(ShutdownEventName, EventResetMode.ManualReset);
                if (_anyoneHereEvent == null || _shutdownEvent == null)
                {
                    throw new InvalidOperationException("Failed to create or open required coordination handles.");
                }
                return true;
            }
            catch (Exception ex)
            {
                LogError($"ERROR: Error creating/opening coordination handles: {ex.Message}.");
                return false;
            }
        }

        private static EventWaitHandle CreateOrOpenEvent(string name, EventResetMode mode)
        {
            EventWaitHandle? handle = null;
            bool createdNew;
            try
            {
                handle = new EventWaitHandle(false, mode, name, out createdNew);
                LogDebug($"Handle '{name}': {(createdNew ? "Created new" : "Opened existing")} (Mode: {mode})"); // Log the mode
                return handle;
            }
            catch (Exception exCreate)
            {
                LogError($"Generic error creating/opening handle '{name}': {exCreate.Message}");
                handle?.Dispose();
                throw;
            }
        }

        // --- New Instance Logic: Check for Duplicate Servers ---
        public static async Task<bool> CheckForDuplicateServersAsync(string myServerUrl, bool allowDuplicates = false)
        {
            if (_anyoneHereEvent == null) { LogError("Coordination handle not initialized."); return false; }

            LogInfo("Checking for other instances monitoring the same server...");
            var respondedPidsThisCheckin = new HashSet<int>();
            var overallStopwatch = Stopwatch.StartNew();
            bool duplicateFound = false;

            // --- Signaling Logic (ManualResetEvent) ---
            try
            {
                // 1. Ensure event is initially reset
                _anyoneHereEvent.Reset();
                LogDebug("Ensured AnyoneHere is Reset.");

                // 2. Signal all waiting Existing Instances
                LogDebug("Signaling AnyoneHere? (Once, ManualReset)");
                _anyoneHereEvent.Set();

                // 3. Wait briefly to allow Existing Instances to wake up
                // Adjust delay if needed, 200-250ms is often sufficient
                int wakeUpDelayMs = 250;
                LogDebug($"Waiting {wakeUpDelayMs}ms for Existing Instances to wake...");
                await Task.Delay(wakeUpDelayMs);

                // 4. Reset the event BEFORE starting pipe listener loop
                // This prevents Existing Instances from re-triggering on the same signal if they loop quickly
                _anyoneHereEvent.Reset();
                LogDebug("Reset AnyoneHere signal after delay.");
            }
            catch (ObjectDisposedException)
            {
                LogError("Error: AnyoneHere event was disposed during signaling phase.");
                return false; // Cannot proceed if handle is bad
            }
            catch (Exception exSignal)
            {
                LogError($"Error during AnyoneHere signaling/reset phase: {exSignal.Message}");
                // Optionally reset again in case of error during delay/reset
                try { _anyoneHereEvent?.Reset(); } catch { }
                // Decide if you can continue or should return false
                // return false; // Safer to abort if signaling failed
            }
            // --- End Signaling Logic ---


            LogDebug($"New Instance: Listening for connections for up to {OverallCheckTimeout.TotalSeconds} seconds...");
            try
            {
                // Loop, attempting to accept connections until timeout or duplicate found (if blocking duplicates)
                while (overallStopwatch.Elapsed < OverallCheckTimeout)
                {
                    // Exit loop immediately if a duplicate is found and we don't allow them
                    if (!allowDuplicates && duplicateFound)
                    {
                        LogDebug("Duplicate found and not allowed, stopping listening.");
                        break;
                    }

                    using var connectionTimeoutCts = new CancellationTokenSource(ConnectionAttemptTimeout);

                    LogDebug($"New Instance: Waiting for next connection attempt (timeout: {ConnectionAttemptTimeout.TotalMilliseconds}ms)...");
                    (int clientPid, string? receivedUrl) = await RunPipeServerCycleAsync(PipeName, connectionTimeoutCts.Token);

                    // Condition: WaitForConnectionAsync timed out - means no Existing Instances (that woke up) are left waiting to connect
                    if (clientPid == -1 && connectionTimeoutCts.IsCancellationRequested)
                    {
                        LogDebug($"No connection attempts within timeout ({ConnectionAttemptTimeout.TotalMilliseconds}ms). Assuming check complete.");
                        break; // Exit the main listening loop
                    }

                    // Condition: Something else cancelled the wait (e.g. overall timeout triggered cancellation source externally)
                    if (connectionTimeoutCts.IsCancellationRequested && clientPid == -1)
                    {
                        LogWarning("CheckForDuplicateServersAsync cancelled while waiting for connection.");
                        break; // Exit the main listening loop
                    }


                    // --- Process a successful connection ---
                    if (clientPid != -1 && receivedUrl != null)
                    {
                        LogDebug($"New Instance received PID {clientPid} and URL '{receivedUrl}'");
                        if (respondedPidsThisCheckin.Add(clientPid)) // True if PID was new for this check cycle
                        {
                            LogDebug($"Received response from new instance PID {clientPid}: {receivedUrl}");
                            if (string.Equals(myServerUrl, receivedUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                LogError($"Duplicate instance (PID {clientPid}) found monitoring the same server: {myServerUrl}");
                                duplicateFound = true;
                                // Loop will break on next iteration if !allowDuplicates
                            }
                        }
                        else
                        {
                            LogDebug($"Ignoring duplicate response from PID {clientPid} in this cycle.");
                        }
                    }
                    else // Handle case where RunPipeServerCycleAsync returned (-1, null) but NOT due to timeout
                    {
                        LogWarning($"Pipe server cycle completed without valid data (PID={clientPid}, URL={receivedUrl}). Client disconnect or internal error?");
                        // Continue listening for other potential clients
                    }
                } // End while loop
            }
            catch (Exception ex)
            {
                LogError($"Error during instance check pipe listening loop: {ex.Message}");
                // Ensure event is reset in case of unexpected exit
                try { _anyoneHereEvent?.Reset(); } catch { }
            }
            finally
            {
                // Event should already be reset from the signaling phase, no reset needed here.
                overallStopwatch.Stop();
                LogDebug($"Instance check loop finished. Duration: {overallStopwatch.Elapsed}");
            }

            if (!duplicateFound) { LogInfo("Duplicate server check complete. No duplicates found."); }
            // Return true if a duplicate was found, false otherwise
            return duplicateFound;
        }


        // --- Pipe Server Logic for ONE Cycle (Run by New Instance) ---
        private static async Task<(int pid, string? url)> RunPipeServerCycleAsync(string pipeName, CancellationToken cancellationToken)
        {
            NamedPipeServerStream? pipeServer = null;
            int clientPid;
            string? clientUrl;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); // Added CurrentUserOnly

                LogDebug($"New Instance: Pipe server created ('{pipeName}'). Waiting for connection...");
                await pipeServer.WaitForConnectionAsync(cancellationToken);

                // Check immediately after wait completes
                if (cancellationToken.IsCancellationRequested)
                {
                    LogDebug("New Instance: Cancellation requested immediately after connection wait.");
                    return (-1, null);
                }
                LogDebug("New Instance: Client connected.");

                // Removed setting pipeServer.ReadTimeout

                // Use using declarations for guaranteed disposal
                using BinaryReader reader = new BinaryReader(pipeServer, Encoding.UTF8, leaveOpen: true);
                using StreamReader sReader = new StreamReader(pipeServer, Encoding.UTF8, leaveOpen: true);

                try
                {
                    clientPid = reader.ReadInt32(); // Read PID first
                                                    // Use ReadLineAsync without token if not available, relies on pipe break/close
                                                    // For .NET versions supporting it, pass the token:
                                                    // clientUrl = await sReader.ReadLineAsync(cancellationToken);
                    clientUrl = await sReader.ReadLineAsync(cancellationToken); // Simpler version

                    LogDebug($"New Instance: Received PID '{clientPid}', URL '{clientUrl ?? "<null>"}'");
                }
                catch (EndOfStreamException)
                { // Client disconnected before sending everything
                    LogDebug("New Instance: Pipe closed by client before receiving expected data.");
                    clientPid = -1; clientUrl = null;
                }
                catch (IOException ioEx)
                { // Other pipe errors during read
                    LogWarning($"New Instance: Pipe IO error during read: {ioEx.Message}");
                    clientPid = -1; clientUrl = null;
                }
                // Let other exceptions propagate for now

                return (clientPid, clientUrl);
            }
            catch (OperationCanceledException)
            {
                LogDebug("New Instance: Pipe connection wait was canceled.");
                return (-1, null);
            }
            catch (IOException ioEx) when (ioEx.Message.Contains("All pipe instances are busy"))
            {
                LogWarning($"New Instance: Pipe server IO error (Instances busy?): {ioEx.Message}"); // Should be less likely now
                return (-1, null);
            }
            catch (IOException ioEx)
            {
                LogWarning($"New Instance: Pipe server IO error: {ioEx.Message}");
                return (-1, null);
            }
            catch (Exception ex)
            {
                LogError($"New Instance: Pipe server unexpected error: {ex.Message}");
                return (-1, null);
            }
            finally
            {
                try { if (pipeServer?.IsConnected ?? false) pipeServer.Disconnect(); } catch { }
                pipeServer?.Dispose();
                LogDebug("New Instance: Pipe server cycle finished, stream disposed.");
            }
        }

        // --- Existing Instance Logic: Listener Task ---
        public static void StartExistingInstanceListener(string myServerUrl, CancellationToken appShutdownToken)
        {
            if (_anyoneHereEvent == null) return; // Handle not ready

            _eiListenerCts = CancellationTokenSource.CreateLinkedTokenSource(appShutdownToken);
            var token = _eiListenerCts.Token;

            Task.Run(async () =>
            {
                LogDebug("Existing Instance: Starting listener for 'AnyoneHere?' signals...");
                var handles = new WaitHandle[] { token.WaitHandle, _anyoneHereEvent };

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        LogDebug("Existing Instance: Waiting for AnyoneHere signal or shutdown...");
                        int signaledIndex = WaitHandle.WaitAny(handles); // Wait indefinitely

                        if (token.IsCancellationRequested || signaledIndex == 0) { break; }

                        if (signaledIndex == 1) // anyoneHereEvent was signaled
                        {
                            LogDebug("Existing Instance: Received 'AnyoneHere' signal. Attempting response...");

                            NamedPipeClientStream? pipeClient = null;
                            try
                            {
                                pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                                LogDebug("Existing Instance: Attempting to connect to New Instance pipe...");
                                // Give slightly less time than New Instance waits for connection attempt to avoid race condition
                                await pipeClient.ConnectAsync((int)(ConnectionAttemptTimeout.TotalMilliseconds * 0.9), token);
                                LogDebug("Existing Instance: Connected to New Instance pipe.");

                                using (var writer = new BinaryWriter(pipeClient, Encoding.UTF8, leaveOpen: true))
                                {
                                    writer.Write(Environment.ProcessId); writer.Flush();
                                }
                                using (var sWriter = new StreamWriter(pipeClient, Encoding.UTF8, leaveOpen: true))
                                {
                                    await sWriter.WriteLineAsync(myServerUrl); await sWriter.FlushAsync();
                                }
                                LogDebug($"Existing Instance: Sent PID {Environment.ProcessId} and URL. Closing pipe.");
                            }
                            catch (OperationCanceledException) { LogWarning("Existing Instance: Connection attempt cancelled."); }
                            catch (TimeoutException) { LogWarning($"Existing Instance: Timeout connecting to New Instance pipe (busy/finished/New Instance exited?)."); } // More likely New Instance finished
                            catch (IOException ioEx) { LogError($"Existing Instance: Pipe IO error connecting/sending: {ioEx.Message}"); }
                            catch (Exception ex) { LogError($"Existing Instance: Error connecting/sending to New Instance pipe: {ex.Message}"); }
                            finally
                            {
                                pipeClient?.Dispose();
                                LogDebug("Existing Instance: Pipe client disposed.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Existing Instance Listener loop error: {ex.Message}");
                        try { await Task.Delay(1000, token); } catch { /* Ignore cancellation */ }
                    }
                }
                LogDebug("Existing Instance Listener task finished.");
            }, token);
        }

        public static void StopExistingInstanceListener() => _eiListenerCts?.Cancel();

        // --- Shutdown Signal Logic (Using Constructor) ---
        public static bool SignalShutdown()
        {
            EventWaitHandle? handle = null;
            bool createdNew;
            try
            {
                handle = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName, out createdNew);
                if (createdNew)
                {
                    LogDebug("No existing instance found to signal shutdown (created new handle)."); return true;
                }
                else
                {
                    LogDebug("Existing instance found. Signaling shutdown..."); handle.Set();
                    LogInfo("Shutdown signal sent successfully."); return true;
                }
            }
            catch (Exception ex) { LogError($"Error during shutdown signaling process for handle '{ShutdownEventName}': {ex.Message}"); return false; }
            finally { handle?.Dispose(); }
        }

        public static WaitHandle GetShutdownWaitHandle() => _shutdownEvent ?? throw new InvalidOperationException("Shutdown handle not initialized.");

        // --- Cleanup ---
        public static void Cleanup()
        {
            LogDebug("Cleaning up coordination handles...");
            _anyoneHereEvent?.Dispose();
            _shutdownEvent?.Dispose();
            _anyoneHereEvent = null;
            _shutdownEvent = null;
            LogDebug("Coordination handles disposed.");
        }

        // --- Logging Placeholders ---
        // Ensure these methods exist and are accessible, e.g., public static in Program or own class
        //private static void LogInfo(string message) => Console.WriteLine($"INFO: {message}");
        //private static void LogDebug(string message) { if (Program.debugMode) Console.WriteLine($"DEBUG: {message}"); }
        //private static void LogWarning(string message) => Console.WriteLine($"WARN: {message}");
        //private static void LogError(string message) => Console.WriteLine($"ERROR: {message}");
    }
}