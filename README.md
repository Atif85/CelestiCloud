# CelestiCloud

CelestiCloud is a lightweight, crossplatform file synchronization and backup engine. It is designed to be a alternative to native cloud desktop clients.

Currently, the project supports Google Drive, with additional providers (e.g., Dropbox, OneDrive) planned in the future.

## Project Structure

* **`CelestiCloud.Core`**: The backend engine. Handles OAuth, cloud provider API communications, concurrent throttling, local file system watching, and `.gitignore`-style path filtering.
* **`CelestiCloud.CLI`**: The command-line interface built with `Spectre.Console`. Supports interactive job creation, account management, and real-time dashboard rendering.
* **`CelestiCloud.GUI`**: A desktop graphical user interface built with Avalonia UI (Currently in early development).

## Key Features

* **Sync & Backup Modes**: 
  * **Sync**: Mirrors your local folders to the cloud (local deletions remove cloud files).
  * **Backup**: Cumulative uploads (local deletions do *not* remove cloud files).
* **Rename/Move Detection**: Identifies local file renames and updates cloud metadata instantly without re-uploading file data.
* **Filtering**: Uses standard `.gitignore` syntax (including negations `!`) to include or exclude specific files and directories.
* **Concurrency**: Parallel file uploads and downloads to maximize bandwidth usage.
* **Cross-Process Safety**: Uses PID lock files to safely allow background jobs, the CLI, and the GUI to share the same configurations without conflicts.

## Current Status: Alpha

CelestiCloud is currently in **Alpha**. 
* The **Core** engine is functional and stable.
* The **CLI** is mostly feature-complete.
* The **GUI** is actively being built.
* Only the **Google Drive** provider is implemented so far.

## Building from Source

To build and run the CLI locally:

1. Clone the repository.
2. Obtain a `credentials.json` file from the Google Cloud Console (configured for Desktop App OAuth) and place it in the `CelestiCloud.Core`.
3. Build the CLI:
   ```bash
   dotnet build CelestiCloud.CLI -c Release
   ```
4. Run the CLI:
   ```bash
   cd CelestiCloud.CLI/bin/Release/net10.0/
   ./celesticloud account add googledrive
   ./celesticloud job create
   ```