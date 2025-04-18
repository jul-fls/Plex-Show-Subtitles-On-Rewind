using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using static RewindSubtitleDisplayerForPlex.Settings;

namespace RewindSubtitleDisplayerForPlex;

// Settings class with default values. Will be updated with values from settings file if it exists
public class Settings
{
    public class SectionDivider { } // Placeholder class for section headers
    // If any settings have their config name changed, we can keep track of them here to be able to still load them and update the config
    public Dictionary<string, ISettingInfo> PreviousSettingsNameMap = []; //{ get; init; } = [];

    // This is also the order they will be written to the settings file
    public SettingInfo<SectionDivider> StandardSettings = new(new(), ""); // Placeholder for Advanced Settings section header

    public SettingInfo<string> ServerURL = new("http://127.0.0.1:32400", "Server_URL_And_Port");
    public SettingInfo<double> MaxRewindSec = new(60, "Max_Rewind_Seconds");
    public SettingInfo<HotkeyAction> DoubleClickHotkeyAction = new(HotkeyAction.ToggleSubtitles, "DoubleClick_PlayPause_Hotkey_Action");
    public SettingInfo<HotkeyAction> TripleClickHotkeyAction = new(HotkeyAction.ToggleRewindMonitoring, "TripleClick_PlayPause_Hotkey_Action");
    public SettingInfo<bool> ManualModeOnly = new(false, "Manual_Mode_Only");
    public SettingInfo<bool> RememberSubtitlesForTVShowMode = new(false, "Remember_Subtitles_For_TVShow_Mode");
    public SettingInfo<bool> AlwaysEnableSubtitlesMode = new(false, "Always_Enable_Subtitles_Mode");
    public SettingInfo<int> ClickHotkeyTimeThresholdMs = new(400, "Click_Time_Threshold_Milliseconds");
    public SettingInfo<List<string>> SubtitlePreferencePatterns = new([], "Subtitle_Preference_Patterns");
    public SettingInfo<bool> PreferExternalSubtitles = new(true, "Prefer_External_Subtitles");
    // -----------------
    public SettingInfo<SectionDivider> StartAdvancedSettings = new(new(), ""); // Placeholder for Advanced Settings section header

    public SettingInfo<LogLevel> ConsoleLogLevel = new(LogLevel.Info, "Log_Level");
    public SettingInfo<bool> LogToFile = new(false, "Log_To_File");
    public SettingInfo<bool> SkipAuth = new(false, "Skip_Auth");
    public SettingInfo<bool> UseEventPolling = new(true, "Use_Event_Polling");
    public SettingInfo<double> ActiveMonitorFrequencySec = new(1.2, "Active_Monitor_Frequency_Seconds");
    public SettingInfo<double> IdleMonitorFrequency = new(30, "Idle_Monitor_Frequency");
    public SettingInfo<double> MaxRewindCoolDownSec = new(4, "Max_Rewind_Cooldown_Seconds");
    public AutoSettingInfo<int> ShortTimeoutLimit = new(-int.MaxValue, "Active_Timeout_Milliseconds", isAutoDefault:true); // Identifiable placeholder to know if user setting failed to set when not auto
    public SettingInfo<bool> AllowDuplicateInstance = new(false, "Allow_Duplicate_Instance");
    // -----------------
    public SettingInfo<SectionDivider> ExperimentalAndDeveloperSettings = new(new(), ""); // Placeholder for Experimental Settings section header

    public SettingInfo<bool> DiscardNextPass = new(true, "Discard_Next_Pass");
    public SettingInfo<bool> IgnoreMessagesSameOffset = new(true, "Ignore_Pass_If_Same_View_Offset");
    public SettingInfo<bool> SendCommandDirectToDevice = new(true, "Send_Command_Direct_To_Device");
    public SettingInfo<double> PendingDisabledCooldownSec = new(3, "Pending_Disabled_Cooldown_Seconds"); // Not used yet, but will be for pending disabled state when using event polling mode
    public SettingInfo<bool> DisableSubtitlesOnAppStartup = new(false, "Disable_Subtitles_On_New_Sessions");
    public SettingInfo<int> InitialSessionDelay = new(10, "Initial_Session_Delay_Seconds");

