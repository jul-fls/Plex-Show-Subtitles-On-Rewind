# Docker Instructions for RewindSubtitleDisplayerForPlex

**Note:** This is a basic Docker setup requiring manual steps. A fully automated build process is not yet implemented.

**Prerequisites:**
* Docker Desktop (or Docker Engine with Docker Compose) installed and running.

**Setup and First Run:**

These instructions assume you are running commands from within a folder called `/Docker` that contains in this repo's Docker folder. The folder name doesn't matter as long as you run any commands from within it as the working directory.

1.  **Download Binary:**
    * Go to the project's [GitHub Releases page](https://github.com/ThioJoe/Plex-Show-Subtitles-On-Rewind/releases) .
    * Download the latest **self-contained Linux x64 binary**. The filename will look like `RewindSubtitleDisplayerForPlex_X.Y.Z_linux-x64`.
    * Place the downloaded binary file **directly inside this `Docker` folder**.

2.  **Create Config Folder:**
    * Create a subdirectory named `config` **inside this `Docker` folder**. This is where the application's configuration (`settings.ini`, `token.config`) will be stored and where you can edit them.
    * ```bash
        mkdir config
        ```

3.  **Build & Initial Run:**
    * Run the following command in your terminal (make sure you are inside the `Docker` folder). This will build the Docker image (if needed) and start the container in the background.
    * ```bash
        docker-compose up -d
        ```

4.  **Authorize Application (One-Time Step):**
    * The application needs to be authorized with your Plex account the first time it runs. Check the container logs:
    * ```bash
        docker-compose logs
        ```
    * Look for a log message containing an authorization URL .
    * Copy this URL and paste it into your web browser.
    * Log in to your Plex account to approve the app. The restart the docker container.
    * Upon restarting the container, the application running in the container should automatically detect the approval and create/update the `token.config` file inside the `./config` folder.

5.  **Stop the Container:**
    * Once authorization seems complete (check logs again if needed), stop the container:
    * ```bash
        docker-compose down
        ```

6.  **Configure Plex Server Address:**
    * Open the `settings.ini` file located inside the `./config` folder (`Docker/config/settings.ini`) with a text editor.
    * Find the setting for the Plex server URL (e.g., `PlexServerUrl`).
    * Change the value from the default (likely `http://127.0.0.1:32400`) to use the special Docker host address:
    * ```ini
        PlexServerUrl = http://host.docker.internal:32400
        ```
    * Save the `settings.ini` file.

7.  **Run Normally:**
    * Start the container again:
    * ```bash
        docker-compose up -d
        ```
    * The application should now connect to your Plex server and run normally.

**Managing the Container:**

* **View Logs:** `docker-compose logs` or `docker-compose logs -f` (to follow).
* **Stop:** `docker-compose down`
* **Start:** `docker-compose up -d`
* **Update Application:** Download the new binary, replace the old one in the `/Docker` folder, then run `docker-compose down && docker-compose up -d --build`.