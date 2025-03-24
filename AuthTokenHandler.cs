using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;

namespace PlexShowSubtitlesOnRewind;
public static class AuthTokenHandler
{
    private static class AuthStrings
    {
        public const string TokenPlaceholder = "whatever_your_app_token_is";
        public const string UUIDPlaceholder = "some-unique-uuid-here-xxxx";
        public const string PlexTvUrl = "https://plex.tv";
        public const string PlexPinUrl = $"{PlexTvUrl}/api/v2/pins";
        public const string UserAuthConfirmationURL = "https://app.plex.tv/auth#?";
        public const string configUUIDSetting = "ClientIdentifier";
        public const string configTokenSetting = "AppToken";
        public const string configFileName = "token.config";
    }

    static void CreateTokenFile(string token = AuthStrings.TokenPlaceholder, string uuid = AuthStrings.UUIDPlaceholder)
    {
        string tokenFilePath = AuthStrings.configFileName;
        File.WriteAllText(tokenFilePath, $"AppToken={token}\nClientIdentifier={uuid}");
    }

    public static (string, string)? LoadTokens()
    {
        string tokenFilePath = AuthStrings.configFileName;
        bool tokenFileExists = false;

        // If it doesn't exist, prompt the user to go through the auth flow to create one
        if (!File.Exists(tokenFilePath))
        {
            // Prompt the user if they want to go through the auth flow to generate a token
            Console.WriteLine($"Required \"{AuthStrings.configFileName}\" file not found. Do you want to go through the necessary authorization flow?\n");
            Console.Write("Enter your choice (y/n): ");
            string? userInput = Console.ReadLine();
            if (userInput != null && userInput.Equals("y", StringComparison.CurrentCultureIgnoreCase))
            {
                bool authFlowResult = FullAuthFlow();
                if (authFlowResult)
                {
                    tokenFileExists = true;
                }
                else
                {
                    Console.WriteLine($"Token generation failed. Please check the {AuthStrings.configFileName} file.\n");
                }
            }
            else
            {
                // Create a placeholder tokens file
                CreateTokenFile(AuthStrings.TokenPlaceholder);
                Console.WriteLine($"Please edit the {AuthStrings.configFileName} file with your Plex app and/or personal tokens.\n");
            }
        }
        else
        {
            tokenFileExists = true;
        }

        // If the token file already exists or was created, read the tokens from the file
        if (tokenFileExists == false)
        {
            return null;
        }
        else if (tokenFileExists == true)
        {
            // Read tokens from file
            string[] lines = File.ReadAllLines(tokenFilePath);
            string? validatedToken = null;
            string? validatedUUID = null;


            foreach (string line in lines)
            {
                if (line.StartsWith($"{AuthStrings.configTokenSetting}="))
                {
                    string rawToken = line.Substring($"{AuthStrings.configTokenSetting}=".Length);
                    validatedToken = validateToken(rawToken);
                }

                if (line.StartsWith($"{AuthStrings.configUUIDSetting}="))
                {
                    string rawUUID = line.Substring($"{AuthStrings.configUUIDSetting}=".Length);
                    validatedUUID = validateToken(rawUUID);
                }

                if (validatedToken != null && validatedUUID != null)
                {
                    return (validatedToken, validatedUUID);
                }
            }
        }

        // If no valid token is found, return null
        return null;

        // ---------------- Local Functions ----------------

        // Local function to validate the token
        static string? validateToken(string token)
        {
            // Trim whitespace and check length
            token = token.Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine($"Auth token is empty or not found. Update {AuthStrings.configFileName}.");
                return null;
            }
            else if (token == AuthStrings.TokenPlaceholder || token == AuthStrings.UUIDPlaceholder)
            {
                Console.WriteLine($"Update {AuthStrings.configFileName} to use your actual auth token for your plex server.\n" +
                    "Or delete the config and run the app again to go through the token generation steps.");
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
        TokenGenResult genResult = GenerateAppTokenRequest(appName: MyStrings.AppName, url: AuthStrings.PlexPinUrl);

        string authUrl;
        // The code and ID should never be null for a success, but check anyway
        if (genResult.Success && genResult.Code != null && genResult.PinID != null && genResult.ClientIdentifier != null)
        {
            // Generate the auth URL and tell the user to visit it
            authUrl = GenerateAuthURL(clientIdentifier: genResult.ClientIdentifier, code: genResult.Code, appName: MyStrings.AppName);

            Console.WriteLine("\n----------------------------------------------------------------");
            Console.WriteLine($"\nPlease visit the following URL to authorize the app: \n\n\t{authUrl}");
            Console.WriteLine("\n\n\tTip: You can check if it shows up here (replace the IP with your server IP and port if necessary):" +
                "\n\thttp://127.0.0.1:32400/web/index.html#!/settings/devices/all");

            Console.WriteLine("\n----------------------------------------------------------------");
            Console.WriteLine("Press Enter to continue after you have authorized the app.");
            // Wait for user to press a key
            Console.ReadLine();
        }
        else
        {
            Console.WriteLine($"Token generation failed. Please check the {AuthStrings.configFileName} file.");
            return false;
        }

        // Get the token after user confirmation. At this point the user should have authorized the app and has hit a key
        // If the token is not null, the user may not have authorized the app, so
        bool authSuccess = false;
        while (authSuccess == false)
        {
            string? authToken = GetAuthorizedTokenAfterUserConfirmation(pinID: genResult.PinID, appName: MyStrings.AppName, clientIdentifier: genResult.ClientIdentifier);
            if (authToken != null)
            {
                authSuccess = true;
                successResult = true;
            }
            else
            {
                Console.WriteLine("----------------------------------------------------");
                Console.WriteLine("\nThe app does not appear authorized.\nVisit this URL and sign in if you haven't already, ");
                Console.WriteLine($"\n\t{authUrl}");
                Console.WriteLine("\nThen press Enter to check again.");
                Console.ReadLine();
            }
        }


        return successResult;
    }

    public static TokenGenResult GenerateAppTokenRequest(string appName, string url, bool strong = true)
    {
        HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30) // Set the timeout to 30 seconds
        };