    // Constructor to set descriptions for each setting
    public Settings()
    {
        // Set descriptions in the constructor
        ServerURL.Description = "The full URL of your local server, including http, IP, and port." +
            "\nIf https:// doesn't work, you can use http:// but only do that if it's on a local network.";
        MaxRewindSec.Description = "Rewinding further than this many seconds will cancel the displaying of subtitles." +
           $"\nDefault Value: {MaxRewindSec.Value}  |  Possible Values: Any positive number (decimals allowed)";
        DoubleClickHotkeyAction.Description = "What to do when the play/pause button is double-clicked." +
           $"\n{HotkeyAction.ToggleSubtitles} will manually toggle subtitles immediately. If they are toggled on, they will stay on until toggled off." +
           $"\n{HotkeyAction.ToggleRewindMonitoring} will toggle whether rewinds from triggering subtitles. You can still use the manual toggle double/triple click hotkey if set." +
           $"\nDefault Value: {DoubleClickHotkeyAction.Value}  |  Possible Values: {HotkeyAction.ToggleSubtitles}, {HotkeyAction.ToggleRewindMonitoring}, {HotkeyAction.None}";
        TripleClickHotkeyAction.Description = $"Default Value: {TripleClickHotkeyAction.Value}  |  Possible Values: ToggleSubtitles, DisableMonitoring, None";
        ManualModeOnly.Description = "(True/False) If true, this app will default to NOT automatically toggle subtitles on rewinds." +
            "\nYou can still toggle subtitles using one of the double or triple click hotkeys. You can also re-enable monitoring using a hotkey." +
            $"\nDefault Value: {ManualModeOnly.Value}";
        RememberSubtitlesForTVShowMode.Description = "(True/False) If true, this app will remember if you manually enable subtitles at the TV-show level, and re-enable them when any episode of that show is played." +
            "\nNote: The same remembered shows list will apply across all users and devices. Be aware of this if there are multiple users of your server who watch the same shows." +
            $"Default value: {RememberSubtitlesForTVShowMode.Value}";
        AlwaysEnableSubtitlesMode.Description = "(True/False) If true, instead of monitoring for rewinds, the app simply will always enable subtitles whenever a new playback session starts." +
            $"\nSubtitles can still be disabled via the {HotkeyAction.ToggleSubtitles} hotkey action, and monitoring can be turned on via the {HotkeyAction.ToggleRewindMonitoring} hotkey action." +
            $"\nThis will also override any other rewind-related monitoring settings. It will also override the {RememberSubtitlesForTVShowMode.ConfigName} setting." +
            $"\nDefault Value: {AlwaysEnableSubtitlesMode.Value}";
        ClickHotkeyTimeThresholdMs.Description = "The maximum time in milliseconds between each individiual click to be considered for double and triple clicks." +
            "\nTry 500ms or more if you have trouble activating it. Be aware that a higher threshold could cause higher chance of false positives, " +
            "\n     because the periodic 'playing' notifications the Plex server sends (every ~5 seconds) are indisinguishable from notifications sent on play button presses." +
           $"\nDefault {ClickHotkeyTimeThresholdMs}  |  Possible Values: Any positive whole number.";
        SubtitlePreferencePatterns.Description = "This allows you to define a filter for which subtitle track will be chosen. If left empty it will always choose the first subtitle track." +
            "\nIt should be a comma separated list of words or phrases, where it will try to look for any subtitle tracks that have a name that matches ALL the listed phrases." +
            "\nYou can also start a word/phrase with a hyphen (-) to require it NOT match that. So you can exclude 'SDH' subtitles by putting '-SDH' (without quotes)." +
            "\nNote: Not case sensitive, and any quotes and/or leading/trailing spaces for each item will be trimmed off. It uses the subtitle track displayed in Plex." +
            $"\nExample to prefer English non-SDH subtitles:   {SubtitlePreferencePatterns.ConfigName}=english,-sdh";
        PreferExternalSubtitles.Description = "(True/False) If true, the app will prefer external subtitles over embedded ones, if there are multiple matches to the above preference patterns, or no matches." +
            $"\nThe above patterns still take priority though. Meaning if the only subtitle tracks matching your above patterns are embedded, it will use those embedded ones." +
            $"\nDefault Value: {PreferExternalSubtitles.Value}";

        // Advanced settings
        ConsoleLogLevel.Description = "The log level to use. DebugExtra contains additional 'noisy' less useful data that is mostly ignored anyway.\n" +
           $"Default: {ConsoleLogLevel.Value}  |  Possible values (Least to Most Detailed): Error, Warning, Info, Verbose, Debug, DebugExtra";
        LogToFile.Description = "(True/False) Log to a file in addition to the console. This will create a log file in the same directory as the app.";
        SkipAuth.Description = "(True/False) Skip the authorization step. (Not Recommended to enable, and not supported if functionality doesn't work right)" +
            "\nThis will only work if you have configured the server to allow connections from specific devices without authorization." +
            "\nNote: Event based polling might not work if this is true. If it doesn't work after going idle, try setting Use_Event_Polling to false.";
        UseEventPolling.Description = "(True/False) Use event polling instead of timer polling. Only disable this if you have issues with maintaining the plex server connection." +
           $"\nDefault Value: {UseEventPolling.Value}";
        ActiveMonitorFrequencySec.Description = "How often (in seconds) to check for rewinds during active playback." +
            "\nThe lower this value, the faster it will respond to rewinds. However setting it below 1 second is NOT recommended because most players will only update the timestamp every 1s anyway." +
           $"\nDefault Value: {ActiveMonitorFrequencySec.Value}  |  Possible Values: Any positive number (decimals allowed).";
        IdleMonitorFrequency.Description = "Only applicable when NOT using event polling mode. How often to check for playback status (in seconds) when no media is playing." +
           $"\nDefault Value: {IdleMonitorFrequency.Value}  |  Possible Values: Any positive number (decimals allowed)";
        MaxRewindCoolDownSec.Description = $"After you rewind further than {MaxRewindSec.ConfigName}, for this many seconds further rewinds will be ignored." +
            $"\nThis is so if you are rewinding by clicking the back button many times, it doesn't immediately start showing subtitles after you pass the Max Rewind threshold." +
            $"\nDefault Value: {MaxRewindCoolDownSec.Value}  | Possible Values: Positive number or decimal, or 0";
        ShortTimeoutLimit.Description = "The maximum time in milliseconds to wait for a response from the server before timing out between checks." +
            "\nShould be shorter than the active frequency (but not required, like for testing). You can also use 'auto' to automatically use 90% of the active frequency." +
            "\nDefault Value: auto  |  Possible Values: Any positive whole number, or \"auto\" (without quotes)";
        AllowDuplicateInstance.Description = "(True/False) Allow multiple instances of the app to run at the same time. Not recommended, mostly used for debugging." +
           $"\nDefault Value: {AllowDuplicateInstance.Value}";

        // Experimental & Development Settings
        DiscardNextPass.Description = "(True/False) If true, the polling pass immediately following a notification event will be discarded because it probably has no new info." +
            $"\nDefault Value: {DiscardNextPass.Value}";
        IgnoreMessagesSameOffset.Description = "(True/False) If true, the app will skip processing messages that have the same playback offset as the last session status check." +
            $"\nDefault Value: {IgnoreMessagesSameOffset.Value}";
        SendCommandDirectToDevice.Description = "(True/False) If true, the app will send commands directly to the device IP instead of going through the server." +
            $"\nDefault Value: {SendCommandDirectToDevice.Value}";
        PendingDisabledCooldownSec.Description = "After subtitles are disabled, there is a short delay when the player actually disables them. This accounts for that." +
            $"\nDefault Value: {PendingDisabledCooldownSec.Value}  |  Possible Values: Any positive number (decimals allowed)";
        DisableSubtitlesOnAppStartup.Description = "(True/False) If true, this app will disable subtitles on the player on app startup and when a new session is detected." +
            $"\nDefault Value: {DisableSubtitlesOnAppStartup.Value}";
        InitialSessionDelay.Description = "Within about 10 seconds of a player app starting a new session, it doesn't seem to respond to subtitle commands correctly, " +
            "so this delay prevents subs from being activated in that time. Or set to 0 to disable." +
            $"\nDefault Value: {InitialSessionDelay.Value}  |  Possible Values: Any positive whole number.";


        // ---------------- Set default values for section dividers ----------------
        StandardSettings.Description =      "----------------------- Standard Settings -----------------------";
        StartAdvancedSettings.Description = "----------------------- Advanced Settings - (Most people shouldn't need these) -----------------------";
        ExperimentalAndDeveloperSettings.Description = "----------------------- Experimental & Development Settings  -----------------------\n" +
            "# These are mostly for debugging, probably don't mess with them. Changing these could cause unexpected behavior.";

        // Create a new dictionary with your name mappings
        PreviousSettingsNameMap = new Dictionary<string, ISettingInfo>
        {
            //{ "Some_Old_ConfigName", ServerURL }, // Example
            { "Max_Rewind_Cooldown", MaxRewindCoolDownSec },
        };

    }   

