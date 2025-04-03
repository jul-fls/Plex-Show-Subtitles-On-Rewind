namespace PlexShowSubtitlesOnRewind;



// Settings class with default values. Will be updated with values from settings file if it exists
public class Settings
{
    public SettingInfo<string> ServerURL = new("http://127.0.0.1:32400", "Server_URL_And_Port");
    public SettingInfo<int> ActiveMonitorFrequency = new(1, "Active_Monitor_Frequency");
    public SettingInfo<int> IdleMonitorFrequency = new(30, "Idle_Monitor_Frequency");
    public SettingInfo<int> ShortTimeoutLimit = new(500, "Active_Timeout_Milliseconds");

    // Constructor to set descriptions for each setting
    public Settings()
    {
        // Set descriptions in the constructor
        ServerURL.Description = "The full URL of your local server, including http, IP, and port";
        ActiveMonitorFrequency.Description = "How often to check for playback status (in seconds) when actively monitoring. Must be a positive whole number.";
        IdleMonitorFrequency.Description = "How often to check for playback status (in seconds) when no media is playing.  Must be a positive whole number.";
        ShortTimeoutLimit.Description = "The maximum time in milliseconds to wait for a response from the server before timing out between checks. Should be shorter than the active frequency.";
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
            WriteError($"Error for setting {ServerURL.ConfigName}: Server URL is empty or null. Will use default value {def.ServerURL}");
            ServerURL = def.ServerURL;
        }

        // Active Monitor Frequency
        if (ActiveMonitorFrequency < 0)
        {
            WriteError($"Error for setting {ActiveMonitorFrequency.ConfigName}: Active Monitor Frequency must be greater than or equal to 0.\nWill use default value {def.ActiveMonitorFrequency}");
            ActiveMonitorFrequency = def.ActiveMonitorFrequency;
        }

        // Idle Monitor Frequency
        if (IdleMonitorFrequency < 0)
        {
            WriteError($"Error for setting {IdleMonitorFrequency.ConfigName}: Idle Monitor Frequency must be greater than or equal to 0.\nWill use default value {def.IdleMonitorFrequency}");
            IdleMonitorFrequency = def.IdleMonitorFrequency;
        }

        // Short Timeout Limit
        if (ShortTimeoutLimit < 0)
        {
            WriteError($"Error for fetting {ShortTimeoutLimit.ConfigName}: Active Monitor Timeout Limit must be greater than or equal to 0.\nWill use default value {def.ShortTimeoutLimit}");
            ShortTimeoutLimit = def.ShortTimeoutLimit;
        }
        if (ShortTimeoutLimit > ActiveMonitorFrequency * 1000)
        {
            WriteError($"Error for fetting {ShortTimeoutLimit.ConfigName}: Active Monitor Timeout Limit must be less than Active Monitor Frequency.\nWill use default value {def.ShortTimeoutLimit}");
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

                // Find the field with this config name
                foreach (KeyValuePair<string, string> kvp in fieldToConfigName)
                {
                    if (kvp.Value == configName)
                    {
                        System.Reflection.FieldInfo? field = settingsType.GetField(kvp.Key);
                        if (field != null)
                        {
                            // Get the generic type parameter of SettingInfo<T>
                            Type valueType = field.FieldType.GetGenericArguments()[0];

                            // Get the current SettingInfo instance
                            object? settingInfo = field.GetValue(settings);

                            // Set Value property with converted value
                            System.Reflection.PropertyInfo? valueProperty = field.FieldType.GetProperty("Value");
                            if (valueProperty != null)
                            {
                                object convertedValue = Convert.ChangeType(settingValue, valueType);
                                valueProperty.SetValue(settingInfo, convertedValue);
                            }
                        }
                    }
                }
            }
        }

        // Validate and clean settings. Will restore default values for each setting if invalid
        settings.CleanAndValidate();

        return settings;
    } 

    // Automatically create settings file if it doesn't exist
    public static void CreateSettingsFileIfNotExists()
    {
        if (!File.Exists(SettingStrings.SettingsFileName))
        {
            Settings settings = new Settings();
            Type settingsType = typeof(Settings); // No change here

            // Assuming SettingStrings.SettingsFileName is defined elsewhere
            using StreamWriter sw = File.CreateText(SettingStrings.SettingsFileName);

            // Iterate through fields of the Settings instance
            foreach (System.Reflection.FieldInfo field in settingsType.GetFields())
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
            WriteGreen($"Created settings config file \"{SettingStrings.SettingsFileName}\"\n");
        }
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
    // --- End ISettingInfo Implementation ---
}