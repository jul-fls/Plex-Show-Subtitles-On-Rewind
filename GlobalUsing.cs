//--------------------------- Global Usings ------------------------------------------
global using static RewindSubtitleDisplayerForPlex.GlobalDefinitions;
global using static RewindSubtitleDisplayerForPlex.Utils;
global using static RewindSubtitleDisplayerForPlex.MyStrings;
global using static RewindSubtitleDisplayerForPlex.LaunchArgs;
using static RewindSubtitleDisplayerForPlex.Program;

#nullable enable

namespace RewindSubtitleDisplayerForPlex
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
        public static readonly string AssemblyFileVersion = Utils.Version.GetVersion();
        public static readonly string LaunchArgsInfo = $"""
            Optional Launch parameters:
                -{LaunchArgs.Background}: {LaunchArgs.Background.Description}
                -{LaunchArgs.Debug}: {LaunchArgs.Debug.Description}
                -{LaunchArgs.Help} or -{LaunchArgs.HelpAlt}: {LaunchArgs.Help.Description}
            """;
        public static string HeadingTitle => $"\n----------- {AppName} - Version {AssemblyFileVersion} -----------\n";
    }

} // ----------- End of Namespace --------------