    public Dictionary<ISettingInfo, string> SettingsThatFailedToLoad = [];
    public Dictionary<ISettingInfo, string> SettingsThatTriggeredWarning = [];
    public List<string> UnknownSettings = [];
    public List<string> MissingSettings = [];

    public static Settings Default() { return new Settings(); }

    // ------------------------------------------------------
    public Settings CleanAndValidate()
    {
        Settings def = new Settings(); // Default settings object to get default values if needed to replace invalid ones

        // ===================================================
        // Validate each setting as much as reasonable
        // --------------------------------------------------

        // Server URL
        ServerURL.Value = ServerURL.Value.TrimEnd('/');
        if (string.IsNullOrEmpty(ServerURL))
        {
            string errorMessage = $"Error for setting {ServerURL.ConfigName}: Server URL is empty or null. Will use default value {def.ServerURL}";
            LogError(errorMessage);
            this.SettingsThatFailedToLoad.TryAdd(ServerURL, errorMessage);
            ServerURL = def.ServerURL;
        }

        // Active Monitor Frequency
        if (ActiveMonitorFrequencySec < 0)
        {
            string errorMessage = $"Error for setting {ActiveMonitorFrequencySec.ConfigName}: Active Monitor Frequency must be greater than or equal to 0.\nWill use default value {def.ActiveMonitorFrequencySec}";
            LogError(errorMessage);
            this.SettingsThatFailedToLoad.TryAdd(ActiveMonitorFrequencySec, errorMessage);
            ActiveMonitorFrequencySec = def.ActiveMonitorFrequencySec;
        }
        else if (ActiveMonitorFrequencySec < 1) // Allow but warn
        {
            string warningMessage = $"Warning for setting {ActiveMonitorFrequencySec.ConfigName}: " +
                $"Active Monitor Frequency of less than 1 second is not recommended because player apps probably won't report the time more accurately than that.\n" +
                $"Too low a frequency could potentially cause longer responses than just setting it at the default 1s if the server / player gets bogged down.\n" +
                $"I recommend you enable debug mode and see if the 'Position' timestamp for each polling status is actually changing every time at this frequency.";
            LogWarning(warningMessage);
            this.SettingsThatTriggeredWarning.TryAdd(ActiveMonitorFrequencySec, warningMessage);
        }

        // Idle Monitor Frequency
        if (IdleMonitorFrequency < 0)
        {
            string errorMessage = $"Error for setting {IdleMonitorFrequency.ConfigName}: Idle Monitor Frequency must be greater than or equal to 0.\nWill use default value {def.IdleMonitorFrequency}";
            LogError(errorMessage);
            this.SettingsThatFailedToLoad.TryAdd(IdleMonitorFrequency, errorMessage);
            IdleMonitorFrequency = def.IdleMonitorFrequency;
        }

        // Short Timeout Limit
        if (ShortTimeoutLimit.IsSetToAuto) // Check the IsSetToAuto flag from the IntOrAuto class
        {
            // Calculate based on the current ActiveMonitorFrequency (which should have been validated already)
            // Using Math.Round just in case they used more than 3 decimal places for some weird reason
            // Don't bother checking for validity because we're using the Active frequency which was already validated
            ShortTimeoutLimit.Value = (int)Math.Round((ActiveMonitorFrequencySec.Value * 1000 * 0.9));
        }
        else // User provided a specific integer value (IsSetToAuto is false)
        {
            int currentTimeoutValue = ShortTimeoutLimit.Value; // Get the integer value explicitly
            int currentFreqMs = (int)Math.Round((ActiveMonitorFrequencySec.Value * 1000));
            int autoCalcDefault = (int)Math.Round((ActiveMonitorFrequencySec.Value * 1000 * 0.9)); // Calculate the default timeout based on the current ActiveMonitorFrequency, which should be valid already

            if (currentTimeoutValue < 0)
            {
                string errorMessage;
                if (currentTimeoutValue == -int.MaxValue) // This means it wasn't auto, and also an invalid value that was replaced with -int.MaxValue as invalid placeholder
                {
                    // In this case it should already have been logged as an error so we don't need to log it again. Still creating the error message in case we need it later
                    errorMessage = $"Error for setting {ShortTimeoutLimit.ConfigName}: Active Timeout value ('{currentTimeoutValue}') cannot be negative. Will use default value {autoCalcDefault}ms (90% of {ActiveMonitorFrequencySec.ConfigName})";
                }                
                else
                {
                    errorMessage = $"Error for setting {ShortTimeoutLimit.ConfigName}: Active Timeout value ('{currentTimeoutValue}') cannot be negative. Will use default value {autoCalcDefault}ms (90% of {ActiveMonitorFrequencySec.ConfigName})";
                    LogError(errorMessage);
                }
                
                this.SettingsThatFailedToLoad.TryAdd(ShortTimeoutLimit, errorMessage); // Still try adding to the error list just in case it wasn't logged already, though it should be
                ShortTimeoutLimit.Value = autoCalcDefault; // Reset to calculated default
            }
            // Optional: Warn if timeout > frequency, but don't force reset unless it's negative
            else if (currentTimeoutValue > currentFreqMs)
            {
                string warning = $"Warning for setting {ShortTimeoutLimit.ConfigName}: Value ({currentTimeoutValue}ms) is greater than {ActiveMonitorFrequencySec.ConfigName} ({currentFreqMs}ms). This might lead to overlapping checks.";
                SettingsThatTriggeredWarning.TryAdd(ShortTimeoutLimit, warning);
                LogWarning(warning); 
            }
        }

        // Max Rewind
        if (MaxRewindSec < 0)
        {
            string errorMessage = $"Error for setting {MaxRewindSec.ConfigName}: Max Rewind must be greater than or equal to 0.\nWill use default value {def.MaxRewindSec}";
            LogError(errorMessage);
            this.SettingsThatFailedToLoad.TryAdd(MaxRewindSec, errorMessage);
            MaxRewindSec = def.MaxRewindSec;
        }

        // Cool Down Count
        if (MaxRewindCoolDownSec < 0)
        {
            string errorMessage = $"Error for setting {MaxRewindCoolDownSec.ConfigName}: Cooldown must be greater than or equal to 0.\nWill use default value {def.MaxRewindCoolDownSec}";
            LogError(errorMessage);
            this.SettingsThatFailedToLoad.TryAdd(MaxRewindCoolDownSec, errorMessage);
            MaxRewindCoolDownSec = def.MaxRewindCoolDownSec;
        }

        // CLick Threshold
        if (ClickHotkeyTimeThresholdMs < 0)
        {
            string errorMessage = $"Error for setting {ClickHotkeyTimeThresholdMs.ConfigName}: Click Threshold must be greater than or equal to 0.\nWill use default value {def.ClickHotkeyTimeThresholdMs}";
            LogError(errorMessage);
            this.SettingsThatFailedToLoad.TryAdd(ClickHotkeyTimeThresholdMs, errorMessage);
            ClickHotkeyTimeThresholdMs = def.ClickHotkeyTimeThresholdMs;
        }

        // Subtitle preference
        if (SubtitlePreferencePatterns.Value != null)
        {
            // Check if the list is empty
            if (SubtitlePreferencePatterns.Value.Count == 0)
            {
                // It's ok if there are no entries in the list
            }
            else
            {
                // Check for invalid entries in the list
                foreach (string pattern in SubtitlePreferencePatterns.Value)
                {
                    // Trim whitespace and quotes from the pattern. Trim again to remove any leading/trailing whitespace for each char
                    string trimmedPattern = pattern.Trim().Trim('"').Trim().Trim('\'').Trim();

                    // Check if the pattern is empty or whitespace and warn the user with a message slightly specific to the issue
                    // For simple extra whitespace after the equals sign in the settings, the original parser should have caught and ignored it, but just in case
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        string warning = $"Warning for setting {SubtitlePreferencePatterns.ConfigName}: Subtitle Preference Pattern list contains empty item which will be ignored.";
                        LogWarning(warning);
                        SettingsThatTriggeredWarning.Add(SubtitlePreferencePatterns, warning);
                        // Remove the empty item from the list
                        SubtitlePreferencePatterns.Value.Remove(pattern);
                    }
                    else if (string.IsNullOrWhiteSpace(trimmedPattern))
                    {
                        string warning = $"Warning for setting {SubtitlePreferencePatterns.ConfigName}: Subtitle Preference Pattern list contains item that was empty after trimming whitespace and quotes and will be ignored.";
                        LogWarning(warning);
                        SettingsThatTriggeredWarning.Add(SubtitlePreferencePatterns, warning);
                        SubtitlePreferencePatterns.Value.Remove(pattern);
                    }
                }
            }
        }

