# Privacy Policy for CelestiCloud

*Last updated: May 29, 2026*

CelestiCloud ("we", "our", or "us") is an open-source, local-first file synchronization desktop application. This Privacy Policy describes how we handle information when you use our software.

## 1. No Data Collection (Local-First)
CelestiCloud does not run on a centralized server. We do not collect, monitor, store, or transmit your personal data, files, credentials, or analytical information to any third-party databases. All configuration files, authentication tokens, and logs are stored locally on your physical device in standard application data directories.

## 2. Google Drive Integration (drive.file scope)
CelestiCloud integrates with the Google Drive API to synchronize your local directories. 
* **Limited Access:** CelestiCloud requests the `https://www.googleapis.com/auth/drive.file` authorization scope. This means our application can **only** read, edit, and delete files and folders that were specifically created by CelestiCloud. We cannot access, view, or modify any other existing personal files or folders stored in your Google Drive.
* **OAuth Tokens:** The OAuth access and refresh tokens returned by Google are stored securely and exclusively on your local machine. They are never sent to us or any unauthorized third parties.

## 3. Third-Party Services
Since your files are uploaded directly from your physical machine to Google servers, your data is subject to Google's Privacy Policy. We encourage you to review their policies directly.

## 4. Contact and Support
Because CelestiCloud is an open-source project, you can inspect our entire codebase on GitHub. If you have any questions, feel free to open an issue on our official repository.