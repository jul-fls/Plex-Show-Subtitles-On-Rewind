using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlexShowSubtitlesOnRewind;
internal static class AuthTokenHandler
{
    private static class AuthStrings
    {
        public const string TokenPlaceholder = "whatever_your_app_token_is";
        public const string PlexTvUrl = "https://plex.tv";
        public const string PlexPinUrl = $"{PlexTvUrl}/api/v2/pins?strong=true";
    }

    static void CreateTokenFile(string token = AuthStrings.TokenPlaceholder)
    {
        string tokenFilePath = "tokens.config";
        File.WriteAllText(tokenFilePath, $"AppToken={token}\n");
    }

    public static string? LoadTokens()
    {
        string tokenFilePath = "tokens.config";

        if (!File.Exists(tokenFilePath))
        {
            // Prompt the user if they want to go through the auth flow to generate a token
            Console.WriteLine("Required \"tokens.config\" file not found. Do you want to go through the necessary authorization flow?\n");
            Console.Write("Enter your choice (y/n): ");
            string? userInput = Console.ReadLine();
            if (userInput != null && userInput.Equals("y" as string, StringComparison.CurrentCultureIgnoreCase))
            {
                bool result = FullAuthFlow();
                if (result)
                {
                    Console.WriteLine("Token generation successful. Please check the tokens.config file.");
                }
                else
                {
                    Console.WriteLine("Token generation failed. Please check the tokens.config file.");
                }
            }
            else
            {
                // Create a placeholder tokens file
                CreateTokenFile(AuthStrings.TokenPlaceholder);
                Console.WriteLine("Please edit the tokens.config file with your Plex app and/or personal tokens.");
                return null;
            }
        }
        else
        {
            // Read tokens from file
            string[] lines = File.ReadAllLines(tokenFilePath);
            foreach (string line in lines)
            {
                if (line.StartsWith("AppToken="))
                {
                    string rawToken = line.Substring("AppToken=".Length);
                    string? validatedToken = validateToken(rawToken);

                    return validatedToken; // Will return even if null, to be checked by the caller
                }
            }
        }

        // If no valid token is found, return null
        return null;

        // Local function to validate the token
        static string? validateToken(string token)
        {
            // Trim whitespace and check length
            token = token.Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("Auth token is empty or not found. Update tokens.config.");
                return null;
            }
            else if (token == AuthStrings.TokenPlaceholder)
            {
                Console.WriteLine("Update tokens.config to use your actual auth token for your plex server.");
                return null;
            }
            else
            {
                return token;
            }
        }
    }

    static bool FullAuthFlow()
    {
        bool successResult = false;
        // Generate the token request
        _ = GenerateAppTokenRequest(MyStrings.AppName, MyStrings.App_ID_GUID);

        return successResult;
    }

    public static async Task<string> GenerateAppTokenRequest(string appName, string uuid)
    {
        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("X-Plex-Client-Identifier", uuid);
        client.DefaultRequestHeaders.Add("X-Plex-Product", appName);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        string requestUri = AuthStrings.PlexPinUrl;
        StringContent content = new(content: string.Empty, encoding: Encoding.UTF8, mediaType: "application/json");

        HttpResponseMessage response = await client.PostAsync(requestUri, content);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private class TokenGenResult
    {
        public string? id { get; set; }
        public string? code { get; set; }
    }

} // ---------------- End class AuthTokenHandler ----------------