        // Incompatible settings
        if (AlwaysEnableSubtitlesMode && DisableSubtitlesOnAppStartup)
        {
            string warnMessage = $"WARNING: Settings {AlwaysEnableSubtitlesMode.ConfigName} and {DisableSubtitlesOnAppStartup.ConfigName} both should not be true. You should set at least one of them to false.";
            LogWarning(warnMessage);
            this.SettingsThatTriggeredWarning.TryAdd(AlwaysEnableSubtitlesMode, warnMessage);
            this.SettingsThatTriggeredWarning.TryAdd(DisableSubtitlesOnAppStartup, warnMessage);
        }

        // If no issues found, return true
        return this;
    }
}

// Function to create a settings file if it doesn't exist using settings names and default values
public static class SettingsHandler
{
    // Various Enums / Pseudo Enums
    private static class SettingStrings
    {
        public const string SettingsFileName = "settings.ini";
        public const string SettingsFileTemplate = "settings.ini.template";
    }

    public static string SettingsFilePath => Path.Combine(BaseConfigsDir, SettingStrings.SettingsFileName);
    public static string SettingsFileTemplatePath => Path.Combine(BaseConfigsDir, SettingStrings.SettingsFileTemplate);

    public enum PrintResultType
    {
        None,
        ResultingConfig,
        FailureSummary
    }

