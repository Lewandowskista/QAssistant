[README.md](https://github.com/user-attachments/files/25545093/README.md)
# QAssistant

> A floating desktop companion for QA engineers — built with WinUI 3 and .NET 10

![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-A78BFA?style=flat-square)
![Framework](https://img.shields.io/badge/framework-.NET%2010-60A5FA?style=flat-square)
![UI](https://img.shields.io/badge/UI-WinUI%203-34D399?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-F472B6?style=flat-square)

---

## What is QAssistant?

QAssistant is an always-on-top floating desktop widget designed for QA engineers who juggle multiple projects, tools, and tasks at once. It keeps everything you need — your Linear issues, notes, links, files, and Kanban board — in one lightweight, pinnable window that stays out of your way until you need it.

---

## Features

### 🗂️ Projects
- Create and manage multiple QA projects side by side
- Color-coded project sidebar for quick visual identification
- Rename or delete projects with a double-click
- Drag to reorder projects in the sidebar

### 🔗 Links
- Embed any URL directly in the app via WebView2
- Persistent browser sessions — stay logged in to Notion, Figma, Linear and GitHub
- Organize and reorder links per project
- Supports all link types: Notion, Figma, Linear, GitHub, and custom URLs

### 📝 Notes
- Rich text notes per project with auto-save
- Reorder notes via drag and drop
- Quick search across all notes and projects
- Timestamped with last saved indicator

### ✅ Tasks — Kanban Board
- 5-column Kanban board: Todo, In Progress, In Review, Done, Blocked
- Manual task mode — create, edit, and delete tasks with priority and due dates
- Linear sync mode — fetch and view real Linear issues directly in the board
- Post comments to Linear issues from within the app
- Open issues directly in Linear with one click
- Due date reminders with in-app notification banners
- Daily summary notification at 9am

### 🤖 AI-Powered Issue Analysis
- Analyze any task or Linear issue using Google Gemini AI
- Get AI-generated insights including:
  - Root Cause Analysis
  - Impact Assessment
  - Suggested Fix
  - Prevention Recommendations
- Copy analysis results to clipboard for easy sharing
- Requires a Google AI Studio API key (free tier available)

### 📁 Files
- Attach files to any project
- Paste screenshots directly from clipboard (Win+Shift+S)
- Drag and drop files into the app
- Browse and attach any file type
- Image thumbnail previews
- Reorder files via drag and drop

### 🔍 Search
- Quick search across all notes and tasks in all projects
- Results navigate directly to the relevant project and tab

### 🖥️ System Tray
- Minimize to system tray instead of closing (toggleable in Settings)
- Right-click tray icon for Show / Hide / Exit menu
- Double-click tray icon to restore

### 🎨 UI & Window
- Always-on-top floating window
- Custom titlebar with Pin toggle, minimize, maximize and close
- Dark theme throughout with purple accent colors
- Fully custom window controls — no default Windows titlebar buttons
- Smooth drag and drop reordering across all lists

### ⚙️ Settings & Diagnostics
- Configure Linear, Jira, and Google AI Studio API integrations
- View data storage paths and log files for troubleshooting
- Refresh projects sidebar manually if needed

---

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# 14 / .NET 10 |
| UI Framework | WinUI 3 (Windows App SDK 1.8) |
| Browser Engine | WebView2 |
| MVVM | CommunityToolkit.Mvvm |
| JSON | System.Text.Json (AOT-compatible) |
| Credential Storage | Windows Credential Manager |
| Tray Integration | H.NotifyIcon.WinUI |
| Linear Integration | Linear GraphQL API |
| Jira Integration | Jira REST API |
| AI Analysis | Google Gemini API |

---

## Getting Started

### Prerequisites

- Windows 10 version 1903 or later (Windows 11 recommended)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Visual Studio 2022/2026](https://visualstudio.microsoft.com/) with the **Windows App SDK** workload installed
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (usually pre-installed on Windows 11)

### Installation

#### From GitHub Releases (Recommended)

1. Download the latest `QAssistant.zip` from [Releases](https://github.com/Lewandowskista/QAssistant/releases)
2. Extract the zip to a folder of your choice
3. Run `WinAppRuntime_Setup.exe` to install the Windows App Runtime (if not already installed)
4. Run `QAssistant.exe`

#### From Source

```bash
# Clone the repository
git clone https://github.com/Lewandowskista/QAssistant.git
cd QAssistant

# Restore and build
dotnet restore
dotnet build -c Release

# Or open in Visual Studio
start QAssistant.sln
git clone https://github.com/yourusername/QAssistant.git

# Open in Visual Studio
start QAssistant.sln
```

1. Open `QAssistant.sln` in Visual Studio 2022/2026
2. Restore NuGet packages (`dotnet restore`)
3. Set the startup project to `QAssistant`
4. Press `F5` to build and run

---

## Configuration

### Linear Integration

1. Go to **Settings** tab inside QAssistant
2. Enter your **Linear API Key** — get it from `linear.app → Settings → API → Personal API Keys`
3. Enter your **Team ID** — found in `linear.app → Settings → Team` (copy from the URL)
4. Click **Save Linear Keys** then **Test Connection**
5. Switch to the **Tasks** tab and click **Linear** to sync your issues

### Jira Integration

1. Go to **Settings** tab
2. Enter your **Jira domain**, **email**, **API token** and **project key**
3. Get your API token from `id.atlassian.com → Security → API tokens`
4. Click **Save Jira Keys** then **Test Connection**

### Google AI Studio (Gemini) Integration

1. Go to **Settings** tab
2. Enter your **Google AI Studio API Key** — get it from [Google AI Studio](https://aistudio.google.com/app/apikey)
3. Click **Save** to store the key
4. Use the **🤖 Analyze Issue** button on any task to get AI-powered insights

---

## Data Storage

All data is stored locally on your machine:

| Data | Location |
|---|---|
| Projects, notes, tasks, links | `%AppData%\QAssistant\projects.json` |
| Storage logs | `%AppData%\QAssistant\storage.log` |
| File attachments | `%AppData%\QAssistant\Files\` |
| WebView2 sessions | `%AppData%\QAssistant\WebView2Data\` |
| API keys & credentials | Windows Credential Manager (`QAssistant_*`) |

> **Tip:** Go to **Settings → Diagnostics** to view the exact paths and open the log folder.

---

## Keyboard Shortcuts

| Action | Shortcut |
|---|---|
| Search notes and tasks | Click the search box in the tab bar |
| Dismiss notification banner | Click ✕ on the banner |
| Rename / edit project | Double-click project in sidebar |

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
│   ├── LinearComment.cs
│   └── SearchResult.cs
├── ViewModels/
│   └── MainViewModel.cs
├── Views/
│   ├── LinksPage.xaml
│   ├── NotesPage.xaml
│   ├── TasksPage.xaml
│   ├── FilesPage.xaml
│   └── SettingsPage.xaml
├── Services/
│   ├── StorageService.cs
│   ├── CredentialService.cs
│   ├── LinearService.cs
│   ├── JiraService.cs
│   ├── GeminiService.cs
│   ├── FileStorageService.cs
│   └── ReminderService.cs
├── MainWindow.xaml
└── App.xaml
```

---

## CI/CD

QAssistant uses GitHub Actions for automated builds and releases:

- **Trigger:** Push a tag matching `v*` (e.g., `v1.0.0`) or use manual workflow dispatch
- **Output:** A portable `.zip` containing:
  - `QAssistant.exe` (single-file, self-contained)
  - `WinAppRuntime_Setup.exe` (Windows App Runtime installer)
- **Download:** Releases are automatically published to [GitHub Releases](https://github.com/Lewandowskista/QAssistant/releases)

---

## Roadmap

- [x] Linear issue sync on Tasks board
- [x] AI-powered issue analysis with Gemini
- [x] System tray integration
- [x] Diagnostics panel in Settings
- [ ] Drag to reorder Kanban task cards
- [ ] Export notes to PDF or Markdown
- [ ] Jira issue sync on Tasks board
- [ ] Keyboard shortcuts for tab navigation
- [ ] Windows toast notifications for reminders
- [ ] Note-level file attachments
- [ ] Task comments history view

---

## Troubleshooting

### Projects not appearing in sidebar
1. Go to **Settings → Diagnostics**
2. Check the **Data Storage Path** shown
3. Click **Open Log File** to view recent operations
4. Click **Refresh Projects Sidebar** to force a UI refresh

### Data not persisting
1. Check that the `%AppData%\QAssistant` folder exists and is writable
2. Review `storage.log` for any error messages
3. Ensure you have write permissions to the AppData folder

---

## Contributing

Pull requests are welcome. For major changes please open an issue first to discuss what you would like to change.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes (`git commit -m 'Add my feature'`)
4. Push to the branch (`git push origin feature/my-feature`)
5. Open a Pull Request

---

## License

MIT — see [LICENSE](LICENSE) for details.

---

<div align="center">
  <sub>Built for QA engineers who like their tools dark, fast, and always on top.</sub>
</div>