        if (strong)
        {
            url += "?strong=true";
        }

        // Client ID should be unique for each instance of the app, aka per user. A GUID is a good choice.
        // It is used in "X-Plex-Client-Identifier" header
        string uuid = Guid.NewGuid().ToString();

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("accept", "application/json");
        request.Headers.Add("X-Plex-Client-Identifier", uuid);
        request.Headers.Add("X-Plex-Product", appName);

        StringContent content = new StringContent(string.Empty);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Content = content;

        try
        {
            HttpResponseMessage response = client.Send(request);

            if (response.IsSuccessStatusCode)
            {
                string resultString = response.Content.ReadAsStringAsync().Result;
                TokenGenResult result = ParseTokenGenJsonResponse(resultString);
                result.ClientIdentifier = uuid;

                return result;
            }
            else
            {
                Console.WriteLine($"Request failed with status code: {response.StatusCode}");
                return new TokenGenResult(false);
            }

        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"Request error: {e.Message}");
            return new TokenGenResult(false);
        }
        catch (TaskCanceledException e)
        {
            Console.WriteLine($"Request timed out: {e.Message}");
            return new TokenGenResult(false);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Unexpected error: {e.Message}");
            return new TokenGenResult(false);
        }
    }

    public static string GenerateAuthURL(string clientIdentifier, string code, string appName)
    {
        // Define the base URL
        string baseUrl = AuthStrings.UserAuthConfirmationURL;

        // Construct the user auth URL with the provided variables
        string userAuthUrl = $"{baseUrl}clientID={clientIdentifier}&code={code}&context%5Bdevice%5D%5Bproduct%5D={Uri.EscapeDataString(appName)}";

        return userAuthUrl;
    }

    public static string? GetAuthorizedTokenAfterUserConfirmation(string pinID, string appName, string clientIdentifier, string baseURL = AuthStrings.PlexPinUrl)
    {
        // Generates url like https://plex.tv/api/v2/pins/{pinID}
        // With headers for X-Plex-Product and X-Plex-Client-Identifier

        string pinURL = $"{baseURL}/{pinID}";

        HttpClient client = new HttpClient();
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, pinURL);
        request.Headers.Add("X-Plex-Client-Identifier", clientIdentifier);
        request.Headers.Add("X-Plex-Product", appName);
        HttpResponseMessage response = client.Send(request);

        if (response.IsSuccessStatusCode)
        {
            string resultString = response.Content.ReadAsStringAsync().Result;
            string authToken = ParseAuthXmlResponse(resultString);

            if (!String.IsNullOrEmpty(authToken))
            {
                // Save the token to the file
                CreateTokenFile(authToken, clientIdentifier);
                return authToken;
            }
            else
            {
                return null;
            }
        }
        else
        {
            Console.WriteLine($"Request failed with status code: {response.StatusCode}");
            return null;
        }
    }

    public static string ParseAuthXmlResponse(string xmlResponse)
    {
        string authToken = "";
        try
        {
            // Load the XML response
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xmlResponse);
            // Get the auth token from the XML attribute
            XmlNode? pinNode = doc.SelectSingleNode("//pin");
            if (pinNode != null && pinNode.Attributes != null)
            {
                XmlAttribute? authTokenAttr = pinNode.Attributes?["authToken"];
                if (authTokenAttr != null)
                {
                    authToken = authTokenAttr.Value;
                    Console.WriteLine($"Successfully created auth token: {authToken}\nIt will automatically be stored in the file {AuthStrings.configFileName}\n");
                }
                else
                {
                    Console.WriteLine("Received invalid token response.");
                }
            }
            else
            {
                Console.WriteLine("Received invalid token response.");
            }
            return authToken;
        }
        catch (XmlException ex)
        {
            Console.WriteLine($"Failed to parse XML response: {ex.Message}");
            return "";
        }
    }

    // Parse the json response to get the id and code
    public static TokenGenResult ParseTokenGenJsonResponse(string jsonResponse)
    {
        bool success = false;
        try
        {
            // Deserialize to JsonElement (general JSON representation)
            JsonElement deserializedObj = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

            string? id = null;
            string? code = null;

            if (deserializedObj.TryGetProperty("id", out JsonElement idElement))
            {
                id = idElement.GetRawText();
            }

            if (deserializedObj.TryGetProperty("code", out JsonElement codeElement))
            {
                code = codeElement.GetRawText();
            }

            if (id != null && code != null)
            {
                success = true;

                // Strip quotes from the strings, including escaped quotes
                id = id.Trim('"').Trim('\\').Trim('"');
                code = code.Trim('"').Trim('\\').Trim('"');

                //Console.WriteLine($"Token Client ID: {id}");
                //Console.WriteLine($"Token Code: {code}");
            }
            else
            {
                Console.WriteLine("Received invalid token response.");
            }

            return new TokenGenResult(success, id, code);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse JSON response: {ex.Message}");
            return new TokenGenResult(false);
        }
    }

    public class TokenGenResult(bool success, string? id = null, string? code = null, string? clientIdentifier = null)
    {
        public bool Success { get; set; } = success;
        public string? PinID { get; set; } = id;
        public string? Code { get; set; } = code;
        public string? ClientIdentifier { get; set; } = clientIdentifier;
    }

} // ---------------- End class AuthTokenHandler ----------------

