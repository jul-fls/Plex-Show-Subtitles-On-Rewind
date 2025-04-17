using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace RewindSubtitleDisplayerForPlex;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(AuthTokenHandler.TokenGenJson))]
// --- Remove the JsonElement line ---
// [JsonSerializable(typeof(JsonElement))] // No longer needed for this approach
internal partial class AuthResponseJsonContext : JsonSerializerContext
{
}



public static class AuthTokenHandler
{
    public static class AuthStrings
    {
        public const string TokenPlaceholder = "whatever_your_app_token_is";
        public const string ClientID_UUID_Placeholder = "some-unique-uuid-here-xxxx";
        public const string ClientID_Pending = "pending_auth";
        public const string PlexTvUrl = "https://plex.tv";
        public const string PlexPinUrl = $"{PlexTvUrl}/api/v2/pins";
        public const string UserAuthConfirmationURL = "https://app.plex.tv/auth#?";
        public const string configUUIDSetting = "ClientIdentifier";
        public const string configTokenSetting = "AppToken";
        public const string configPinIDSetting = "PinID";
        public const string tokenFileName = "token.config";
        public static readonly string tokenFileTemplateName = $"{tokenFileName}.example";
        public static readonly string tokenNote = "    Note: This is not the same Plex token for your account that you see mentioned in other tutorials.\n" +
                                         "        This is a token specifically for the individual app.\n" +
                                         "        It will appear in the 'Devices' section of your account and can be revoked whenever you want.";
    }

    public static string TokenFilePath => Path.Combine(BaseConfigsDir, AuthStrings.tokenFileName);
    public static string TokenTemplatePath => Path.Combine(BaseConfigsDir, AuthStrings.tokenFileTemplateName);

    static void CreateTokenFile(string token = AuthStrings.TokenPlaceholder, string uuid = AuthStrings.ClientID_UUID_Placeholder)
    {
        File.WriteAllText(TokenFilePath, 
                $"{AuthStrings.configTokenSetting}={token}" +
                $"\n{AuthStrings.configUUIDSetting}={uuid}");
    }

    static void CreatePendingTokenFile(string pinID, string clientID_UUID, string tempAuthCode)
    {
        File.WriteAllText(TokenFilePath, 
                    $"{AuthStrings.configTokenSetting}={tempAuthCode}\n" +
                    $"{AuthStrings.configUUIDSetting}={clientID_UUID}\n" +
                    $"{AuthStrings.configPinIDSetting}={pinID}\n"
                    );
    }

    public static bool CreateTemplateTokenFile(bool force = false)
    {
        try
        {
            // First check if the template file already exists
            if (force == true || !File.Exists(TokenTemplatePath))
            {
                File.WriteAllText(TokenTemplatePath, 
                    $"{AuthStrings.configTokenSetting}={AuthStrings.TokenPlaceholder}\n" +
                    $"{AuthStrings.configUUIDSetting}={AuthStrings.ClientID_UUID_Placeholder}\n"
                    );
                return true;
            }
            else
            {
                LogError($"Template file already exists: {TokenTemplatePath}.\n");
                return false;
            }
        }
        catch (Exception ex)
        {
            LogError($"Error creating token template file: {ex.Message}");
            return false;
        }

    }