    // Load settings from settings file into a Settings object
    public static Settings LoadSettings(PrintResultType printResult = PrintResultType.FailureSummary)
    {
        CreateSettingsFileIfNotExists();
        Settings settings = new Settings();
        Type settingsType = typeof(Settings);

        // Create a lookup dictionary for field name to config name for easier matching
        Dictionary<string, string> fieldToConfigName = [];

        // List of settings to check if any are missing
        List<string> knownSettingsInfoLeft = []; // Will remove from this list as we find them
        List<string> unknownSettings = [];

        // Iterate through fields of the Settings instance
        foreach (System.Reflection.FieldInfo field in settingsType.GetFields())
        {
            // Get the value of the field from the specific 'settings' instance
            object? settingValue = field.GetValue(settings);

            // Check if the field's value implements ISettingInfo and cast it. But ignore Section Dividers which are not an actual setting
            if (settingValue is ISettingInfo settingInfo && settingInfo.ValueType != typeof(SectionDivider))
            {
                // Get the ConfigName directly from the interface - NO REFLECTION
                string configName = settingInfo.ConfigName;

                // Check if the configName is valid before adding to the dictionary
                // (Interface guarantees non-null string, so just check for empty/whitespace)
                if (!string.IsNullOrWhiteSpace(configName))
                {
                    // Map the C# field name (field.Name) to the config file name (configName)
                    fieldToConfigName[field.Name] = configName;

                    // Populate the list of known settings to track which ones are missing later
                    knownSettingsInfoLeft.Add(configName);
                }
                else
                {
                    // Optional: Warn if a setting is found without a usable ConfigName
                    LogError($"Warning: Field '{field.Name}' (Type: {settingInfo.GetType().Name}) has a null or empty ConfigName. This is probably a bug.");
                }
            }
        }

        // Load settings from file
        foreach (string line in File.ReadAllLines(SettingsFilePath))
        {
            // Local function
            static bool checkIsCommentLine(string checkLine)
            {
                checkLine = checkLine.Trim().Trim('\t');
                return checkLine.StartsWith('#') || checkLine.StartsWith("//");
            }
            // --------------------------------------------------

            // If it starts with a comment character, skip it
            if (checkIsCommentLine(line))
                continue;

            string[] parts = line.Split('=');
            if (parts.Length == 2)
            {
                string configName = parts[0].Trim();
                string rawSettingValue = parts[1].Trim().Trim('"');
                bool recognized = false; // Flag to track if we found the config name in the dictionary

                // Check if any old settings names are used and warn the user. Will attempt to load the value into the new setting name anyway.
                if (settings.PreviousSettingsNameMap.TryGetValue(configName, out ISettingInfo? newSetting))
                {
                    string oldConfigName = configName;
                    configName = newSetting.ConfigName; // Update to the new config name

                    string warningMessage = $"Warning: The setting name '{oldConfigName}' is deprecated. Use '{configName}' instead.\n" +
                        $"Use the -{LaunchArgs.UpdateSettings.Arg} launch argument to automatically update the settings file.";
                    LogWarning(warningMessage);
                    settings.SettingsThatTriggeredWarning.TryAdd(newSetting, warningMessage);
                }

                // Find the field with this config name (Loop using fieldToConfigName)
                foreach (KeyValuePair<string, string> kvp in fieldToConfigName)
                {
                    if (kvp.Value == configName) // configName is the string in the file, kvp.Value is each known setting name
                    {
                        // Get the C# Field name (key) and retrieve the FieldInfo
                        System.Reflection.FieldInfo? field = settingsType.GetField(kvp.Key);
                        if (field != null)
                        {
                            // Get the actual SettingInfo object instance from the 'settings' object
                            object? settingObj = field.GetValue(settings);

                            // Check if it's an ISettingInfo and cast it, but ignore SectionDivider which is not an actual setting
                            if (settingObj is ISettingInfo settingInfo && settingInfo.ValueType != typeof(SectionDivider))
                            {
                                try
                                {
                                    // Remove the config name from the list of known settings even if it ends up failing to set the value
                                    knownSettingsInfoLeft.Remove(configName);

                                    // Use the interface method to set the value - NO REFLECTION HERE
                                    settingInfo.SetValueFromString(rawSettingValue);                                   
                                }
                                catch (FormatException ex)
                                {
                                    // Make a string to add to the error message if there is an inner exception
                                    string innerExceptionMessage = string.IsNullOrEmpty(ex.InnerException?.Message) ? string.Empty : ("\n    > Inner Exception: " + ex.InnerException?.Message);  
                                    string errorMessage = $"Error applying setting '{configName}' in {SettingStrings.SettingsFileName} (Likely invalid value): {ex.Message}{innerExceptionMessage}";
                                    LogError(errorMessage);
                                    settings.SettingsThatFailedToLoad.TryAdd(settingInfo, errorMessage);
                                }
                            }
                            else
                            {
                                // Handle case where the field value isn't an ISettingInfo (shouldn't happen if fieldToConfigName is built correctly)
                                string error = $"Error: Field '{kvp.Key}' associated with config '{configName}' did not contain an ISettingInfo object. This is probably a bug.";
                                LogError(error);
                            }
                        }
                        else
                        {
                            // Handle case where field name from dictionary doesn't exist in Settings class
                            string error = $"Error: Field name '{kvp.Key}' (for config '{configName}') not found in Settings class. This is probably a bug.";
                            LogError(error);
                        }
                        recognized = true; // Mark as found even if it failed to set the value
                        break; // Found the setting, exit the inner loop
                    }
                }

                if (!recognized)
                {
                    // If we didn't find the config name in the dictionary, it might be a new setting or a typo
                    string error = $"Error: Unknown setting '{configName}' in {SettingStrings.SettingsFileName}.\n" +
                        $"Use the -{LaunchArgs.ConfigTemplate.Arg} launch argument to generate an example config. Or use -{LaunchArgs.UpdateSettings.Arg} to regenerate a config using your existing valid settings.";
                    LogError(error);
                    unknownSettings.Add(configName);
                }
            }
        }

        settings.UnknownSettings = unknownSettings.Distinct().ToList(); // Remove duplicates
        settings.MissingSettings = knownSettingsInfoLeft;

        // Validate and clean settings. Will restore default values for each setting if invalid
        settings.CleanAndValidate();

        // Display super-error warning if any settings failed to load
        if (settings.SettingsThatFailedToLoad.Count > 0 && printResult == PrintResultType.FailureSummary)
        {
            string failedSettings = string.Join(", ", settings.SettingsThatFailedToLoad.Select(s => s.Key.ConfigName));
            WriteRedSuper($"\nWarning: The following settings failed to load. See errors above and check them in your settings file:");
            WriteRed($"\t\t{failedSettings}\n");
        }
        else if (printResult == PrintResultType.ResultingConfig)
        {
            PrintResultingConfig(settings);
        }

        return settings;
    }

