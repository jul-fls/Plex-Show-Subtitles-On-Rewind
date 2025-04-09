//--------------------------- Global Usings ------------------------------------------
global using static RewindSubtitleDisplayerForPlex.GlobalDefinitions;
global using static RewindSubtitleDisplayerForPlex.Utils;
global using static RewindSubtitleDisplayerForPlex.MyStrings;
global using static RewindSubtitleDisplayerForPlex.LaunchArgs;
global using static RewindSubtitleDisplayerForPlex.Logger;
using static RewindSubtitleDisplayerForPlex.Program;

#nullable enable

namespace RewindSubtitleDisplayerForPlex
{
    public static class GlobalDefinitions
    {
        public const ConsoleColor Green = ConsoleColor.Green;
        public const ConsoleColor Red = ConsoleColor.Red;
        public const ConsoleColor Yellow = ConsoleColor.Yellow;

    }

    // Enum for active/idle state of the monitoring
    public enum MonitoringState
    {
        Active,
        Idle
    }

    public static class MyStrings
    {
        public const string AppNameDashed = "Rewind-Subtitle-Displayer-For-Plex";
        public const string AppName = "Rewind Subtitle Displayer For Plex";
        public const string AppNameShort = "Rewind Subtitle Displayer";
        public static readonly string AssemblyFileVersion = Utils.Version.GetVersion();
        
        public static readonly string RequirementEnableRemoteAccess = "Note: You **MUST** enable the \"Remote Control\" / aka \"Advertise As Player\" option in each player app for this to work.";
        public static string HeadingTitle => $"\n----------- {AppName} - Version {AssemblyFileVersion} -----------\n";
    }

} // ----------- End of Namespace --------------

