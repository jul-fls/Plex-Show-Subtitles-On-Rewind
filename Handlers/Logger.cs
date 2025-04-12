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

    // Log levels
    public enum LogLevel
    {
        Info,
        Verbose,
        Warning,
        Error,
        Debug
    }

    private static void Log(string message, LogLevel level = LogLevel.Info, ConsoleColor? color = null)
    {
        string prefix = level switch
        {
            LogLevel.Warning => "[ WARNING ]  ",
            LogLevel.Verbose => "[ VERBOSE ]  ",
            LogLevel.Info =>    "[  INFO   ]  ",
            LogLevel.Error =>   "[  ERROR  ]  ",
            LogLevel.Debug =>   "[  DEBUG  ]  ",
            _ => "[UNKNOWN] "
        };

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
                Console.ResetColor(); // Reset ahead of time just in case

                if (color is ConsoleColor nonNullColor)
                {
                    Console.ForegroundColor = nonNullColor;
                    Console.WriteLine($"{prefix}{message}");
                }
                else
                {
                    Console.WriteLine($"{prefix}{message}");
                }
            }
            finally // Want to be sure to always reset the color
            {
                Console.ResetColor();
                Console.Write(trailingNewlines); // Print the trailing newlines after the message
            }
        }
    }

    public static void WriteSafe(string message)
    {
        lock (ConsoleWriterLock) // Lock access to the console
        {
            Console.ResetColor(); // Reset ahead of time just in case
            Console.Write(message);
        }
    }

    public static void WriteLineSafe(string message)
    {
        lock (ConsoleWriterLock) // Lock access to the console
        {
            Console.ResetColor(); // Reset ahead of time just in case
            Console.WriteLine(message);
        }
    }

    public static void LogInfo(string message, ConsoleColor? color = null)
    {
        Log(message, LogLevel.Info, color);
    }

    public static void LogDebug(string message, ConsoleColor? color = null)
    {
        // If color is not provided, use dark gray by default
        if (color == null)
        {
            color = ConsoleColor.DarkGray;
        }

        if (_debug)
        {
            Log(message, LogLevel.Debug, color);
        }
    }

    public static void LogWarning(string message, ConsoleColor? color = null)
    {
        // If color is not provided, use yellow by default
        if (color == null)
        {
            color = ConsoleColor.Yellow;
        }
        Log(message, LogLevel.Warning, color);
    }

    public static void LogError(string message, ConsoleColor? color = null)
    {
        // If color is not provided, use red by default
        if (color == null)
        {
            color = ConsoleColor.Red;
        }
        Log(message, LogLevel.Error, color);
    }

    public static void LogVerbose(string message, ConsoleColor? color = null)
    {
        if (_verbose)
        {
            Log(message, LogLevel.Verbose, color);
        }
    }

    public static void LogSuccess(string message)
    {
        Log(message, LogLevel.Info, color:ConsoleColor.Green);
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
        // This is because the background color can be messed up by newline

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
    }
}