    public static (string, string)? LoadTokens()
    {
        bool tokenFileExists = false;

        // If it doesn't exist, prompt the user to go through the auth flow to create one
        if (File.Exists(TokenFilePath))
        {
            tokenFileExists = true;
        }
        else if (Program.IsContainerized) // Running in docker or another non-interactive environment
        {
            NonInteractiveAuthFlow_GenerateAuth();

            // Nothing more to do, any relevant messages will be printed already
            new System.Threading.ManualResetEvent(false).WaitOne(); // Wait instead of exiting, so the container doesn't exit and start looping
        }
        else
        {
            // Prompt the user if they want to go through the auth flow to generate a token
            WriteYellow($"\nRequired \"{AuthStrings.tokenFileName}\" file not found. Do you want to go through the necessary authorization flow now?\n");
            WriteLineSafe(AuthStrings.tokenNote);
            WriteSafe("\nAuthorize App? (y/n): ");
            string? userInput = ReadlineSafe();
            if (userInput != null && userInput.Equals("y", StringComparison.CurrentCultureIgnoreCase))
            {
                bool authFlowResult = FullAuthFlow();
                if (authFlowResult)
                {
                    tokenFileExists = true;
                }
                else
                {
                    WriteRed($"Token generation failed. Please check the {AuthStrings.tokenFileName} file.\n");
                }
            }
            else
            {
                WriteYellow("\n\nYou'll need to generate an auth token for the app to work.\n" +
                    $"   - Either restart the app and go through the auth instructions flow\n" +
                    $"   - Or if you know how, you can manually make the API REST requests and use {AuthStrings.tokenFileTemplateName} to store the token.\n");
                CreateTemplateTokenFile();
            }
        }

        // If the token file already exists or was created, read the tokens from the file
        // This check is here because it might have been created in the block above
        if (tokenFileExists == false)
        {
            return null;
        }
        else if (tokenFileExists == true)
        {

            (string ? validatedToken, string ? validatedUUID, string ? validatedPinID)? tokens = ReadTokenConfig(TokenFilePath);
            
            if (tokens.Value.validatedToken != null && tokens.Value.validatedUUID != null)
            {
                // Extra step if PinID is found because we need to check if the app is authorized yet
                if (tokens.Value.validatedPinID != null)
                {
                    bool authSuccess = NonInteractiveAuthFlow_CheckAuth(
                        pinID: tokens.Value.validatedPinID, 
                        clientID: tokens.Value.validatedUUID, 
                        tempAuthCode: tokens.Value.validatedToken, 
                        appName: MyStrings.AppNameShort
                        );

                    // If auth was a failure, don't exit or else a container will loop
                    if (authSuccess == false)
                    {
                        if (Program.IsContainerized)
                            new System.Threading.ManualResetEvent(false).WaitOne();
                        else
                            Environment.Exit(1);
                    }
                }

                return (tokens.Value.validatedToken, tokens.Value.validatedUUID);

            }
        }

        // Should only get here if no token or UUID lines were found at all, because if they were found but invalid would have already returned
        return null;

        // ---------------- Local Functions ----------------

        
    }

    public static void ClearLastLine()
    {
        Console.SetCursorPosition(0, Console.CursorTop - 1);
        WriteSafe(new string(' ', Console.BufferWidth - 1));
        Console.SetCursorPosition(0, Console.CursorTop - 1);
        WriteLineSafe(); // This seems needed to prevent weird overlapping of text
    }

    public static (string? validatedToken, string? validatedUUID, string? validatedPinID) ReadTokenConfig(string tokenFilePath)
    {
        // Read tokens from file
        string[] lines = File.ReadAllLines(tokenFilePath);
        string? validatedToken = null;
        string? validatedUUID = null;
        string? validatedPinID = null;


        foreach (string line in lines)
        {
            if (line.StartsWith($"{AuthStrings.configTokenSetting}="))
            {
                string rawToken = line.Substring($"{AuthStrings.configTokenSetting}=".Length);
                validatedToken = validateTokenValue(rawToken);
            }

            if (line.StartsWith($"{AuthStrings.configUUIDSetting}="))
            {
                string rawUUID = line.Substring($"{AuthStrings.configUUIDSetting}=".Length);
                validatedUUID = validateTokenValue(rawUUID);
            }

            if (line.StartsWith($"{AuthStrings.configPinIDSetting}="))
            {
                string rawPinID = line.Substring($"{AuthStrings.configPinIDSetting}=".Length);
                validatedPinID = validateTokenValue(rawPinID);
            }
        }

        return (validatedToken, validatedUUID, validatedPinID);

        // ----------------- Local function to validate the token -----------------
        static string? validateTokenValue(string tokenConfigValue)
        {
            // Trim whitespace and check length
            tokenConfigValue = tokenConfigValue.Trim();

            if (string.IsNullOrWhiteSpace(tokenConfigValue))
            {
                LogError($"Auth token is empty or not found. Update {AuthStrings.tokenFileName}.");
                return null;
            }
            else if (tokenConfigValue == AuthStrings.TokenPlaceholder || tokenConfigValue == AuthStrings.ClientID_UUID_Placeholder)
            {
                LogError($"Update {AuthStrings.tokenFileName} to use your actual auth token for your plex server.\n" +
                    "\t\tOr delete the config and run the app again to go through the token generation steps.");
                return null;
            }
            else
            {
                return tokenConfigValue;
            }
        }
    }