    enum ProblemType
    {
        Error,
        Warning,
        Missing,
        Unknown,
        None
    }

    // After loading and validating settings, print the resulting config to the console
    public static void PrintResultingConfig(Settings settings)
    {
        Dictionary<ISettingInfo, string> failedSettings = settings.SettingsThatFailedToLoad;
        Dictionary<ISettingInfo, string>  warnedSettings = settings.SettingsThatTriggeredWarning;

        WriteLineSafe("\n------------------------ Resulting Config ------------------------\n\n" +
            "These resulting values will be used based on your config file.\n" +
            "Note: They may not appear exactly as they do in the file, such as for 'auto' settings and values reset to default because they failed or were missing.\n");

        // Print all the settings. For ones that failed to load, highlight them. They have already been reset to default values
        foreach (System.Reflection.FieldInfo field in typeof(Settings).GetFields())
        {
            // Get the value of the field from the specific 'settings' instance
            object? fieldValue = field.GetValue(settings);
            // Check if the field's value is actually an ISettingInfo instance
            if (fieldValue is ISettingInfo settingInfo && settingInfo.ValueType != typeof(SectionDivider))
            {
                // Access properties directly via the interface
                string configName = settingInfo.ConfigName;
                object? resultValue = settingInfo.GetValueAsObject();
                ProblemType problem = ProblemType.None;

                // If it's a list, join the items with commas
                if (resultValue is List<string> list)
                {
                    resultValue = string.Join(", ", list);
                }

                if (failedSettings.ContainsKey(settingInfo))
                {
                    problem = ProblemType.Error;
                }
                else if (warnedSettings.ContainsKey(settingInfo))
                {
                    problem = ProblemType.Warning;
                } 
                else if (settings.MissingSettings.Contains(configName))
                {
                    problem = ProblemType.Missing;
                }

                // Print the setting name and value, highlighting if it failed to load
                if (problem == ProblemType.Error)
                {
                    WriteRedSuper(configName, noNewline: true);
                    WriteSafe(" = ");
                    WriteSafe($"{resultValue}");
                    WriteRed("  (See Error Above) - Default Value Will Be Used");
                }
                else if (problem == ProblemType.Warning)
                {
                    WriteYellow(configName, noNewline: true);
                    WriteSafe(" = ");
                    WriteSafe($"{resultValue}");
                    WriteYellow("  (See Warning Above)");
                }
                else if (problem == ProblemType.Missing)
                {
                    WriteRedSuper(configName, noNewline: true);
                    WriteSafe(" = ");
                    WriteSafe($"{resultValue}");
                    WriteRed("  (Missing Setting) - Default Value Will Be Used");
                }
                else // No problem
                {
                    WriteGreen(configName, noNewline: true);
                    WriteSafe(" = ");
                    WriteLineSafe($"{resultValue}"); // Print the value
                }
            }
        }

        // Print unknown settings and missing settings
        if (settings.UnknownSettings.Count > 0)
        {
            WriteLineSafe("\nUnknown Settings:");
            foreach (string unknownSetting in settings.UnknownSettings)
            {
                WriteRed(unknownSetting);
            }
        }

        if (settings.MissingSettings.Count > 0)
        {
            WriteLineSafe("\nMissing Settings:");
            foreach (string missingSetting in settings.MissingSettings)
            {
                WriteRed(missingSetting);
            }
        }
    }

