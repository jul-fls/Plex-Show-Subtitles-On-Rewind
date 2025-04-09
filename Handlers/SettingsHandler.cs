using System.ComponentModel;
using static RewindSubtitleDisplayerForPlex.Settings;

namespace RewindSubtitleDisplayerForPlex;



// Settings class with default values. Will be updated with values from settings file if it exists
public class Settings
{
    public class SectionDivider { } // Placeholder class for section headers

    // This is also the order they will be written to the settings file
    public SettingInfo<SectionDivider> StandardSettings = new(new(), ""); // Placeholder for Advanced Settings section header
    public SettingInfo<string> ServerURL = new("http://127.0.0.1:32400", "Server_URL_And_Port");
    public SettingInfo<bool> BackgroundMode = new(false, "Background_Mode");
    public SettingInfo<int> ActiveMonitorFrequency = new(1, "Active_Monitor_Frequency");
    public SettingInfo<int> MaxRewind = new(60, "Max_Rewind_Seconds");
    public SettingInfo<int> CoolDownCount = new(5, "Max_Rewind_Cooldown");
    public SettingInfo<List<string>> SubtitlePreferencePatterns = new([], "Subtitle_Preference_Patterns");
    public SettingInfo<SectionDivider> StartAdvancedSettings = new(new(), ""); // Placeholder for Advanced Settings section header
    public SettingInfo<bool> UseEventPolling = new(true, "Use_Event_Polling");
    public SettingInfo<int> IdleMonitorFrequency = new(30, "Idle_Monitor_Frequency");
    public SettingInfo<int> ShortTimeoutLimit = new(750, "Active_Timeout_Milliseconds");
    public SettingInfo<bool> DebugMode = new(false, "Debug_Mode");
    public SettingInfo<bool> AllowDuplicateInstance = new(false, "Allow_Duplicate_Instance");
    

    // Constructor to set descriptions for each setting
    public Settings()
    {
        // Set descriptions in the constructor
        ServerURL.Description = "The full URL of your local server, including http, IP, and port";
        BackgroundMode.Description = "(True/False) Windows Only: Run in background mode. This will not show the the console Window at all, but will still run in the background and monitor playback.\n" +
            $"You can stop all running isntances by running the app through command line again but with \"-{LaunchArgs.Stop}\" parameter.";
        ActiveMonitorFrequency.Description = "How often to check for playback status (in seconds) when actively monitoring. Must be a positive whole number.";
        DebugMode.Description = "(True/False) Always default to using debug mode without having to use '-debug' launch parameter.";
        MaxRewind.Description = "Rewinding further than this many seconds will cancel the displaying of subtitles. Must be a positive whole number.";
        CoolDownCount.Description = $"After you rewind further than {MaxRewind.ConfigName}, for this many cycles (each cycle as long as {ActiveMonitorFrequency.ConfigName}), further rewinds will be ignored.\n" +
            $"This is so if you are rewinding by clicking the back button many times, it doesn't immediately start showing subtitles after you pass the Max Rewind threshold." +
            $"Must be a whole number greater than or equal to zero.";
        SubtitlePreferencePatterns.Description = "This allows you to define a filter for which subtitle track will be chosen. If left empty it will always choose the first subtitle track.\n" +
            "It should be a comma separated list of words or phrases, where it will try to look for any subtitle tracks that have a name that matches ALL the listed phrases.\n" +
            "You can also start a word/phrase with a hyphen (-) to require it NOT match that. So you can exclude 'SDH' subtitles by putting '-SDH' (without quotes).\n" +
            "Note: Not case sensitive, and any quotes and/or leading/trailing spaces for each item will be trimmed off. It uses the subtitle track displayed in Plex.\n" +
            $"Example to prefer English non-SDH subtitles:   {SubtitlePreferencePatterns.ConfigName}=english,-sdh";

        // Advanced settings
        ShortTimeoutLimit.Description = "The maximum time in milliseconds to wait for a response from the server before timing out between checks. Should be shorter than the active frequency. Must be a positive whole number.";
        AllowDuplicateInstance.Description = "(True/False) Allow multiple instances of the app to run at the same time. Not recommended, mostly used for debugging.";
        UseEventPolling.Description = "(True/False) Use event polling instead of timer polling. Only disable this if you have issues with maintaining the plex server connection.";
        IdleMonitorFrequency.Description = "Only applicable when NOT using event polling mode. How often to check for playback status (in seconds) when no media is playing.  Must be a positive whole number.";

        // Set default values for section dividers
        StandardSettings.Description =      "----------------------- Standard Settings -----------------------";
        StartAdvancedSettings.Description = "----------------------- Advanced Settings -----------------------";
    }

