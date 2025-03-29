//--------------------------- Global Usings ------------------------------------------
global using static PlexShowSubtitlesOnRewind.Utils;
global using static PlexShowSubtitlesOnRewind.GlobalDefinitions;

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

} // ----------- End of Namespace --------------