    static bool NonInteractiveAuthFlow_GenerateAuth()
    {
        // We'll generate the auth url and display it to the user and tell them to visit it and login
        // Then just exit the app and not wait for them to press enter, because we can't. Instead tell them to log in and run the app again

        string appNameToUse = MyStrings.AppNameShort;

        // Generate the token request
        TokenGenResult genResult = GenerateAppTokenRequest(appName: appNameToUse, url: AuthStrings.PlexPinUrl);

        string authUrl;
        // The code and ID should never be null for a success, but check anyway
        if (genResult.Success && genResult.Code != null && genResult.PinID != null && genResult.ClientIdentifier != null)
        {
            // Generate the auth URL and tell the user to visit it
            authUrl = GenerateAuthURL(clientIdentifier: genResult.ClientIdentifier, code: genResult.Code, appName: appNameToUse);
            WriteLineSafe("\n----------------------------------------------------------------");
            WriteLineSafe($"\nPlease visit the following URL to authorize the app by logging in. Then run the app again. \n\n\t{authUrl}\n\n");
            WriteLineSafe("-------------------------------------------------");
            CreatePendingTokenFile(pinID: genResult.PinID, clientID_UUID: genResult.ClientIdentifier, tempAuthCode: genResult.Code);
            return true;
        }
        else
        {
            WriteLineSafe($"Token generation failed. See any error messages above.");
            return false;
        }
    }

    static bool NonInteractiveAuthFlow_CheckAuth(string pinID, string clientID, string tempAuthCode, string appName = MyStrings.AppNameShort)
    {
        // Load the tokens from the file
        (string authToken, string clientID)? authResult = GetAuthorizedTokenAfterUserConfirmation(pinID: pinID, appName: appName, clientIdentifier: clientID);
        if (authResult != null)
        {
            // Save the token to the file
            CreateTokenFile(authResult.Value.authToken, authResult.Value.clientID);
            return true;
        }
        else
        {
            string authUrl = GenerateAuthURL(clientIdentifier: clientID, code: tempAuthCode, appName: appName);

            WriteLineSafe("----------------------------------------------------------------");
            WriteRedSuper("\nThe app does not appear authorized.", noNewline: true);
            WriteRed("  Visit this URL and sign in if you haven't already: ");

            WriteGreen($"\n\t{authUrl}");
            WriteLineSafe("\nThen run the app again.");
            return false;
        }
    }

    static bool FullAuthFlow()
    {
        string appNameIncludingConfig;
        if (!string.IsNullOrWhiteSpace(Program.config.CurrentDeviceLabel.Value.Trim()))
        {
            appNameIncludingConfig = MyStrings.AppNameShort + $" ({Program.config.CurrentDeviceLabel.Value.Trim()})";
        }
        else
        {
            appNameIncludingConfig = MyStrings.AppNameShort;
        }

        bool successResult = false;
        // Generate the token request
        TokenGenResult genResult = GenerateAppTokenRequest(appName: appNameIncludingConfig, url: AuthStrings.PlexPinUrl);

        string authUrl;
        // The code and ID should never be null for a success, but check anyway
        if (genResult.Success && genResult.Code != null && genResult.PinID != null && genResult.ClientIdentifier != null)
        {
            // Generate the auth URL and tell the user to visit it
            authUrl = GenerateAuthURL(clientIdentifier: genResult.ClientIdentifier, code: genResult.Code, appName: appNameIncludingConfig);

            WriteLineSafe("\n----------------------------------------------------------------");
            WriteGreen($"\nPlease visit the following URL to authorize the app. (Select then right click to copy from a console) \n\n\t{authUrl}");
            WriteLineSafe("\n\nTip: After authorizing, you should see it show up in the 'devices' section at:" +
                "\n     http://<Your Server IP>:<Port>/web/index.html#!/settings/devices/all");

            WriteLineSafe("\n----------------------------------------------------------------");
            WriteSafe("Press Enter to continue ");
            Console.ForegroundColor = ConsoleColor.Red;
            WriteSafe("after you have authorized the app.");
            Console.ResetColor();
            //WriteLineSafe(); // Don't add extra newline or else can't erase it

            // Wait for user to press Enter
            ReadlineSafe();

            // Erase the past line
            ClearLastLine();
        }
        else
        {
            WriteLineSafe($"Token generation failed. Please check the {AuthStrings.tokenFileName} file.");
            return false;
        }

        // Get the token after user confirmation. At this point the user should have authorized the app and has hit a key
        // If the token is not null, the user may not have authorized the app, so
        bool authSuccess = false;
        while (authSuccess == false)
        {
            (string authToken, string clientID)? authResult = GetAuthorizedTokenAfterUserConfirmation(pinID: genResult.PinID, appName: appNameIncludingConfig, clientIdentifier: genResult.ClientIdentifier);
            if (authResult != null)
            {
                // Save the token to the file
                CreateTokenFile(authResult.Value.authToken, authResult.Value.clientID);
                authSuccess = true;
                successResult = true;
            }
            else
            {
                WriteLineSafe("----------------------------------------------------------------");
                WriteRedSuper("\nThe app does not appear authorized.", noNewline: true);
                WriteRed("  Visit this URL and sign in if you haven't already: ");

                WriteGreen($"\n\t{authUrl}");
                WriteLineSafe("\nThen press Enter to check again.");
                ReadlineSafe();
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
                WriteLineSafe($"Request failed with status code: {response.StatusCode}");
                return new TokenGenResult(false);
            }

        }
        catch (HttpRequestException e)
        {
            WriteLineSafe($"Request error: {e.Message}");
            return new TokenGenResult(false);
        }
        catch (TaskCanceledException e)
        {
            WriteLineSafe($"Request timed out: {e.Message}");
            return new TokenGenResult(false);
        }
        catch (Exception e)
        {
            WriteLineSafe($"Unexpected error: {e.Message}");
            return new TokenGenResult(false);
        }
    }