    public List<ISettingInfo> SettingsThatFailedToLoad = [];

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
            LogError($"Error for setting {ServerURL.ConfigName}: Server URL is empty or null. Will use default value {def.ServerURL}");
            this.SettingsThatFailedToLoad.Add(ServerURL);
            ServerURL = def.ServerURL;
        }

        // Active Monitor Frequency
        if (ActiveMonitorFrequency < 0)
        {
            LogError($"Error for setting {ActiveMonitorFrequency.ConfigName}: Active Monitor Frequency must be greater than or equal to 0.\nWill use default value {def.ActiveMonitorFrequency}");
            this.SettingsThatFailedToLoad.Add(ActiveMonitorFrequency);
            ActiveMonitorFrequency = def.ActiveMonitorFrequency;
        }

        // Idle Monitor Frequency
        if (IdleMonitorFrequency < 0)
        {
            LogError($"Error for setting {IdleMonitorFrequency.ConfigName}: Idle Monitor Frequency must be greater than or equal to 0.\nWill use default value {def.IdleMonitorFrequency}");
            this.SettingsThatFailedToLoad.Add(IdleMonitorFrequency);
            IdleMonitorFrequency = def.IdleMonitorFrequency;
        }

        // Short Timeout Limit
        if (ShortTimeoutLimit < 0)
        {
            LogError($"Error for fetting {ShortTimeoutLimit.ConfigName}: Active Monitor Timeout Limit must be greater than or equal to 0.\nWill use default value {def.ShortTimeoutLimit}");
            this.SettingsThatFailedToLoad.Add(ShortTimeoutLimit);
            ShortTimeoutLimit = def.ShortTimeoutLimit;
        }
        else if (ShortTimeoutLimit > ActiveMonitorFrequency * 1000)
        {
            LogError($"Error for fetting {ShortTimeoutLimit.ConfigName}: Active Monitor Timeout Limit must be less than Active Monitor Frequency.\nWill use default value {def.ShortTimeoutLimit}");
            this.SettingsThatFailedToLoad.Add(ShortTimeoutLimit);
            ShortTimeoutLimit = def.ShortTimeoutLimit;
        }

        // Max Rewind
        if (MaxRewind < 0)
        {
            LogError($"Error for setting {MaxRewind.ConfigName}: Max Rewind must be greater than or equal to 0.\nWill use default value {def.MaxRewind}");
            this.SettingsThatFailedToLoad.Add(MaxRewind);
            MaxRewind = def.MaxRewind;
        }

        // Cool Down Count
        if (CoolDownCount < 0)
        {
            LogError($"Error for setting {CoolDownCount.ConfigName}: Cool Down Count must be greater than or equal to 0.\nWill use default value {def.CoolDownCount}");
            this.SettingsThatFailedToLoad.Add(CoolDownCount);
            CoolDownCount = def.CoolDownCount;
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
                        LogWarning($"Warning for setting {SubtitlePreferencePatterns.ConfigName}: Subtitle Preference Pattern list contains empty item which will be ignored.");
                        // Remove the empty item from the list
                        SubtitlePreferencePatterns.Value.Remove(pattern);
                    }
                    else if (string.IsNullOrWhiteSpace(trimmedPattern))
                    {
                        LogWarning($"Warning for setting {SubtitlePreferencePatterns.ConfigName}: Subtitle Preference Pattern list contains item that was empty after trimming whitespace and quotes and will be ignored.");
                        SubtitlePreferencePatterns.Value.Remove(pattern);
                    }
                }
            }
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

    // Load settings from settings file into a Settings object
    public static Settings LoadSettings()
    {
        CreateSettingsFileIfNotExists();
        Settings settings = new Settings();
        Type settingsType = typeof(Settings); // Or typeof(Settings) - either works

        // Create a lookup dictionary for field name to config name for easier matching
        Dictionary<string, string> fieldToConfigName = [];

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
                }
                else
                {
                    // Optional: Warn if a setting is found without a usable ConfigName
                    LogError($"Warning: Field '{field.Name}' (Type: {settingInfo.GetType().Name}) has a null or empty ConfigName.");
                }
            }
        }

        // Load settings from file
        foreach (string line in File.ReadAllLines(SettingStrings.SettingsFileName))
        {
            // Local function
            bool checkIsCommentLine(string checkLine)
            {
                checkLine = checkLine.Trim().Trim('\t');
                return checkLine.StartsWith("#") || checkLine.StartsWith("//");
            }
            // --------------------------------------------------

            // If it starts with a comment character, skip it
            if (checkIsCommentLine(line))
                continue;

            string[] parts = line.Split('=');
            if (parts.Length == 2)
            {
                string configName = parts[0].Trim();
                string settingValue = parts[1].Trim().Trim('"');

                // Find the field with this config name (Loop using fieldToConfigName)
                foreach (KeyValuePair<string, string> kvp in fieldToConfigName)
                {
                    if (kvp.Value == configName) // Found the config name match
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
                                    // Use the interface method to set the value - NO REFLECTION HERE
                                    settingInfo.SetValueFromString(settingValue); //
                                }
                                catch (FormatException ex)
                                {
                                    // Handle errors during conversion/setting reported by SetValueFromString
                                    LogError($"Error applying setting '{configName}' in {SettingStrings.SettingsFileName}- Likely invalid value: {ex.Message}\nInner Exception (If Any): {ex.InnerException?.Message}");
                                    settings.SettingsThatFailedToLoad.Add(settingInfo);
                                }
                            }
                            else
                            {
                                // Handle case where the field value isn't an ISettingInfo (shouldn't happen if fieldToConfigName is built correctly)
                                LogWarning($"Warning: Field '{kvp.Key}' associated with config '{configName}' did not contain an ISettingInfo object.");
                            }
                        }
                        else
                        {
                            // Handle case where field name from dictionary doesn't exist in Settings class
                            LogError($"Warning: Field name '{kvp.Key}' (for config '{configName}') not found in Settings class.");
                        }
                        break; // Found the setting, exit the inner loop
                    }
                }
            }
        }

        // Validate and clean settings. Will restore default values for each setting if invalid
        settings.CleanAndValidate();

        // Display super-error warning if any settings failed to load
        if (settings.SettingsThatFailedToLoad.Count > 0)
        {
            string failedSettings = string.Join(", ", settings.SettingsThatFailedToLoad.Select(s => s.ConfigName));
            WriteErrorSuper($"\nWarning: The following settings failed to load. See errors above and check them in your settings file:");
            WriteRed($"\t\t{failedSettings}\n");
            
        }

        return settings;
    }

    private static bool CreateSettingsFile(string fileName, Settings? settingsIn = null)
    {
        Settings settings;

        if (settingsIn != null)
            settings = settingsIn;
        else
            settings = new Settings(); // Load default settings if no settings object is provided

        try
        {
            // Assuming SettingStrings.SettingsFileName is defined elsewhere
            using StreamWriter sw = File.CreateText(fileName);

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
                        string[] descriptionLines = description.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        foreach (string line in descriptionLines)
                        {
                            sw.WriteLine($"\t# {line}");
                        }
                    }

                    // Check against null/empty for configName and null for defaultValue
                    // (configName is non-null string from interface, but check for empty might still be desired)
                    if (!string.IsNullOrEmpty(configName) && defaultValue != null)
                    {
                        // Consider potential formatting for defaultValue depending on its type
                        // .ToString() might not always be the desired file format (e.g., for booleans, dates)
                        string valueAsString;

                        // Currently just need a special case for lists
                        if (defaultValue is List<string> list)
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
                        if (string.IsNullOrEmpty(configName)) Console.WriteLine($" - ConfigName is missing.");
                        if (defaultValue == null) Console.WriteLine($" - DefaultValue is null.");
                    }
                }
            }

            // sw is automatically disposed/flushed by the using statement
            sw.Close();
            WriteGreen($"Created settings config file \"{fileName}\"\n");
            return true; // Indicate success
        }
        catch (Exception ex)
        {
            // Handle exceptions (e.g., file access issues)
            WriteRed($"Error creating settings file: {ex.Message}");
            // Optionally log the inner exception: Console.WriteLine(ex.InnerException);
            return false; // Indicate failure
        }
    }

    // Automatically create settings file if it doesn't exist
    public static bool CreateSettingsFileIfNotExists()
    {
        if (!File.Exists(SettingStrings.SettingsFileName))
        {
            CreateSettingsFile(SettingStrings.SettingsFileName);
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
        if (File.Exists(SettingStrings.SettingsFileName))
        {
            // If it exists, create a backup
            string backupFileName = SettingStrings.SettingsFileName + ".bak";
            backupFileName = Utils.GetAvailableFileName(backupFileName, returnFullPath: false, mode: Utils.FileNameIterationLocation.Extension);
            try
            {
                File.Copy(sourceFileName: SettingStrings.SettingsFileName, destFileName: backupFileName, overwrite: true);
                WriteGreen($"Backup of settings file created as \"{SettingStrings.SettingsFileName}.bak\"\n");
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file access issues)
                WriteRed($"Error - Cannot update settings file. Failed while creating backup settings file: {ex.Message}");
                // Optionally log the inner exception: Console.WriteLine(ex.InnerException);
                return false; // Indicate failure
            }
        }
        // Create a new settings file with the updated settings
        bool result = CreateSettingsFile(fileName: SettingStrings.SettingsFileName, settingsIn: settings);

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
        CreateSettingsFile(fileName: SettingStrings.SettingsFileTemplate);
    }


} // --------- End of SettingsHandler ---------


