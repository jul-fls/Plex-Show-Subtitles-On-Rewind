using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RewindSubtitleDisplayerForPlex;
internal static class Logger
{
    private static bool _verbose => Program.verboseMode;
    private static bool _debug => Program.debugMode;
    private static readonly Lock ConsoleWriterLock = new Lock();
    private static readonly Lock LogFileLock = new Lock();

    private static readonly string LogFilePath = Path.Combine(Environment.CurrentDirectory, MyStrings.LogFileName);

    // Log levels
    public enum LogLevel
    {
        Info,
        Verbose,
        Warning,
        Error,
        Debug
    }

    // Method to log to file
    public static void LogToFile(string message)
    {
        if (Program.config.LogToFile == false)
        {
            return; // Don't log to file if the setting is disabled
        }

        string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";

        // Ensure the log file is created if it doesn't exist
        if (!File.Exists(LogFilePath))
        {
            try
            {
                File.Create(LogFilePath).Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create log file: {ex.Message}");
                return;
            }
        }
        // Lock access to the log file
        lock (LogFileLock)
        {
            try
            {
                // Append the log message to the file
                using (StreamWriter writer = new StreamWriter(LogFilePath, true))
                {
                    writer.WriteLine(logMessage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }

    private static string GetPrefix(LogLevel level)
    {
        return level switch
        {
            LogLevel.Warning => "[ WARNING ]  ",
            LogLevel.Verbose => "[ VERBOSE ]  ",
            LogLevel.Info => "[  INFO   ]  ",
            LogLevel.Error => "[  ERROR  ]  ",
            LogLevel.Debug => "[  DEBUG  ]  ",
            _ => "[UNKNOWN] "
        };
    }

    private static void Log(string message, LogLevel level, ConsoleColor? logTypeColor, ConsoleColor? messageColor)
    {
        string prefix = GetPrefix(level);

        // If the logTypeColor provided is not default for the log level, 

        // Move any leading newlines prior to the prefix
        if (message.StartsWith("\n"))
        {
            // Get the number of leading newlines
            int leadingNewlines = message.TakeWhile(c => c == '\n').Count();
            // Remove the leading newlines from the message
            message = message.Substring(leadingNewlines);
            // Add the leading newlines to the prefix
            prefix = new string('\n', leadingNewlines) + prefix;
        }

        // Any trailing newlines, store them and print them after the message
        string trailingNewlines = "";
        if (message.EndsWith("\n"))
        {
            // Get the number of trailing newlines
            int trailingNewlineCount = message.Reverse().TakeWhile(c => c == '\n').Count();
            // Remove the trailing newlines from the message
            message = message.Substring(0, message.Length - trailingNewlineCount);
            // Add the trailing newlines to the string
            trailingNewlines = new string('\n', trailingNewlineCount);
        }

        // Any other newlines, add spaces after so it lines up with the prefix
        message = message.Replace("\n", "\n             ");

        lock (ConsoleWriterLock) // Lock access to the console
        {
            try
            {
                Console.ResetColor();

                // Write the prefix
                if (logTypeColor is ConsoleColor nonNullColor)
                {
                    Console.ForegroundColor = nonNullColor;
                    Console.Write($"{prefix}");
                }
                else
                {
                    Console.Write($"{prefix}");
                }

                // Write the message
                if (messageColor is ConsoleColor nonNullMessageColor)
                {
                    Console.ForegroundColor = nonNullMessageColor;
                    Console.WriteLine(message);
                }
                else
                {
                    // Don't reset the color so it inherits the logTypeColor
                    Console.WriteLine(message);
                }
            }
            finally // Want to be sure to always reset the logTypeColor
            {
                Console.ResetColor();
                Console.Write(trailingNewlines); // Print the trailing newlines after the message
            }
        }

        // Log to file
        string logMessage = $"{prefix}{message}";
        logMessage = logMessage.Trim();
        LogToFile(logMessage);
    }

    public static void WriteSafe(string message)
    {
        lock (ConsoleWriterLock) // Lock access to the console
        {
            Console.ResetColor(); // Reset ahead of time just in case
            Console.Write(message);
        }
    }

    public static void WriteLineSafe(string? message = null)
    {
        lock (ConsoleWriterLock) // Lock access to the console
        {
            Console.ResetColor(); // Reset ahead of time just in case
            Console.WriteLine(message);
        }
    }

    public static void LogInfo(string message, ConsoleColor? color = null)
    {
        Log(message, LogLevel.Info, null, color);
    }

    public static void LogDebug(string message, ConsoleColor? messageColor = null)
    {
        if (_debug)
        {
            Log(message, LogLevel.Debug, ConsoleColor.DarkGray, messageColor);
        }
    }

    public static void LogWarning(string message, ConsoleColor? messageColor = null)
    {
        Log(message, LogLevel.Warning, ConsoleColor.Yellow, messageColor);
    }

    public static void LogError(string message, ConsoleColor? messageColor = null)
    {
        Log(message, LogLevel.Error, ConsoleColor.Red, messageColor);
    }

    public static void LogVerbose(string message, ConsoleColor? messageColor = null)
    {
        if (_verbose)
        {
            Log(message, LogLevel.Verbose, null, messageColor);
        }
    }

    // Log success message with green color for both the log type prefix and message
    public static void LogSuccess(string message)
    {
        Log(message, LogLevel.Info, logTypeColor: ConsoleColor.Green, null);
    }

    // ------------------------------- COLOR RELATED ---------------------------------
    public static void WriteErrorSuper(string message, bool noNewLine = false)
    {
        WriteWithBackground(message, ConsoleColor.White, ConsoleColor.DarkRed, noNewLine: noNewLine);
    }

    public static void WriteSuccessSuper(string message, bool noNewLine = false)
    {
        WriteWithBackground(message, ConsoleColor.White, ConsoleColor.DarkGreen, noNewLine: noNewLine);
    }

    public static void WriteWithBackground(string message, ConsoleColor foreground, ConsoleColor? background, bool noNewLine = false)
    {
        // If there are any newlines in the message, split it and write each line separately.
        // If there are trailing or leading newlines, write them separately not colored.
        // This is because the background logTypeColor can be messed up by newline

        string[] lines = Regex.Split(message, @"(\r\n|\r|\n)");

        foreach (string line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }
            else if (line.Trim('\n').Length > 0)
            {
                WriteColor(message: line, foreground: foreground, background: background, noNewline: true);
            }
            else
            {
                lock (ConsoleWriterLock) // Lock access to the console
                {
                    Console.WriteLine(); // Write a newline at the end because we've been using noNewline:true
                }

            }
        }

        if (noNewLine == false)
        {
            lock (ConsoleWriterLock)
            {
                Console.WriteLine(); // Write a newline at the end because we've been using noNewline:true
            }
        }
    }

    public static void WriteYellow(string message, bool noNewline = false)
    {
        WriteColor(message: message, foreground: ConsoleColor.Yellow, noNewline: noNewline);
    }

    public static void WriteGreen(string message, bool noNewline = false)
    {
        WriteColor(message: message, foreground: ConsoleColor.Green, noNewline: noNewline);
    }

    public static void WriteRed(string message, bool noNewline = false)
    {
        WriteColor(message: message, foreground: ConsoleColor.Red, noNewline: noNewline);
    }

    public static void WriteRedSuper(string message, bool noNewline=false)
    {
        WriteWithBackground(message: message, foreground: ConsoleColor.White, background: ConsoleColor.DarkRed, noNewLine: noNewline);
    }

    public static void WriteColorParts(string msg1, string msg2, ConsoleColor? foreground1 = null, ConsoleColor? foreground2 = null)
    {
        lock (ConsoleWriterLock)
        {
            try
            {
                Console.ResetColor();

                // First Part
                if (foreground1 is ConsoleColor nonNullColor1)
                    Console.ForegroundColor = nonNullColor1;

                Console.Write(msg1);

                // Second Part
                if (foreground2 is ConsoleColor nonNullColor2)
                    Console.ForegroundColor = nonNullColor2;
                else
                    Console.ResetColor();

                Console.Write(msg2);
            }
            finally
            {
                Console.ResetColor();
            }
        }

        // Log to file
        string logMessage = $"{msg1}{msg2}";
        LogToFile(logMessage);
    }

    public static void WriteColor(string message, ConsoleColor foreground, ConsoleColor? background = null, bool noNewline = false)
    {
        lock (ConsoleWriterLock) // Lock access to the console
        {
            try
            {
                Console.ResetColor(); // Reset ahead of time just in case

                Console.ForegroundColor = foreground;
                if (background != null)
                    Console.BackgroundColor = background.Value;

                if (noNewline)
                    Console.Write(message);
                else
                    Console.WriteLine(message);
            }
            finally
            {
                Console.ResetColor();
            }
        }

        LogToFile(message);
    }
}
