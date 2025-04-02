//--------------------------- Global Usings ------------------------------------------
global using static PlexShowSubtitlesOnRewind.GlobalDefinitions;
global using static PlexShowSubtitlesOnRewind.Utils;
global using static PlexShowSubtitlesOnRewind.MyStrings;
global using static PlexShowSubtitlesOnRewind.LaunchArgs;
using static PlexShowSubtitlesOnRewind.Program;

namespace PlexShowSubtitlesOnRewind
{
    public static class GlobalDefinitions
    {
        public static ConsoleColor Green = ConsoleColor.Green;
        public static ConsoleColor Red = ConsoleColor.Red;
        public static ConsoleColor Yellow = ConsoleColor.Yellow;

    }

    // Enum for active/idle state of the monitoring
    public enum MonitoringState
    {
        Active,
        Idle
    }

    // Enum for whether to poll using a timer or wait for events while idle
    public enum PollingMode
    {
        Timer,
        Event
    }

    public static class MyStrings
    {
        public const string AppNameDashed = "Show-Rewind-Subtitles-For-Plex";
        public const string AppName = "Show Rewind Subtitles For Plex";
        public static string LaunchArgsInfo = $"""
            Optional Launch parameters:
                -{LaunchArgs.Background}: {LaunchArgs.Background.Description}
                -{LaunchArgs.Debug}: {LaunchArgs.Debug.Description}
                -{LaunchArgs.Help} or -{LaunchArgs.HelpAlt}: {LaunchArgs.Help.Description}
            """;
        public static string HeadingTitle = $"\n----------- {AppName} -----------\n";
    }

} // ----------- End of Namespace --------------