// Non-generic interface
public interface ISettingInfo
{
    string ConfigName { get; }
    string Description { get; }
    object? GetValueAsObject(); // Method to get the value as object
    Type ValueType { get; }     // Property to get the underlying type T
    // Potentially add IsRequired if needed
    bool IsRequired { get; }
    void SetValueFromString(string stringValue);
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
    public bool IsRequired { get; set; } = true; // Added IsRequired property

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

    // --- ISettingInfo Implementation ---
    string ISettingInfo.ConfigName => this.ConfigName;
    string ISettingInfo.Description => this.Description;
    object? ISettingInfo.GetValueAsObject() => this.Value; // Boxes value types
    Type ISettingInfo.ValueType => typeof(T);
    bool ISettingInfo.IsRequired => this.IsRequired;

    // Implementation of the new method
    void ISettingInfo.SetValueFromString(string stringValue)
    {
        try
        {
            // Special handling for List<string>
            if (typeof(T) == typeof(List<string>))
            {
                // Split the string by commas and trim whitespace
                string[] items = stringValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                List<string> listValue = new(items.Select(item => item.Trim()).ToList());
                this.Value = (T)(object)listValue; // Cast to object first to avoid invalid cast exception
                return;
            }
            else
            {
                // Perform the conversion from the input string to Type T internally
                // This uses the known type T of this specific SettingInfo instance
                this.Value = (T)Convert.ChangeType(stringValue, typeof(T));
                return;
            }
        }
        catch (Exception ex)
        {
            // Handle potential conversion errors (FormatException, InvalidCastException, etc.)
            // You might want to log this, throw a more specific exception, or set a default.
            throw new FormatException($"Failed to convert string '{stringValue}' to type {typeof(T).Name} for setting '{this.ConfigName}'. See inner exception for details.", ex);
        }
    }
}