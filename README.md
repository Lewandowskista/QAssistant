# QAssistant

> A QA-focused desktop workbook for Windows — built with WinUI 3 and .NET 10

![Platform](https://img.shields.io/badge/platform-Windows%2010%20v1809%2B-A78BFA?style=flat-square)
![Framework](https://img.shields.io/badge/framework-.NET%2010-60A5FA?style=flat-square)
![UI](https://img.shields.io/badge/UI-WinUI%203-34D399?style=flat-square)
![License](https://img.shields.io/badge/license-MPL--2.0-F472B6?style=flat-square)

---

## What is QAssistant?

QAssistant is a native Windows desktop workbook built for QA engineers. It centralises task tracking, test management, AI-powered issue analysis, and automation integration in a single self-contained application that runs entirely on your machine — no cloud account or subscription required.

---

## Features

### 🗂️ Projects
- Create and manage multiple QA projects side by side
- Color-coded project sidebar for quick visual identification
- Rename or delete projects with a double-click
- Each project has its own tasks, notes, links, files, test plans, and credentials

### 🔗 Links
- Embed any URL directly in the app via WebView2
- Persistent browser sessions — stay logged in to Notion, Figma, Linear, and GitHub
- Organize and reorder links per project with pin support
- Typed shortcuts for Notion, Figma, Linear, GitHub, and generic URLs

### 📝 Notes
- Per-project notes with auto-save and timestamps
- File attachments per note
- Quick search across all notes and projects

### ✅ Tasks — Kanban Board
- 7-column Kanban board: **Backlog → To Do → In Progress → In Review → Done → Canceled → Duplicate**
- Manual task creation with title, description, priority, due date, labels, assignee, and reporter
- Drag tasks between columns
- Task detail sidebar with full history, attachments, and AI analysis
- **Linear sync** — import issues from any team (resolve by name, key, or UUID), post comments, open in Linear
- **Jira sync** — import issues from any Atlassian Cloud project via REST API v3, with full field mapping
- Due-date reminders with in-app notification banners
- Daily 9 AM summary toast notification

### 🤖 AI-Powered Issue Analysis (Gemini)
- Deep per-issue analysis powered by Google Gemini (`gemini-2.5-flash`)
- Automatic model fallback if rate-limited
- Multimodal support — attached screenshots are included in the analysis
- Structured output in four sections:
  - **Root Cause Analysis**
  - **Impact Assessment**
  - **Suggested Fix**
  - **Prevention Recommendations**
- Versioned analysis history — each run is hashed and timestamped, and persisted across sessions for Linear and Jira issues
- Uses a compact **TOON** (Token-Oriented Object Notation) prompt format to reduce token usage while preserving analysis quality
- Copy results to clipboard for easy sharing

### 🧪 Test Management
- Organise test cases into named **Test Plans**
- Manual test case creation with pre-conditions, steps, test data, expected/actual result, and priority
- **AI-assisted test case generation** from issue title and description
- Per-plan **criticality assessment** powered by Gemini
- Test execution tracking with per-run results: Passed, Failed, Blocked, Skipped, Not Run
- Archive completed test plans to keep the workspace clean
- Export test cases and execution results to **CSV**

### 🔌 Automation API
- Lightweight local HTTP server for CI/automation framework integration
- Exposes test cases and accepts execution results via REST
- Bearer token authentication with a cryptographically random key stored in Credential Manager
- Compatible with Playwright, Cypress, or any HTTP client
- Configurable port; key regeneration available from Settings

### 📁 Files
- Per-project file attachment storage
- Paste screenshots directly from clipboard
- Drag-and-drop file import
- Executable and script extensions are blocked for security
- Image thumbnail previews

### 🔍 Search
- Quick search across all notes and tasks in all projects
- Results navigate directly to the relevant project and tab

### 🔔 Reminders & Notifications
- Background timer checks tasks every minute for approaching due dates
- Overdue, upcoming (today/tomorrow), and later notification tiers
- Daily 9 AM summary banner
- Native Windows toast notifications via `AppNotificationManager`
- Click a notification to navigate directly to the relevant task

### 🖥️ System Tray
- Minimize to tray support (toggleable in Settings)
- Right-click context menu: Restore / Exit
- Double-click tray icon to restore window

### ⚙️ Settings & Diagnostics
- Per-project credential storage — each project can connect to a different workspace
- All credentials stored securely in the **Windows Credential Manager** (never written to disk)
- Automation API key management with one-click regeneration
- Storage path diagnostics with log file access

---

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# / .NET 10 |
| UI Framework | WinUI 3 (Windows App SDK 1.8) |
| Browser Engine | WebView2 |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| JSON | System.Text.Json (source-generated, AOT-compatible) |
| Credential Storage | Windows Credential Manager (Win32 P/Invoke) |
| Tray Integration | H.NotifyIcon.WinUI 2.4 |
| Notifications | Windows App SDK `AppNotificationManager` |
| Linear Integration | Linear GraphQL API |
| Jira Integration | Jira Cloud REST API v3 |
| AI Analysis | Google Gemini API (`gemini-2.5-flash`) |
| Automation API | `System.Net.HttpListener` (localhost) |

---

## Requirements

- Windows 10 version 1809 (build 17763) or later — Windows 11 recommended
- x64 architecture
- Internet connection for Linear/Jira sync and Gemini AI features
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (pre-installed on Windows 11)

---

## Getting Started

### From GitHub Releases (Recommended)

1. Download the latest `QAssistant.zip` from [Releases](https://github.com/Lewandowskista/QAssistant/releases)
2. Extract the zip to a folder of your choice
3. Run `WinAppRuntime_Setup.exe` to install the Windows App Runtime if prompted
4. Run `QAssistant.exe`

### From Source

```bash
git clone https://github.com/Lewandowskista/QAssistant.git
cd QAssistant
```

Open `QAssistant.sln` in **Visual Studio 2022 or later** with the **Windows App SDK** workload installed, set the startup project to `QAssistant` (x64), and press **F5**.

---

## Configuration

All credentials are entered on the **Settings** page inside the app and stored securely in the Windows Credential Manager. No config files or environment variables are needed. Credentials are scoped per project — each project can connect to a different workspace.

### Linear

1. Go to **Settings → Linear**
2. Enter your **API Key** — generate one at `linear.app → Settings → API → Personal API Keys`
3. Enter a **Team ID, key, or name** to scope the issue sync
4. Click **Save Linear Keys**, then **Test Connection**

### Jira

1. Go to **Settings → Jira**
2. Enter your **Atlassian subdomain** (e.g. `mycompany`), **email**, **API token**, and **project key**
3. Generate a token at `id.atlassian.com → Security → API tokens`
4. Click **Save Jira Keys**, then **Test Connection**

### Google Gemini (AI Analysis)

1. Go to **Settings → Gemini**
2. Enter your **API Key** — get a free key at [aistudio.google.com](https://aistudio.google.com/app/apikey)
3. Use the **Analyze** button on any task to run AI-powered issue analysis

---

## Automation API

QAssistant exposes a local REST API so automation frameworks (Playwright, Cypress, etc.) can query test cases and submit execution results without manual interaction.

### Enable

Go to **Settings → Automation API**, set a port, and start the server. The API key is displayed there and can be regenerated at any time.

### Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Service status |
| `GET` | `/testcases` | All test cases for the active project |
| `POST` | `/executions` | Submit one or more execution results |

### Authentication

```http
Authorization: Bearer <api-key>
```

The key is auto-generated on first use using `RandomNumberGenerator` and stored in Credential Manager.

---

## Data Storage

All data is stored locally — nothing is sent to any cloud service except the external APIs you explicitly configure.

| Data | Location |
|---|---|
| Projects, notes, tasks, links, tests | `%AppData%\QAssistant\projects.json` |
| Storage logs | `%AppData%\QAssistant\storage.log` |
| File attachments | `%AppData%\QAssistant\Files\` |
| WebView2 sessions | `%AppData%\QAssistant\WebView2Data\` |
| API keys & credentials | Windows Credential Manager (`QAssistant_*`) |

Sensitive fields in `projects.json` are encrypted at rest using **Windows DPAPI**, binding them to the current Windows user account.

> **Tip:** Go to **Settings → Diagnostics** to view the exact paths and open the log folder directly.

---

## Security

- **Credentials** — stored exclusively in Windows Credential Manager; never written to disk in plaintext
- **URI validation** — all external URLs are validated for HTTP/HTTPS scheme and checked against private/loopback IP ranges (SSRF mitigation)
- **File uploads** — executable and script extensions (`.exe`, `.ps1`, `.bat`, `.cmd`, etc.) are blocked
- **Automation API** — binds to localhost only; all requests require a bearer token
- **AI prompts** — user-supplied values are sanitised before embedding into Gemini prompts to prevent prompt injection

---

## Project Structure

```
QAssistant/
├── Models/
│   ├── Project.cs
│   ├── ProjectTask.cs
│   ├── Note.cs
│   ├── EmbedLink.cs
│   ├── FileAttachment.cs
│   ├── TestCase.cs
│   ├── TestPlan.cs
│   ├── TestExecution.cs
│   ├── AnalysisEntry.cs
│   ├── WorklogEntry.cs
│   ├── LinearComment.cs
│   └── SearchResult.cs
├── ViewModels/
│   └── MainViewModel.cs
├── Views/
│   ├── LinksPage.xaml
│   ├── NotesPage.xaml
│   ├── TasksPage.xaml
│   ├── TestsPage.xaml
│   ├── FilesPage.xaml
│   └── SettingsPage.xaml
├── Services/
│   ├── StorageService.cs          JSON persistence with DPAPI encryption
│   ├── CredentialService.cs       Windows Credential Manager wrapper
│   ├── LinearService.cs           Linear GraphQL API client
│   ├── JiraService.cs             Jira Cloud REST API v3 client
│   ├── GeminiService.cs           Google Gemini AI client (TOON prompt engine)
│   ├── AutomationApiService.cs    Local HTTP API server
│   ├── ReportService.cs           CSV export
│   ├── ReminderService.cs         Due-date background checker
│   ├── NotificationService.cs     Windows toast notifications
│   └── FileStorageService.cs      File attachment management
├── Helpers/
│   ├── UriSecurity.cs
│   └── DialogHelper.cs
├── Converters/
│   └── TaskPriorityToBrushConverter.cs
├── MainWindow.xaml
└── App.xaml
```

---

## CI/CD

QAssistant uses GitHub Actions for automated builds and releases:

- **Trigger:** Push a tag matching `v*` (e.g. `v1.2.0`) or trigger manually via workflow dispatch
- **Output:** A portable `.zip` containing `QAssistant.exe` (single-file, self-contained x64) and `WinAppRuntime_Setup.exe`
- **Download:** Releases are automatically published to [GitHub Releases](https://github.com/Lewandowskista/QAssistant/releases)

---

## Roadmap

- [x] Linear issue sync on Tasks board
- [x] Jira issue sync on Tasks board
- [x] AI-powered issue analysis with Gemini
- [x] Test case management and execution tracking
- [x] AI-assisted test case generation
- [x] Automation API for Playwright/Cypress integration
- [x] CSV export for test cases and executions
- [x] Note-level file attachments
- [x] Windows toast notifications for reminders
- [x] System tray integration
- [x] Diagnostics panel in Settings
- [ ] Drag to reorder Kanban task cards
- [ ] Export notes to PDF or Markdown
- [ ] Keyboard shortcuts for tab navigation
- [ ] Dark/light theme toggle

---

## Troubleshooting

### Projects not appearing in sidebar
1. Go to **Settings → Diagnostics**
2. Check the **Data Storage Path** shown
3. Click **Open Log File** to view recent operations

### Data not persisting
1. Check that `%AppData%\QAssistant` exists and is writable
2. Review `storage.log` for error messages
3. Ensure your Windows user account has write access to AppData

### API connection issues
- Verify your API keys are correct and have not expired
- For Jira: use only the subdomain portion of your URL (e.g. `mycompany`, not `mycompany.atlassian.net`)
- For Linear: the team identifier can be the team UUID, key (e.g. `ENG`), or display name

---

## Contributing

Contributions, issues, and feature requests are welcome. Please open an issue first to discuss significant changes.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes (`git commit -m 'Add my feature'`)
4. Push to the branch (`git push origin feature/my-feature`)
5. Open a Pull Request

---

## License

This project is licensed under the **Mozilla Public License 2.0**.  
See the [LICENSE](LICENSE) file for details.

---

## Acknowledgements

- [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) by Microsoft
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) by Microsoft
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) by HavenDV
- [Google Gemini API](https://ai.google.dev/)

---

<div align="center">
  <sub>Built for QA engineers who like their tools fast, local, and secure.</sub>
</div>
