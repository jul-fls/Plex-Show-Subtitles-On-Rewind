//--------------------------- Global Usings ------------------------------------------
global using static RewindSubtitleDisplayerForPlex.GlobalDefinitions;
global using static RewindSubtitleDisplayerForPlex.Utils;
global using static RewindSubtitleDisplayerForPlex.MyStrings;
global using static RewindSubtitleDisplayerForPlex.LaunchArgs;
global using static RewindSubtitleDisplayerForPlex.Logging;
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
    public enum LogLevel
    {
        Error = 0,
        Warning = 1,
        Info = 2,
        Verbose = 3,
        Debug = 4,
        DebugExtra = 5
    }
    public enum Action
    {
        Play,
        Pause,
        Buffering,
        None
    }
    public enum HotkeyAction // What to do when the hotkey is activated
    {
        None,
        ToggleSubtitles
    }
    public enum HotkeyMode
    {
        DoubleClick,
        TripleClick
    }

    public static class MyStrings
    {
        public const string AppNameDashed = "Rewind-Subtitle-Displayer-For-Plex";
        public const string AppName = "Rewind Subtitle Displayer For Plex";
        public const string AppNameShort = "Rewind Subtitle Displayer";
        public const string LogFileName = "RewindSubtitleDisplayerForPlex.log";
        public static readonly string AssemblyFileVersion = Utils.Version.GetVersion();

        public static readonly string StartupImportantNotes = 
            "   1. You **MUST** enable the \"Remote Control\" / aka \"Advertise As Player\" option in each player app for this to work.\n" +
            "   2. Plex seems to be glitchy when enabling EMBEDDED subtitles for parts of the video that haven't buffered yet.\n" +
            "       > For example, you rewind immediately after starting a video from the middle.\n" +
            "       > This does NOT seem to be a problem with external subtitles (those from a separate file).\n";
        public static string HeadingTitle => $"\n----------- {AppName} - Version {AssemblyFileVersion} -----------\n";
    }

} // ----------- End of Namespace --------------

