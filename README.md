# Rewind Subtitle Displayer For Plex

## What it Does

If you rewind a show or movie by a short amount (like pressing the back button on a remote), this tool will automatically tell Plex to **turn on subtitles** temporarily on that device. 

Once playback reaches the point where you started the rewind, it will **automatically turn the subtitles back off** (unless you had them on manually already).

## Why?

99% of the time you use the rewind button, it's probably because you didn't catch the dialog

## Features

* **Monitors All Local Players**: Automatically detects playback sessions from any Plex client on your network connected to your server.
* **Respects Manual Settings**: If you manually turned subtitles on *before* rewinding, they will stay on.
* **Temporary Subtitle Display**: Enables the first available subtitle track *only* for the duration you rewound. Then seamlessly disables them again.
* **Smart-Cancellation**: Automatic disabling of temporary subtitles again on certain conditions:
  * If you rewind longer than a specified threshold (60 seconds default)
  * If you fast forward at any point

# Other Benefits

* **API Integration**: This uses the Plex API directly, the enabling/disabling of subtitles is seamless.
* **Cross-Platform**: It's a simple console app that can be run on Windows, MacOS, and Linux.
* **Run Purely in Background**: On Windows use the `-background` launch argument to run it without any console Window. It will sit idle until Plex notifies it that Media is playing.

## Screenshot
![Screenshot](https://github.com/user-attachments/assets/e01928bb-8131-47dc-b1c8-9479080a5d98)


## Requirements

* You **MUST** enable the "Remote Control" / aka "Advertise As Player" option in each player app for this to work.
    * For example in the iOS app this can be found under Settings > Remote Control > Advertise As Player
    * Note: The Plex Media Server desktop app strangely does not allow this feature. However the "[HTPC](https://support.plex.tv/articles/htpc-getting-started/)" version of the desktop Plex player does.
* This may only work if your Plex server is on your local network

## Setup

1.  **Connect to Plex**:
    * Run the application. It will guide you through a one-time authorization process in your web browser to link with your Plex account. This is required to connect to your server via the API.
2.  **Configure Server Address**:
    * Edit the `settings.ini` file.
    * Set `Server_URL_And_Port` to your Plex Media Server's local IP address and port (e.g., `http://192.168.1.100:32400`).

## Usage

1.  Run the executable (e.g., `RewindSubtitleDisplayerForPlex.exe` on Windows).
2.  Leave it running in the background. It will automatically monitor sessions.
3.  Optional arguments:
    * `-background`: Run hidden (Windows only).
    * `-debug`: Show detailed logs.
