# Smart-X WPF / API integration

## Architecture

The WPF client is kept separate from the SmartXApi project. HTTP and endpoint-specific responsibilities are in `SmartxWPF/Services`:

- `ApiClient` — common base URL, bearer-token header, JSON, error handling, and file upload.
- `AuthenticationService` — signs in with Firebase email/password and applies the returned Firebase ID token to API requests.
- `DashboardService` — dashboard, alerts, and predictive insights.
- `DeviceService` — device list and search.
- `TelemetryService` — sensor timeline endpoint.
- `CommandStreamService` — issue commands, undo, and history.
- `FileImportService` — uploads `.txt` or `.csv` device lists.

DTOs shared by the UI/service layer are in `Models/ApiDtos.cs`. The API source project was retained from the supplied archive; this WPF integration did not intentionally change API files.

## Run locally

1. Open `SmartX2.slnx` in Visual Studio 2026 with the .NET 10 SDK and Windows Desktop development workload installed.
2. The development settings already set `Firebase:ProjectId` to `smar-auth`. No database is needed: the API keeps devices, sensors, telemetry and attachments in memory and seeds them from `SmartXApi/Data/DataSeeder.cs` on startup. Data resets whenever the API restarts.
3. Start `SmartXApi` first. Its launch settings include `http://localhost:5234` and `https://localhost:7295`.
4. In Firebase Console, enable **Authentication → Sign-in method → Email/Password** and ensure a user account exists under **Authentication → Users**. Start `SmartxWPF`: the Smart-X splash page appears first. Select **Enter Smart-X**, then enter the API URL and Firebase email/password on the login page and select **Sign in and open workspace**.
5. Use **Import device list** to upload a `.txt` or `.csv` file. The API parser expects delimited fields in this order: `DeviceIdentifier,MacAddress,Location,Room,NodeId,SensorSpec`. The first two fields are required. Lines beginning with `#` or `//` are ignored; a header row is supported. See `SmartXApi/Services/DeviceImportService.cs` for accepted sensor specification formats.

## Startup and view navigation

- The WPF opens on a dedicated Smart-X splash page. **Enter Smart-X** navigates to the Firebase login page; a successful sign-in opens the main workspace.
- The workspace follows the dark left-navigation / light content-card visual approach of the supplied SmartXsem2 WPF reference, while retaining the current Part 2 API-backed pages: Overview, Sensor telemetry, and Command stream.
- The workspace provides device-list import, dashboard metrics, device state, sensor snapshots/history, alerts, automated insights, command history, and command actions. Sign out returns to the login page.
- Network topology remains visible but disabled until its UI is implemented.

## Current scope and limitations

- The WPF reads and displays dashboard metrics, device state, latest sensor snapshots, selected sensor history, alerts, insights, command history, and command outcomes.
- Device search, command issue, undo, and device-list upload call existing API endpoints.
- Network topology remains intentionally disabled because no topology UI is implemented here.
- The API requires Firebase bearer authentication for operator endpoints. Firebase ID tokens expire; automatic refresh is not included in this version, so sign in again after token expiry.
- The supplied API source requires Firebase configuration before it can run.
- Build/runtime verification could not be performed in this generation environment because the .NET SDK is not installed. The XAML was XML-parsed and its declared event handlers were checked against the code-behind, but please build the solution in Visual Studio and resolve any machine-specific configuration issues.


## Firebase email/password sign-in

The WPF client signs in through Firebase Authentication's Identity Toolkit REST endpoint using the Firebase Web API key from the supplied web-app configuration. In Firebase Console, open **Authentication → Sign-in method** and enable **Email/Password**; then create a user under **Authentication → Users**. Use that user's email and password in the WPF sign-in panel. The API development settings specify Firebase project ID `smar-auth`, which must match the Firebase project that issues the ID token.

The Firebase Web API key is a client identifier, not a service-account private key. Restrict it in Google Cloud where practical. Do not put service-account credentials or private keys in the WPF client. Firebase ID tokens expire; sign in again after token expiry (automatic refresh is not included in this version).
