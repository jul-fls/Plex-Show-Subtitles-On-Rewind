namespace RewindSubtitleDisplayerForPlex;



// Settings class with default values. Will be updated with values from settings file if it exists
public class Settings
{
    // This is the order they will be written to the settings file
    public SettingInfo<string> ServerURL = new("http://127.0.0.1:32400", "Server_URL_And_Port");
    public SettingInfo<int> ActiveMonitorFrequency = new(1, "Active_Monitor_Frequency");
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
        ActiveMonitorFrequency.Description = "How often to check for playback status (in seconds) when actively monitoring. Must be a positive whole number.";
        DebugMode.Description = "(True/False) Always default to using debug mode without having to use '-debug' launch parameter.";

        // Advanced settings
        ShortTimeoutLimit.Description = "The maximum time in milliseconds to wait for a response from the server before timing out between checks. Should be shorter than the active frequency. Must be a positive whole number.";
        AllowDuplicateInstance.Description = "(True/False) Allow multiple instances of the app to run at the same time. Not recommended, mostly used for debugging.";
        UseEventPolling.Description = "(True/False) Use event polling instead of timer polling. Only disable this if you have issues with maintaining the plex server connection.";
        IdleMonitorFrequency.Description = "Only applicable when NOT using event polling mode. How often to check for playback status (in seconds) when no media is playing.  Must be a positive whole number.";
    }

    // ------------------------------------------------------
    public Settings CleanAndValidate()
    {
        Settings def = new Settings();

        // ===================================================
        // Validate each setting as much as reasonable
        // --------------------------------------------------

        // Server URL
        ServerURL.Value = ServerURL.Value.TrimEnd('/');
        if (string.IsNullOrEmpty(ServerURL))
        {
            LogError($"Error for setting {ServerURL.ConfigName}: Server URL is empty or null. Will use default value {def.ServerURL}");
            ServerURL = def.ServerURL;
        }

        // Active Monitor Frequency
        if (ActiveMonitorFrequency < 0)
        {
            LogError($"Error for setting {ActiveMonitorFrequency.ConfigName}: Active Monitor Frequency must be greater than or equal to 0.\nWill use default value {def.ActiveMonitorFrequency}");
            ActiveMonitorFrequency = def.ActiveMonitorFrequency;
        }

        // Idle Monitor Frequency
        if (IdleMonitorFrequency < 0)
        {
            LogError($"Error for setting {IdleMonitorFrequency.ConfigName}: Idle Monitor Frequency must be greater than or equal to 0.\nWill use default value {def.IdleMonitorFrequency}");
            IdleMonitorFrequency = def.IdleMonitorFrequency;
        }

        // Short Timeout Limit
        if (ShortTimeoutLimit < 0)
        {
            LogError($"Error for fetting {ShortTimeoutLimit.ConfigName}: Active Monitor Timeout Limit must be greater than or equal to 0.\nWill use default value {def.ShortTimeoutLimit}");
            ShortTimeoutLimit = def.ShortTimeoutLimit;
        }
        if (ShortTimeoutLimit > ActiveMonitorFrequency * 1000)
        {
            LogError($"Error for fetting {ShortTimeoutLimit.ConfigName}: Active Monitor Timeout Limit must be less than Active Monitor Frequency.\nWill use default value {def.ShortTimeoutLimit}");
            ShortTimeoutLimit = def.ShortTimeoutLimit;
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
        var fieldToConfigName = new Dictionary<string, string>();

        // Iterate through fields of the Settings instance
        foreach (System.Reflection.FieldInfo field in settingsType.GetFields())
        {
            // Get the value of the field from the specific 'settings' instance
            object? settingValue = field.GetValue(settings);

            // Check if the field's value implements ISettingInfo and cast it
            if (settingValue is ISettingInfo settingInfo) // <<<< KEY CHANGE: Cast to interface
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
                    Console.WriteLine($"Warning: Field '{field.Name}' (Type: {settingInfo.GetType().Name}) has a null or empty ConfigName.");
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

                            // Check if it's an ISettingInfo and cast it
                            if (settingObj is ISettingInfo settingInfo) // <<<< Use the interface
                            {
                                try
                                {
                                    // Use the interface method to set the value - NO REFLECTION HERE
                                    settingInfo.SetValueFromString(settingValue); // <<<< KEY CHANGE
                                }
                                catch (FormatException ex)
                                {
                                    // Handle errors during conversion/setting reported by SetValueFromString
                                    Console.WriteLine($"Error applying setting '{configName}': {ex.Message}");
                                    // Optionally log the inner exception: Console.WriteLine(ex.InnerException);
                                }
                            }
                            else
                            {
                                // Handle case where the field value isn't an ISettingInfo (shouldn't happen if fieldToConfigName is built correctly)
                                Console.WriteLine($"Warning: Field '{kvp.Key}' associated with config '{configName}' did not contain an ISettingInfo object.");
                            }
                        }
                        else
                        {
                            // Handle case where field name from dictionary doesn't exist in Settings class
                            Console.WriteLine($"Warning: Field name '{kvp.Key}' (for config '{configName}') not found in Settings class.");
                        }
                        break; // Found the setting, exit the inner loop
                    }
                }
            }
        }

        // Validate and clean settings. Will restore default values for each setting if invalid
        settings.CleanAndValidate();

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
                if (fieldValue is ISettingInfo settingInfo) // <<<< KEY CHANGE: Cast to interface
                {
                    // Access properties directly via the interface - NO REFLECTION NEEDED
                    string description = settingInfo.Description;
                    string configName = settingInfo.ConfigName;
                    object? defaultValue = settingInfo.GetValueAsObject();

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
                        sw.WriteLine($"{configName}={defaultValue}");
                        sw.WriteLine(); // Add blank line
                    }
                    else
                    {
                        // Optional: Log or handle cases where essential info is missing
                        Console.WriteLine($"Warning: Skipping field '{field.Name}'. ConfigName or DefaultValue is missing/null.");
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
            // Perform the conversion from the input string to Type T internally
            // This uses the known type T of this specific SettingInfo instance
            this.Value = (T)Convert.ChangeType(stringValue, typeof(T));
        }
        catch (Exception ex)
        {
            // Handle potential conversion errors (FormatException, InvalidCastException, etc.)
            // You might want to log this, throw a more specific exception, or set a default.
            throw new FormatException($"Failed to convert string '{stringValue}' to type {typeof(T).Name} for setting '{this.ConfigName}'. See inner exception for details.", ex);
        }
    }
}