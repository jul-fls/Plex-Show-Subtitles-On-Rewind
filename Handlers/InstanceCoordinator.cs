using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace RewindSubtitleDisplayerForPlex
{
    public static class InstanceCoordinator
    {
        // --- Unique Identifiers (REPLACE GUID) ---
        private const string AppGuid = "391AE04C-125D-11F0-8C20-2A0F2161BBC3"; // GUID unique to the app but not to each instance
        private static string InstanceUniqueGUID = Guid.NewGuid().ToString(); // Unique to each instance

        private static Dictionary<string, bool> CheckedInWithInstances = new(); // Track which instances we have checked in with so we don't ask them again

        private const string AnyoneElseEventName = $"Global\\{AppNameDashed}_{AppGuid}_AnyoneElse";
        private const string YesImHereEventName = $"Global\\{AppNameDashed}_{AppGuid}_YesImHere";
        private const string ShutdownEventName = $"Global\\{AppNameDashed}_{AppGuid}_Shutdown"; // For -stop
        private const string PipeName = $"RewindSubtitleDisplayer_{AppGuid}_InstanceCheckPipe";

        // --- Event Handles (Need careful creation/opening) ---
        private static EventWaitHandle? _anyoneElseEvent;
        private static EventWaitHandle? _yesImHereEvent;
        private static EventWaitHandle? _shutdownEvent; // For -stop
        private static EventWaitHandle? _noMoreCheckins; // Know when new instance is done checking in so we can stop ignoring _anyoneElseEvent (in case new instance is created after we check in with the first one)

        // --- Configuration ---
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500); // Timeout for EI connecting to NI pipe
        private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMilliseconds(1000); // Timeout for NI waiting for YesImHere signal
        private static readonly TimeSpan OverallCheckTimeout = TimeSpan.FromSeconds(10); // Max time NI spends checking

        // --- State for EIs ---
        private static CancellationTokenSource? _eiListenerCts;


        // --- Initialization and Global Handle Management ---

        public static bool InitializeHandles()
        {
            // Create/Open ALL handles needed by both NI and EI roles
            // Use ManualResetEvent, initially non-signaled
            try
            {
                _anyoneElseEvent = CreateOrOpenEvent(AnyoneElseEventName);
                _yesImHereEvent = CreateOrOpenEvent(YesImHereEventName);
                _shutdownEvent = CreateOrOpenEvent(ShutdownEventName); // For -stop command
                _noMoreCheckins = CreateOrOpenEvent($"{AppNameDashed}_{AppGuid}_NoMoreCheckins"); // For checking in with other instances
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Fatal error creating/opening coordination handles: {ex.Message}. Cannot proceed.");
                return false;
            }
        }

        private static EventWaitHandle CreateOrOpenEvent(string name)
        {
            EventWaitHandle? handle = null;
            bool createdNew = false;
            try
            {
                // Use the cross-platform constructor. It implicitly opens if 'createdNew' is false.
                handle = new EventWaitHandle(
                    initialState: false,
                    mode: EventResetMode.ManualReset,
                    name: name,
                    createdNew: out createdNew);

                LogDebug($"Handle '{name}': {(createdNew ? "Created new" : "Opened existing")}");
                return handle;
            }
            // Catch specific exceptions that indicate a real problem, NOT just "opened existing".
            catch (WaitHandleCannotBeOpenedException whEx) // Name invalid, or different type exists
            {
                LogError($"Handle '{name}' could not be created/opened. Name invalid, or different sync object type exists? Details: {whEx.Message}");
                throw; // Re-throw - cannot proceed
            }
            catch (IOException ioEx) // Permissions/filesystem issues on Unix
            {
                LogError($"IO error creating/opening handle '{name}' (check permissions/name validity on Linux/macOS?): {ioEx.Message}");
                handle?.Dispose();
                throw;
            }
            catch (UnauthorizedAccessException authEx) // Permissions issues
            {
                LogError($"Unauthorized access creating/opening handle '{name}'. Check process permissions. Details: {authEx.Message}");
                handle?.Dispose();
                throw;
            }
            catch (Exception exCreate) // Catch other potential creation errors
            {
                LogError($"Generic error creating/opening handle '{name}': {exCreate.Message}");
                handle?.Dispose(); // Dispose if partially created
                throw; // Re-throw
            }
            // NOTE: Removed the fallback attempt to call the simple OpenExisting(name) here.
        }

        // --- NI Logic: Check for Duplicate Servers ---
        public static async Task<bool> CheckForDuplicateServersAsync(string myServerUrl)
        {
            if (_anyoneElseEvent == null || _yesImHereEvent == null)
            {
                LogError("Coordination handles not initialized.");
                return false; // Cannot proceed, allow startup
            }

            LogInfo("Checking for other instances monitoring the same server...");
            var reportedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var overallStopwatch = Stopwatch.StartNew();
            bool duplicateFound = false;

            while (!duplicateFound && overallStopwatch.Elapsed < OverallCheckTimeout)
            {
                LogDebug($"Starting check cycle. Elapsed: {overallStopwatch.Elapsed}");
                _yesImHereEvent.Reset(); // Ensure it's reset before we check
                bool heardYes = false;
                Task<string?> pipeTask; // Task to get URL from one EI

                try
                {
                    // Start pipe server for this cycle
                    pipeTask = RunPipeServerCycleAsync(PipeName);

                    // Signal EIs to respond
                    LogDebug("Signaling AnyoneElse?");
                    _anyoneElseEvent.Set();

                    // Wait briefly for *any* EI to signal it's pending
                    LogDebug($"Waiting ({ResponseTimeout.TotalMilliseconds}ms) for YesImHere signal...");
                    heardYes = _yesImHereEvent.WaitOne(ResponseTimeout);
                    LogDebug($"HeardYes = {heardYes}");

                    // Reset AnyoneElse signal for next cycle (or if we exit)
                    _anyoneElseEvent.Reset();
                    LogDebug("Reset AnyoneElse signal.");


                    // Wait for the pipe server task to complete (it accepts one client or times out internally)
                    string? receivedUrl = await pipeTask;

                    if (!string.IsNullOrEmpty(receivedUrl))
                    {
                        LogInfo($"Received Server URL from an existing instance: {receivedUrl}");
                        if (string.Equals(myServerUrl, receivedUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            LogError($"Duplicate instance found monitoring the same server: {myServerUrl}");
                            duplicateFound = true;
                            // No need to check further, exit loop
                        }
                        else
                        {
                            // Optional: Keep track of URLs seen? Not strictly necessary for duplicate check.
                        }
                    }
                    else
                    {
                        LogDebug("Pipe server cycle completed without receiving data.");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error during instance check cycle: {ex.Message}");
                    // Decide how to handle errors - maybe break the loop?
                    break;
                }
                finally
                {
                    // Ensure AnyoneElse is reset even if exceptions occur
                    _anyoneElseEvent?.Reset();
                }


                if (!heardYes)
                {
                    LogInfo("No instances signaled readiness within the timeout. Assuming check is complete.");
                    break; // Exit loop if no one signaled YesImHere
                }

                // If we heard "Yes" but didn't find a duplicate yet, loop again
                LogDebug("Heard 'Yes', continuing check cycle.");
                await Task.Delay(50); // Small delay before next cycle? Optional.
            }

            overallStopwatch.Stop();
            if (!duplicateFound)
            {
                LogInfo("Duplicate server check complete. No duplicates found.");
            }

            return duplicateFound; // True if duplicate was found, false otherwise
        }

        // --- Pipe Server Logic for ONE Cycle (Run by NI) ---
        private static async Task<string?> RunPipeServerCycleAsync(string pipeName)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1, // Only allow one connection for this specific instance
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                LogDebug($"NI: Pipe server created ('{pipeName}'). Waiting for connection...");

                // Wait for a connection (with a timeout slightly longer than ResponseTimeout?)
                await pipeServer.WaitForConnectionAsync(CancellationTokenSource.CreateLinkedTokenSource(new CancellationTokenSource(ResponseTimeout + TimeSpan.FromMilliseconds(500)).Token).Token); // Example timeout

                LogDebug("NI: Client connected to pipe.");

                using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                string? receivedUrl = await reader.ReadLineAsync(); // Read the URL sent by EI
                LogDebug($"NI: Received from pipe: '{receivedUrl ?? "<null>"}'");
                return receivedUrl;
            }
            catch (OperationCanceledException)
            {
                LogDebug("NI: Pipe connection wait timed out or canceled.");
                return null; // Expected if no client connects in time
            }
            catch (IOException ioEx)
            {
                LogError($"NI: Pipe server IO error: {ioEx.Message}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"NI: Pipe server unexpected error: {ex.Message}");
                return null;
            }
            finally
            {
                // Ensure disconnection and disposal
                try { pipeServer?.Disconnect(); } catch { } // Ignore errors on disconnect
                pipeServer?.Dispose();
                LogDebug("NI: Pipe server cycle finished, stream disposed.");
            }
        }


        // --- EI Logic: Listener Task ---
        public static void StartExistingInstanceListener(string myServerUrl, CancellationToken appShutdownToken)
        {
            if (_anyoneElseEvent == null || _yesImHereEvent == null) return; // Handles not ready

            _eiListenerCts = CancellationTokenSource.CreateLinkedTokenSource(appShutdownToken);
            var token = _eiListenerCts.Token;

            Task.Run(async () =>
            {
                LogInfo("EI: Starting listener for 'AnyoneElse?' signals...");
                var handles = new WaitHandle[] { token.WaitHandle, _anyoneElseEvent };
                // Use RegisterWaitForSingleObject or just loop/WaitAny
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        LogDebug("EI: Waiting for AnyoneElse signal or shutdown...");
                        int signaledIndex = WaitHandle.WaitAny(handles); // Wait indefinitely

                        if (token.IsCancellationRequested || signaledIndex == 0)
                        {
                            LogInfo("EI: Shutdown requested or token canceled. Exiting listener.");
                            break;
                        }

                        if (signaledIndex == 1) // anyoneElseEvent was signaled
                        {
                            LogDebug("EI: Received 'AnyoneElse?' signal.");

                            // Signal we are here and will try to connect
                            try
                            {
                                _yesImHereEvent.Set();
                                LogDebug("EI: Signaled 'YesImHere'.");
                            }
                            catch (Exception sigEx)
                            {
                                LogError($"EI: Failed to signal YesImHere: {sigEx.Message}");
                                continue; // Skip connection attempt if signaling failed
                            }

                            // Try connecting to NI's pipe
                            NamedPipeClientStream? pipeClient = null;
                            try
                            {
                                pipeClient = new NamedPipeClientStream(
                                    ".", // Local machine
                                    PipeName,
                                    PipeDirection.Out,
                                    PipeOptions.None); // Or Asynchronous if needed

                                LogDebug("EI: Attempting to connect to NI pipe...");
                                pipeClient.Connect((int)ConnectTimeout.TotalMilliseconds); // Use synchronous connect with timeout

                                LogDebug("EI: Connected to NI pipe. Sending URL.");
                                using (var writer = new StreamWriter(pipeClient, Encoding.UTF8))
                                {
                                    await writer.WriteLineAsync(myServerUrl);
                                    await writer.FlushAsync();
                                }
                                LogDebug("EI: URL sent, closing pipe.");
                            }
                            catch (TimeoutException)
                            {
                                LogWarning("EI: Timeout connecting to NI pipe (might be busy or finished check).");
                            }
                            catch (Exception ex)
                            {
                                LogError($"EI: Error connecting/sending to NI pipe: {ex.Message}");
                            }
                            finally
                            {
                                pipeClient?.Dispose();
                                LogDebug("EI: Pipe client disposed.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"EI Listener loop error: {ex.Message}");
                        // Avoid tight loop on error
                        await Task.Delay(1000, token);
                    }
                }
                LogInfo("EI Listener task finished.");
            }, token);
        }

        public static void StopExistingInstanceListener()
        {
            _eiListenerCts?.Cancel();
        }


        // --- Shutdown Signal Logic ---
        public static bool SignalShutdown()
        {
            EventWaitHandle? handle = null;
            bool createdNew; // Variable to capture the constructor's output

            try
            {
                // Use the cross-platform constructor. It will create the handle if it doesn't exist,
                // or open the existing one if it does.
                handle = new EventWaitHandle(
                    initialState: false,          // Doesn't matter much here
                    mode: EventResetMode.ManualReset, // Should match how EIs wait
                    name: ShutdownEventName,      // The dedicated shutdown event name
                    createdNew: out createdNew);  // Check if we created it or opened existing

                if (createdNew)
                {
                    // We created it, meaning no other instance was running and had created it previously.
                    LogInfo("No running instance found. Shutdown handle created but not signaled.");
                    // No need to Set() as nobody is listening.
                    // The desired end-state (no running instances) is true, so return success.
                    return true;
                }
                else
                {
                    // We opened an existing handle created by a running instance.
                    LogInfo("Running instance found. Signaling shutdown via existing handle...");
                    handle.Set(); // Signal the running instance(s)
                    LogInfo("Shutdown signal sent successfully.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Log any errors during handle creation/opening or signaling
                LogError($"Error during shutdown signaling process for handle '{ShutdownEventName}': {ex.Message}");
                return false; // Indicate failure if an unexpected error occurred
            }
            finally
            {
                // IMPORTANT: The -stop instance MUST dispose of the handle it created or opened.
                handle?.Dispose();
            }
        }

        public static WaitHandle GetShutdownWaitHandle()
        {
            if (_shutdownEvent == null) throw new InvalidOperationException("Shutdown handle not initialized.");
            return _shutdownEvent;
        }

        // --- Cleanup ---
        public static void Cleanup()
        {
            LogDebug("Cleaning up coordination handles...");
            _anyoneElseEvent?.Dispose();
            _yesImHereEvent?.Dispose();
            _shutdownEvent?.Dispose(); // Also dispose shutdown handle
            _anyoneElseEvent = null;
            _yesImHereEvent = null;
            _shutdownEvent = null;
            LogDebug("Coordination handles disposed.");
        }
    }
}