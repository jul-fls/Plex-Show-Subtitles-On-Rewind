using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlexShowSubtitlesOnRewind;

// Settings class with default values. Will be updated with values from settings file if it exists
public class Settings
{
    public string ServerURL = "http://127.0.0.1:32400";

    public bool CleanAndValidate()
    {
        // Clean each setting where necessary
        // --------------------------------------------------

        // Server URL
        ServerURL = ServerURL.Trim().Trim('"').TrimEnd('/');

        // Validate each setting as much as reasonable
        // --------------------------------------------------

        // Server URL
        if (ServerURL == null || ServerURL.Length == 0)
        {
            Console.WriteLine("Error: Server URL is empty or null");
            return false;
        }

        // If no issues found, return true
        return true;
    }
}

// Function to create a settings file if it doesn't exist using settings names and default values
public static class SettingsHandler
{
    // Various Enums / Pseudo Enums
    private static class SettingStrings
    {
        public const string SettingsFileName = "settings.config";
    }

    private static class SettingsNames
    {
        public const string ServerURL = "Server_URL_And_Port";
    }

    // Load settings from settings file into a Settings object
    public static Settings LoadSettings()
    {
        CreateSettingsFileIfNotExists();
        Settings settings = new Settings();
        Type settingNames = typeof(SettingsNames);
        Type settingsType = settings.GetType();
        foreach (var line in File.ReadAllLines(SettingStrings.SettingsFileName))
        {
            string[] parts = line.Split('=');
            if (parts.Length == 2)
            {
                string settingName = parts[0];
                string settingValue = parts[1];
                foreach (System.Reflection.FieldInfo field in settingNames.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy))
                {
                    if (field.IsLiteral && !field.IsInitOnly)
                    {
                        string? settingNameField = field.GetValue(null)?.ToString();
                        if (settingNameField != null && settingNameField == settingName)
                        {
                            System.Reflection.FieldInfo? settingsField = settingsType.GetField(field.Name);
                            if (settingsField != null)
                            {
                                settingsField.SetValue(settings, Convert.ChangeType(settingValue, settingsField.FieldType));
                            }
                        }
                    }
                }
            }
        }

        // Validate and clean settings. Return defaults if invalid
        if (settings.CleanAndValidate())
        {
            return settings;
        }
        else
        {
            Console.WriteLine("Invalid settings found - see any errors above. Using default settings.");
            return new Settings();
        }
    }

    // Automatically create settings file if it doesn't exist using settings names and default values from the Settings class and SettingsNames class
    public static void CreateSettingsFileIfNotExists()
    {
        if (!File.Exists(SettingStrings.SettingsFileName))
        {
            var settings = new Settings();
            var settingNames = typeof(SettingsNames);
            var settingsType = settings.GetType();

            using StreamWriter sw = File.CreateText(SettingStrings.SettingsFileName);

            foreach (var field in settingNames.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy))
            {
                if (field.IsLiteral && !field.IsInitOnly)
                {
                    string? settingName = field.GetValue(null)?.ToString();
                    if (settingName != null)
                    {
                        var settingsField = settingsType.GetField(field.Name);

                        if (settingsField != null)
                        {
                            object? defaultValue = settingsField.GetValue(settings);
                            sw.WriteLine($"{settingName}={defaultValue}");
                        }
                    }
                }
            }

            sw.Close();
            Console.WriteLine($"Created settings config file \"{SettingStrings.SettingsFileName}\"\n");
        }
    }


}
