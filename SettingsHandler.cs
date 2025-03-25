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
        Type settingsType = settings.GetType();

        // Create a lookup dictionary for field name to config name for easier matching
        var fieldToConfigName = new Dictionary<string, string>();
        foreach (System.Reflection.FieldInfo field in settingsType.GetFields())
        {
            if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(SettingInfo<>))
            {
                var settingValue = field.GetValue(settings);
                if (settingValue != null)
                {
                    // Get the ConfigName property value
                    var configName = field.FieldType.GetProperty("ConfigName")?.GetValue(settingValue)?.ToString();
                    if (!string.IsNullOrEmpty(configName))
                    {
                        fieldToConfigName[field.Name] = configName;
                    }
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
            Type settingsType = settings.GetType();

            using StreamWriter sw = File.CreateText(SettingStrings.SettingsFileName);

            foreach (System.Reflection.FieldInfo field in settingsType.GetFields())
            {
                if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(SettingInfo<>))
                {
                    object? settingInfo = field.GetValue(settings);
                    if (settingInfo != null)
                    {
                        string? description = field.FieldType.GetProperty("Description")?.GetValue(settingInfo)?.ToString();
                        string? configName = field.FieldType.GetProperty("ConfigName")?.GetValue(settingInfo)?.ToString();
                        object? defaultValue = field.FieldType.GetProperty("Value")?.GetValue(settingInfo);

                        // For each line in a description (whether single or multiple line description), add comment character, a tab, and the description
                        if (description != null && description != "")
                        {
                            string[] descriptionLines = description.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            foreach (string line in descriptionLines)
                            {
                                sw.WriteLine($"\t# {line}");
                            }
                        }

                        if (configName != null && defaultValue != null)
                        {
                            sw.WriteLine($"{configName}={defaultValue}\n");
                        }
                    }
                }
            }

            sw.Close();
            WriteGreen($"Created settings config file \"{SettingStrings.SettingsFileName}\"\n");
        }
    }
} // --------- End of SettingsHandler ---------


// Generic setting wrapper that supports metadata
public class SettingInfo<T>
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
    public bool IsRequired { get; set; } = true;

    public SettingInfo(T defaultValue, string configName)
    {
        _value = defaultValue;
        ConfigName = configName;
    }

    // Implicit conversions to maintain usage simplicity
    public static implicit operator T(SettingInfo<T> setting) => setting._value;
    public static implicit operator SettingInfo<T>(T value) => new(value, string.Empty);

    public override string ToString() => _value?.ToString() ?? string.Empty;
}