    private static bool CreateSettingsFile(string filePath, Settings? settingsIn = null)
    {
        Settings settings;

        if (settingsIn != null)
            settings = settingsIn;
        else
            settings = new Settings(); // Load default settings if no settings object is provided

        try
        {
            // Assuming SettingStrings.SettingsFileName is defined elsewhere
            using StreamWriter sw = File.CreateText(filePath);

            // Iterate through fields of the Settings instance
            foreach (System.Reflection.FieldInfo field in typeof(Settings).GetFields())
            {
                // Get the value of the field from the specific 'settings' instance
                object? fieldValue = field.GetValue(settings);

                // Check if the field's value is actually an ISettingInfo instance
                // This replaces the need to check field.FieldType separately AND casts it.
                if (fieldValue is ISettingInfo settingInfo) //
                {
                    // Access properties directly via the interface - NO REFLECTION NEEDED
                    string description = settingInfo.Description;
                    string configName = settingInfo.ConfigName;
                    object? defaultValue = settingInfo.GetValueAsObject();

                    // Special case for SectionDivider. Only print the description
                    if (settingInfo.ValueType == typeof(SectionDivider))
                    {
                        sw.WriteLine($"# {description}\n");
                        continue; // Skip to next field
                    }

                    // For each line in a description, add comment character, a tab, and the description
                    // (Check against string.Empty since interface Description is non-null string)
                    if (!string.IsNullOrEmpty(description)) // More concise check
                    {
                        // Use Environment.NewLine for potentially better cross-platform line breaks
                        string[] descriptionLines = description.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
                        foreach (string line in descriptionLines)
                        {
                            sw.WriteLine($"\t# {line}");
                        }
                    }

                    // Check against null/empty for configName and null for defaultValuePlaceholder
                    // (configName is non-null string from interface, but check for empty might still be desired)
                    if (!string.IsNullOrEmpty(configName) && defaultValue != null)
                    {
                        // Consider potential formatting for defaultValuePlaceholder depending on its type
                        // .ToString() might not always be the desired file format (e.g., for booleans, dates)
                        string valueAsString;

                        // Handle special cases first like lists and 'auto' default values
                        if (settingInfo.IsAutoDefault == true)
                        {
                            valueAsString = "auto";
                        }
                        else if (defaultValue is List<string> list)
                        {
                            // Join list items with commas
                            valueAsString = string.Join(",", list);
                        }
                        else
                        {
                            // Default case for other types. This seems to work for most types
                            valueAsString = $"{defaultValue}";
                        }

                        sw.WriteLine($"{configName}={valueAsString}");
                        sw.WriteLine();
                    }
                    else
                    {
                        // Optional: Log or handle cases where essential info is missing
                        LogWarning($"Warning: Skipping field '{field.Name}'. ConfigName or DefaultValue is missing/null.");
                        if (string.IsNullOrEmpty(configName)) WriteLineSafe($" - ConfigName is missing.");
                        if (defaultValue == null) WriteLineSafe($" - DefaultValue is null.");
                    }
                }
            }

            // sw is automatically disposed/flushed by the using statement
            sw.Close();
            WriteGreen($"Created settings config file \"{filePath}\"\n");
            return true; // Indicate success
        }
        catch (Exception ex)
        {
            // Handle exceptions (e.g., file access issues)
            LogError($"Error creating settings file: {ex.Message}");
            // Optionally log the inner exception: WriteLineSafe(ex.InnerException);
            return false; // Indicate failure
        }
    }

    // Automatically create settings file if it doesn't exist
    public static bool CreateSettingsFileIfNotExists()
    {
        if (!File.Exists(SettingsFilePath))
        {
            CreateSettingsFile(SettingsFilePath);
            return true; // File was created
        }
        else
        {
            // File already exists, no action taken
            return false; // File already exists
        }
    }

    // Will re-write the settings file using the current settings. Useful if the user has a partial/old settings file.
    // When their old settings file is loaded, it will apply whichever ones exist, but apply defaults for the rest
    // Therefore we can just re-write the settings file with the current settings object which will include their values where they were valid
    public static bool UpdateSettingsFile(Settings settings)
    {
        // Check if the settings file exists
        if (File.Exists(SettingsFilePath))
        {
            // If it exists, create a backup
            string backupFileName = SettingsFilePath + ".bak";
            backupFileName = Utils.GetAvailableFileName(backupFileName, returnFullPath: false, mode: Utils.FileNameIterationLocation.Extension);
            try
            {
                File.Copy(sourceFileName: SettingsFilePath, destFileName: backupFileName, overwrite: true);
                WriteGreen($"Backup of settings file created as \"{SettingStrings.SettingsFileName}.bak\"\n");
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file access issues)
                WriteRed($"Error - Cannot update settings file. Failed while creating backup settings file: {ex.Message}");
                // Optionally log the inner exception: WriteLineSafe(ex.InnerException);
                return false; // Indicate failure
            }
        }
        // Create a new settings file with the updated settings
        bool result = CreateSettingsFile(filePath: SettingsFilePath, settingsIn: settings);

        if (result)
        {
            WriteGreen($"Successfully updated settings file.");
            return true; // Indicate success
        }
        else
        {
            WriteRed($"Error - Cannot update settings file. Failed to create new settings file.");
            return false; // Indicate failure
        }
    }

    // Generate a template settings file. Will overwrite the existing one if it exists
    public static void GenerateTemplateSettingsFile()
    {
        CreateSettingsFile(filePath: SettingsFileTemplatePath);
    }

} // --------- End of SettingsHandler ---------


// Non-generic interface
public interface ISettingInfo
{
    string ConfigName { get; }
    string Description { get; }
    object? GetValueAsObject(); // Method to get the value as object
    Type ValueType { get; }     // Property to get the underlying type T
    string RawValue { get; } // Original unprocessed value (or minimally processed, like after trimming)
    void SetValueFromString(string stringValue);
    static bool SettingSupportsAuto { get; }
    bool IsAutoDefault { get; } // Could vary depending on individual setting, so not static
}

// Modify SettingInfo<T> to implement it
public class SettingInfo<T> : ISettingInfo
{
    private T _value;

    // Core value with implicit conversion for seamless usage
    public T Value
    {
        get => _value;
        set => _value = value;
    }

    // Metadata properties
    public string ConfigName { get; set; }
    public string Description { get; set; } = string.Empty;
    public string RawValue { get; private set; } = string.Empty;

    // Unchanging auto-related properties, since this is not an auto class
    public bool SettingSupportsAuto { get; } = false ; // This type never supports auto, there are specific types for that
    public bool IsAutoDefault { get; } = false; // This type never supports auto, there are specific types for that

    // Constructor
    public SettingInfo(T defaultValue, string configName)
    {
        _value = defaultValue;
        ConfigName = configName;
    }

    // Implicit conversions to maintain usage simplicity
    public static implicit operator T(SettingInfo<T> setting) => setting._value;
    // Be cautious with implicit conversion from T - might hide the SettingInfo object unintentionally.
    // Consider making it explicit or removing if not strictly needed.
    // public static explicit operator SettingInfo<T>(T value) => new(value, string.Empty); // Example: Explicit

    public override string ToString() => _value?.ToString() ?? string.Empty;