    public static string GenerateAuthURL(string clientIdentifier, string code, string appName = MyStrings.AppNameShort)
    {
        // Define the base URL
        string baseUrl = AuthStrings.UserAuthConfirmationURL;

        // Construct the user auth URL with the provided variables
        string userAuthUrl = $"{baseUrl}clientID={clientIdentifier}&code={code}&context%5Bdevice%5D%5Bproduct%5D={Uri.EscapeDataString(appName)}";

        return userAuthUrl;
    }

    public static (string authToken, string clientID)? GetAuthorizedTokenAfterUserConfirmation(string pinID, string appName, string clientIdentifier, string baseURL = AuthStrings.PlexPinUrl)
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
                return (authToken, clientIdentifier);
            }
            else
            {
                return null;
            }
        }
        else
        {
            WriteLineSafe($"Request failed with status code: {response.StatusCode}");
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
                if (authTokenAttr != null && !string.IsNullOrEmpty(authTokenAttr.Value))
                {
                    authToken = authTokenAttr.Value;
                    WriteLineSafe("----------------------------------------------------------------");
                    WriteGreenSuper("\t    Success!    ");
                    WriteLineSafe($"\tSuccessfully created auth token: {authToken}");
                    WriteLineSafe($"\tIt will automatically be stored in the file {AuthStrings.tokenFileName}");
                    WriteLineSafe("----------------------------------------------------------------");
                }
                else
                {
                    WriteLineSafe("\tReceived invalid token response.");
                }
            }
            else
            {
                WriteLineSafe("\tReceived invalid token response.");
            }
            return authToken;
        }
        catch (XmlException ex)
        {
            WriteLineSafe($"\tFailed to parse XML response: {ex.Message}");
            return "";
        }
    }

    // Parse the json response to get the id and code
    public static TokenGenResult ParseTokenGenJsonResponse(string jsonResponse)
    {
        try
        {
            // Get the default instance of the source-generated context
            AuthResponseJsonContext context = AuthResponseJsonContext.Default; // Hopefully Default works now!

            // --- Deserialize directly to TokenGenResult using its JsonTypeInfo ---
            // The source generator should create the 'TokenGenResult' property on the context.
            TokenGenJson? result = JsonSerializer.Deserialize(jsonResponse, context.TokenGenJson);
            // --- End of key change ---

            TokenGenResult tokenGenResult;
            // Determine success based on whether we got a result and it has the expected data
            if (result != null && result.Id != null && !string.IsNullOrEmpty(result.Code))
            { 
                tokenGenResult = new TokenGenResult(true, result.Id, result.Code);
            }
            else
            {
                // If the result is null or doesn't contain the expected data, return a failure
                WriteLineSafe("Token generation failed. Please check the response.");
                return new TokenGenResult(false);
            }

                return tokenGenResult;
        }
        catch (JsonException jsonEx)
        {
            // Handle potential JSON parsing errors
            WriteLineSafe($"Error parsing token generation JSON: {jsonEx.Message}");
            return new TokenGenResult(false);
        }
        catch (Exception ex) // Catch other potential errors (e.g., if context.Default failed)
        {
            WriteLineSafe($"Unexpected error in ParseTokenGenJsonResponse: {ex.Message}");
            return new TokenGenResult(false);
        }
    }

    public class TokenGenJson
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; } = null;
        [JsonPropertyName("code")]
        public string? Code { get; set; } = null;
    }

    public class TokenGenResult(bool success, long? id = null, string? code = null, string? clientIdentifier = null)
    {
        public bool Success { get; set; } = success;
        public string? PinID { get; set; } = id.ToString();
        public string? Code { get; set; } = code;
        public string? ClientIdentifier { get; set; } = clientIdentifier;
    }

} // ---------------- End class AuthTokenHandler ----------------

