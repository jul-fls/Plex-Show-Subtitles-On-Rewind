using RewindSubtitleDisplayerForPlex;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RewindSubtitleDisplayerForPlex;
public static class MyLogger // Rename if needed
{
    // --- Configuration ---
    private static readonly string LogFilePath = Path.Combine(BaseConfigsDir, MyStrings.LogFileName);
    private static bool IsFileLoggingEnabled => Program.config?.LogToFile ?? false; // Assumes Program.config exists

    // --- Core Queuing Components ---
    private static readonly BlockingCollection<string> _logQueue = [];
    private static readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    // Update the declaration of _consumerTask to make it nullable.  
    private static Task? _consumerTask;

    // --- Initialization & Shutdown ---
    public static void Initialize() // Only call if logging to file is enabled
    {
        if (_consumerTask != null && !_consumerTask.IsCompleted) return; // Prevent re-initialization
        if (string.IsNullOrEmpty(LogFilePath))
        { 
            throw new InvalidOperationException("LogFilePath must be set.");
        }

        _consumerTask = Task.Run(() => ProcessLogQueue(_cancellationTokenSource.Token));
    }

    public static void Shutdown(TimeSpan? timeout = null)
    {
        _logQueue.CompleteAdding();
        try
        {
            // Wait for the consumer task, default 5 seconds timeout
            _consumerTask?.Wait(timeout ?? TimeSpan.FromSeconds(5));
        }
        catch (AggregateException) { /* Log or handle task exceptions if needed */ }
        finally
        {
            _cancellationTokenSource.Dispose();
            _logQueue.Dispose();
        }
    }

    // --- Logging Method (Producer) ---
    public static void LogToFile(string message)
    {
        if (!IsFileLoggingEnabled || _logQueue.IsAddingCompleted) return;

        string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        try
        {
            // Non-blocking add is preferred for fire-and-forget
            if (!_logQueue.TryAdd(logMessage))
            {
                LogDebug("Log queue is full. Message not added.");
            }
        }
        catch (InvalidOperationException)
        { 
            LogDebug("Log queue is completed. Cannot add more messages.");
        }
        catch (Exception ex)
        {
            // Handle other exceptions if needed
            LogError($"Failed to add log message to queue: {ex.Message}");
        }
    }

    // --- Background File Writer (Consumer) ---
    private static void ProcessLogQueue(CancellationToken cancellationToken)
    {
        EnsureLogFileDirectoryExists(); // Ensure directory exists once

        try
        {
            foreach (var message in _logQueue.GetConsumingEnumerable(cancellationToken))
            {
                try
                {
                    // Append message to file
                    using StreamWriter writer = new StreamWriter(LogFilePath, true);
                    writer.WriteLine(message);
                }
                catch (Exception)
                {
                    // Failed to write this message, log to console or ignore
                    LogError($"Failed to write log message: {message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Task was cancelled, exit gracefully
            LogDebug("Log queue processing was cancelled.");
        }
        catch (Exception)
        {
            // Unhandled exception in consumer task
            LogError("An error occurred while processing the log queue.");
        }
    }

    // --- Helper for Directory Creation ---
    private static void EnsureLogFileDirectoryExists()
    {
        if (string.IsNullOrEmpty(LogFilePath)) return;
        try
        {
            string? logDirectory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
        }
        catch (Exception)
        {
            LogError($"Failed to create log directory: {LogFilePath}");
        }
    }
}