    // --- ISettingInfo Implementation --- (Unchanging Values)
    string ISettingInfo.ConfigName => this.ConfigName;
    string ISettingInfo.Description => this.Description;
    object? ISettingInfo.GetValueAsObject() => this.Value; // Boxes value types
    Type ISettingInfo.ValueType => typeof(T);

    // Implementation of the new method
    [SuppressMessage("Style", "IDE0306:Simplify collection initialization")]
    void ISettingInfo.SetValueFromString(string stringValue)
    {
        try
        {
            this.RawValue = stringValue.Replace("\n", "");

            // Special handling for List<string>
            if (typeof(T) == typeof(List<string>))
            {
                // Split the string by commas and trim whitespace
                string[] items = stringValue.Split([','], StringSplitOptions.RemoveEmptyEntries);
                List<string> listValue = new(items.Select(item => item.Trim()).ToList());
                this.Value = (T)(object)listValue; // Cast to object first to avoid invalid cast exception
                return;
            }
            else if (typeof(T).BaseType == typeof(Enum))
            {
                // Handle Enum conversion
                if (Enum.TryParse(enumType: typeof(T), value: stringValue, ignoreCase: true, result: out object? enumValue))
                {
                    this.Value = (T)enumValue;
                    return;
                }
                else
                {
                    throw new FormatException($"Failed to convert '{stringValue}' to enum type {typeof(T).Name}.");
                }
            }
            else
            {
                // Perform the conversion from the input string to Type T internally
                // This uses the known type T of this specific SettingInfo instance
                this.Value = (T)Convert.ChangeType(stringValue, typeof(T));
                return;
            }
        }
        catch (FormatException ex)
        {
            // Handle potential conversion errors (FormatException, InvalidCastException, etc.)
            // You might want to log this, throw a more specific exception, or set a default.

            // If it's boolean write a more specific error because people don't know what that is
            if (typeof(T) == typeof(bool))
            {
                throw new FormatException($"Failed to interpret value. Must be true or false. See inner exception for details.", ex);
            }
            else if (typeof(T) == typeof(int))
            {
                throw new FormatException($"Failed to convert text '{stringValue}' to valid value. Must be whole number. See inner exception for details.", ex);
            }
            else if (typeof(T) == typeof(double))
            {
                throw new FormatException($"Failed to convert text '{stringValue}' to valid value. Must be a number (decimals allowed). See inner exception for details.", ex);
            }
            else
            {
                throw new FormatException($"Failed to convert text '{stringValue}' to type {typeof(T).Name}. See inner exception for details.", ex);
            }
        }
        catch (Exception ex)
        {
            // Handle other potential exceptions
            throw new Exception($"Failed to set value. See inner exception for details.", ex);
        }
    }
}

// Generic base class for auto settings. Takes a type parameter T
//TODO: Can probably consolidate this into the SettingInfo<T> class and remove the need for this class
public class AutoSettingInfo<T> : ISettingInfo
{
    // ------------ Matching Properties required by ISettingInfo ---------------
    public string ConfigName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Type ValueType => typeof(int);
    public string RawValue { get; protected set; } = string.Empty;

    private T _value;
    public virtual T Value
    {
        get => _value;
        set
        {
            _value = value;
            _isSetToAuto = false;
        }
    }

    public static implicit operator T(AutoSettingInfo<T> setting) => setting.Value; // Implicit conversion to T

    public override string ToString() => _value?.ToString() ?? string.Empty;

    public object? GetValueAsObject() => _value; // Always return the resolved value

    public void SetValueFromString(string stringValue)
    {
        _isSetToAuto = false; // Reset flag
        this.RawValue = stringValue.Replace("\n", " ");

        try
        {

            if (stringValue.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                _isSetToAuto = true;
                // Don't set _value here yet, let CleanAndValidate handle the calculation based on other settings
                // Or set a temporary placeholder if absolutely needed, but a flag is cleaner.
                // _value = -1; // Example placeholder if needed, but prefer using the flag
            }
            else
            {
                // Perform the conversion from the input string to Type T internally
                // This uses the known type T of this specific SettingInfo instance
                this.Value = (T)Convert.ChangeType(stringValue, typeof(T));
                return;
            }
        }
        catch
        {
            throw new FormatException($"Failed to convert text '{stringValue}'.  Must be 'auto' or valid value for type {typeof(T).Name}.");
        }
    }

    // ----------------------------------------------------------------------------------
    // --------------------------------- Auto Settings ----------------------------------
    // ----------------------------------------------------------------------------------

    // Private / Protected fields
    private bool _isSetToAuto;

    // --------------- Public Properties ---------------
    /// <summary>
    /// Indicates if 'auto' is the default setting for the configuration. Defaults to true, determining how the settings
    /// file is formatted. Otherwise the setting file will have the literal default 'Value' property (e.g., 0, -1, etc.) which may be a placeholder.
    /// </summary>
    public bool IsAutoDefault { get; init; }
    public static bool SettingSupportsAuto => true; // Force this ISettingInfo property to be true for all auto settings
    public bool IsSetToAuto => _isSetToAuto;

    // ------------------ Constructor -----------------

    // Implicitly require isAutoDefault using constructor, instead of 'required' keyword on the property
    // If additional constructors were added in the future, must remember to add this to all of them
    public AutoSettingInfo(T defaultValuePlaceholder, string configName, bool isAutoDefault)
    {
        IsAutoDefault = isAutoDefault;
        _isSetToAuto = isAutoDefault; // Initial value will be true if isAutoDefault is true
        _value = defaultValuePlaceholder;
        ConfigName = configName;
    }

    // ------------------- Methods ------------------

    // To set the value without tripping the auto flag as false if necessary. Not currently used.
    public void ForceSetValueKeepAutoTrue(T value)
    {
        _value = value;
        _isSetToAuto = true;
    }

} // --- End of AutoSettingInfo ---
