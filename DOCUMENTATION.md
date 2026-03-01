# QAssistant — Full Feature Documentation

> **QAssistant** is a Windows desktop application built for QA engineers and development teams.  
> It centralises project management, test management, environment tracking, API testing, and AI-assisted analysis in one always-on-top tool.

---

## Table of Contents

1. [Application Layout](#1-application-layout)
2. [Projects Sidebar](#2-projects-sidebar)
3. [Global Search](#3-global-search)
4. [Notification Banners](#4-notification-banners)
5. [Window Controls](#5-window-controls)
6. [Dashboard](#6-dashboard)
7. [Links](#7-links)
8. [Notes](#8-notes)
9. [Files](#9-files)
10. [Tasks](#10-tasks)
11. [Tests](#11-tests)
12. [Test Data](#12-test-data)
13. [Checklists](#13-checklists)
14. [Environments](#14-environments)
15. [API Playground](#15-api-playground)
16. [SAP HAC](#16-sap-hac)
17. [Settings](#17-settings)

---

## 1. Application Layout

The application window is divided into three vertical panels:

| Panel | Width | Purpose |
|---|---|---|
| **Projects sidebar** (left) | 200 px | Lists all projects; create, rename, reorder, delete |
| **Tools sidebar** (centre) | 200 px (collapsible) | Navigation menu grouped by category |
| **Main content area** (right) | Fills remainder | Page content for the active tool |

A custom title bar spans the full width. The tools sidebar can be collapsed to a 40 px icon strip using the arrow button in its header.

---

## 2. Projects Sidebar

### Purpose
Organise all your work under isolated projects. Every page (Notes, Tasks, Files, etc.) displays data for the **currently selected project** only.

### How to use

| Action | How |
|---|---|
| **Create a project** | Click **+ New Project** at the bottom of the sidebar, enter a name, and press **Create** |
| **Select a project** | Click any project name in the list |
| **Rename / change colour** | Right-click a project → edit the name and pick one of eight colour swatches → **Save** |
| **Delete a project** | Right-click → **Delete** → confirm — removes all tasks, notes, files, etc. within it |
| **Reorder projects** | Drag-and-drop items in the list; the new order is persisted automatically |

**Colour coding** — a 4 × 16 px coloured bar is shown next to each project name. Available colours: Purple, Blue, Green, Red, Yellow, Pink, Gray, Orange.

---

## 3. Global Search

### Purpose
Quickly locate a **note** or **task** across all projects without leaving the current page.

### How to use

1. Type in the **Search…** box at the top of the tools sidebar.
2. Results appear as a drop-down list showing the item title, a short excerpt, and the parent project name.
3. Click a suggestion to navigate directly to **Notes** or **Tasks** for the relevant project, with the matching item pre-selected.

Search is case-insensitive and matches both the title and body/description of notes and tasks.

---

## 4. Notification Banners

### Purpose
Surface task due-date reminders without leaving the app. The reminder service runs in the background and shows colour-coded banners at the top of the content area.

### Banner types

| Colour | Category | Meaning |
|---|---|---|
| 🔴 Red border | **Overdue** | Task's due date has already passed; a live countup timer shows elapsed time (e.g. `+2h 14m 30s overdue`) |
| 🟡 Yellow border | **Due Today** | Task is due sometime today; a live countdown shows time remaining (e.g. `in 3h 22m 10s`) |
| 🟢 Green border | **Upcoming** | Task is due within the next few days; auto-dismissed after 5 seconds with a slide-up animation |

### Interaction

- **Click a banner** — navigates to the project and opens the task detail panel for the relevant task.
- **Dismiss (×)** — removes the banner immediately; the reminder will reappear on the next check cycle.

---

## 5. Window Controls

Located in the title bar (top-right):

| Control | Behaviour |
|---|---|
| **Settings (⚙)** | Opens the Settings dialog |
| **Pin / Unpin** | Toggles **Always on Top** — when pinned the window stays in front of all other windows; the button turns accent-coloured when active |
| **Minimise (−)** | Minimises the window (or hides to system tray if *Minimise to Tray* is enabled in Settings) |
| **Maximise / Restore (□)** | Toggles between maximised and restored state |
| **Close (×)** | Closes the app (or hides to tray if *Minimise to Tray* is enabled) |

---

## 6. Dashboard

**Navigation:** Tools sidebar → **Dashboard**

### Purpose
A read-only at-a-glance summary of the selected project's health.

### Metrics shown

| Card | Description |
|---|---|
| **Open Tasks** | Count of tasks not in Done / Canceled / Duplicate |
| **Critical Blockers** | Open tasks with *Critical* priority |
| **Pass Rate** | Percentage of test cases with status *Passed* out of total |
| **Test Cases** | Total test case count |
| **Overdue** | Tasks whose due date has passed and are still open |

### Sections

- **Task Breakdown** — horizontal bar chart of tasks grouped by status (Backlog, Todo, In Progress, In Review, Done, Canceled, Duplicate), with percentage fills.
- **Upcoming Due Dates** — up to 7 open tasks ordered by due date, showing *Today / Tomorrow / In N days* relative labels.
- **Test Plans** — the 5 most recently created non-archived test plans with pass rate, total count, and failed-case count.

---

## 7. Links

**Navigation:** Tools sidebar → **Links** (under **ORGANIZATION**)

### Purpose
A built-in web browser panel for URLs you visit repeatedly during QA work (e.g. staging environments, Figma designs, Confluence pages, Jira boards).

### How to use

| Action | How |
|---|---|
| **Add a link** | Click **+ Add Link**, enter a title, URL (`https://…`), and type, then **Add** |
| **Open a link** | Click its name in the left list — the URL loads in the embedded WebView2 browser on the right |
| **Edit a link** | Right-click a list item → edit title / URL → **Save** |
| **Delete a link** | Right-click → **Delete** |
| **Reorder links** | Drag-and-drop in the list |

**Link types** — you can categorise a link when adding it (e.g. *Staging*, *Documentation*, *Design*, etc.).  
**URL validation** — only `http://` and `https://` URLs are accepted.  
The embedded browser uses a sandboxed WebView2 environment with dev-tools, JS dialogs, and web messaging disabled for security.

---

## 8. Notes

**Navigation:** Tools sidebar → **Notes** (under **ORGANIZATION**)

### Purpose
A lightweight notepad for per-project notes (meeting minutes, investigation findings, deployment notes, etc.).

### How to use

| Action | How |
|---|---|
| **Create a note** | Click **+ New Note** — a blank note named "New Note" is created and selected |
| **Edit a note** | Click the note in the list → type in the **title** and **content** fields; changes auto-save after a 500 ms debounce |
| **Delete a note** | With a note open, click **Delete** → confirm |
| **Reorder notes** | Drag-and-drop in the left list |
| **Copy a note** | Select it in the list → `Ctrl+C` → `Ctrl+V` to paste as a copy |
| **Cut / move a note** | `Ctrl+X` on the selected note removes it; `Ctrl+V` pastes it |

### Attachments

Each note supports file attachments stored locally on disk.

| File type | Rendered as |
|---|---|
| Images (`.png`, `.jpg`, `.gif`, `.webp`, `.bmp`) | Inline thumbnail — tap to open a full preview dialog |
| Videos / Audio (`.mp4`, `.webm`, `.mp3`, etc.) | Inline media player with transport controls |
| All other files | Card showing filename, size, date added — **Open** (opens with the system default app) and **Delete** buttons |

**Last saved** timestamp is shown below the content area and updates on every auto-save.

---

## 9. Files

**Navigation:** Tools sidebar → **Files** (under **ORGANIZATION**)

### Purpose
A project-level file library for screenshots, design assets, test evidence, and other attachments that are not tied to a specific note.

### How to use

| Action | How |
|---|---|
| **Upload a file** | Click **Browse Files** → pick any file type from the system picker |
| **Paste a screenshot** | Copy a screen region (e.g. `Win+Shift+S`) then click **Paste Screenshot** — the clipboard bitmap is saved automatically as a `.bmp` file |
| **Drop files** | Drag one or more files from Explorer and drop anywhere onto the page |
| **Open a file** | Click **Open** on a file card — opens with the system default application |
| **Delete a file** | Click **Delete** on a file card — permanently removes the stored copy |
| **Reorder files** | Drag-and-drop the file cards |

Files are stored in `%AppData%\QAssistant\Files\` and referenced by absolute path. Each card shows a thumbnail (for images), file icon, name, size, and date added.

---

## 10. Tasks

**Navigation:** Tools sidebar → **Tasks** (under **QA BASIC**)

### Purpose
A Kanban board for managing project tasks. Supports three modes: **Manual** (local tasks), **Linear** (synced from Linear.app), and **Jira** (synced from Atlassian Jira).

---

### 10.1 Kanban Board

Seven columns are displayed: **Backlog → Todo → In Progress → In Review → Done → Canceled → Duplicate**.  
Each column shows a task count badge. Click any task card to open the detail panel.

**Drag-and-drop status change** — drag a task card from one column and drop it onto another column's drop zone. In Linear / Jira mode this also updates the status on the remote server.

---

### 10.2 Task Modes

#### Manual Mode
Click **Manual** in the mode toolbar (default).

- Uses tasks stored locally in the project.
- The **+ New Task** button is visible.
- Task status and priority can be edited from the detail panel.

**Creating a task:** Click **+ New Task** → fill in title, description, status, priority, an optional ticket URL, and an optional due date+time → **Add Task**.

**Editing a task (detail panel):**
1. Click a task card to open the right panel.
2. Switch to the **Details** tab.
3. Change **Status** and **Priority** via drop-downs.
4. Toggle **Set due date** to enable / disable the date-time picker.
5. Click **Save Changes** (or `Ctrl+S`).

**Deleting a task:** Open the task, click **Delete Task** → confirm (or `Ctrl+D`).

---

#### Linear Mode
Click **Linear** in the mode toolbar.

Requires **Linear API Key** and **Linear Team ID** configured in Settings.

- Fetches all issues from the configured team from the Linear API.
- Drag-and-drop columns map to Linear workflow states automatically.
- Click **Refresh** (or `Ctrl+R`) to re-fetch issues.

**Posting a comment:** Open a task → **Comments** tab → type in the comment box → **Post Comment**.

**Opening in Linear:** Open a task → click **Open in Linear** (or `Ctrl+O`).

---

#### Jira Mode
Click **Jira** in the mode toolbar.

Requires **Jira Domain**, **Email**, **API Token**, and **Project Key** configured in Settings.

- Fetches issues from the configured Jira project.
- Drag-and-drop moves work the same as Manual mode (Jira status sync via the API is not implemented for drag-and-drop).
- Click **Refresh** to re-fetch issues.

**Posting a comment:** Open a task → **Comments** tab → type → **Post Comment to Jira**.

**Opening in Jira:** Open a task → click **Open in Jira**.

---

### 10.3 Task Detail Panel

Opened by clicking any task card. Tabs:

| Tab | Content |
|---|---|
| **Details** | Editable status, priority, due date, assignee, labels (mode-dependent) |
| **Description** | Rendered Markdown-style description — supports `# Heading`, `## Sub-heading`, `- bullets`, and `` [code block] `` markers |
| **Comments** | Live-fetched comments from Linear or Jira; post new comments inline |
| **History** | Git-style versioned analysis history (see §10.4) |
| **Worklog** | Timeline of field changes fetched from Linear issue history or Jira changelog |

**Media section** — images and videos referenced in the description or attached to the issue are rendered below the description. Click an image to open a full-screen lightbox. The lightbox has **Open in browser** and **Close** controls.

Close the detail panel with the **×** button or press `Escape`.

---

### 10.4 AI Analysis

**Requires** a Google AI Studio (Gemini) API key configured in Settings.

1. Open any task in the detail panel.
2. Click **Analyze** (available in all three modes).
3. QAssistant downloads any image attachments, fetches existing comments, and sends a structured prompt to the Gemini API.
4. A busy overlay is shown during analysis.
5. The result dialog shows four structured sections: **Root Cause Analysis**, **Impact Assessment**, **Suggested Fix**, **Prevention Recommendations**.
6. Click **Copy to Clipboard** to copy the raw Markdown.

Each analysis is stored in the **History** tab with:
- Version number (`v1`, `v2`, …)
- Short hash of the result content
- Timestamp
- Task status + priority at the time of analysis
- Summary (first meaningful line, up to 120 characters)
- Expandable full analysis

Delete an individual history entry using the trash icon on each entry card.

---

### 10.5 Bug Report Generator

1. Open any task.
2. Click **Bug Report**.
3. A dialog appears with:
   - **Environment** picker (populated from the project's Environments list)
   - **Reporter** name field (optional)
   - **Include AI analysis** checkbox (available when analysis history exists)
   - **Report Preview** — live-updating editable Markdown preview
4. Click **Copy Markdown** to copy to clipboard.
5. In Linear/Jira mode: click **Post to Linear** / **Post to Jira** to create a new issue on the remote tracker with the report as the body. The new issue URL is shown in the status bar and opened in the browser.

---

### 10.6 Keyboard Shortcuts (Tasks page)

| Shortcut | Action |
|---|---|
| `Escape` | Dismiss shortcuts overlay → close lightbox → close detail panel (in order) |
| `Ctrl+/` | Toggle keyboard shortcut overlay |
| `Ctrl+1` | Switch to Manual mode |
| `Ctrl+2` | Switch to Linear mode |
| `Ctrl+R` | Refresh (Linear/Jira mode) |
| `Ctrl+N` | New task (Manual mode) |
| `Ctrl+S` | Save task changes (detail panel open, Manual mode) |
| `Ctrl+D` | Delete task (detail panel open, Manual mode) |
| `Ctrl+O` | Open in Linear (detail panel open, Linear mode) |

---

## 11. Tests

**Navigation:** Tools sidebar → **Tests** (under **QA BASIC**)

### Purpose
Manage test plans and test cases, run tests, track coverage, and generate reports — all integrated with Linear and Jira for issue-driven test generation.

The Tests page has five sub-tabs:

---

### 11.1 Test Case Generation

**Requires** a Gemini API key in Settings.

**From Linear / Jira issues:**
1. Select the source (**Linear** or **Jira**) from the drop-down.
2. Optionally upload a **design document** (`.txt`, `.md`, `.pdf`, `.docx`, `.csv`, `.json`, `.xml`, `.html`) as extra context — the first 30,000 characters are used.
3. Click **Generate Test Cases**.
4. QAssistant fetches all issues from the configured integration, builds a structured prompt, and calls the Gemini API.
5. Generated test cases are organised into a new **Test Plan** named after the source and timestamp.

**From CSV / Excel:**
1. Click **Import CSV / Excel** and select a `.csv` or `.xlsx` file.
2. A column mapping dialog appears — map spreadsheet columns to test case fields (Title, Steps, Expected Result, etc.) and give the plan a name.
3. Click **Import** — a new Test Plan is created with one test case per row.

**Test Plan display:** Plans are shown as collapsible groups. Each plan shows its ID (e.g. `TP-001`), name, source badge, case count, and pass/fail summary. Test cases inside show their ID, title, priority, and current status.

**Archiving a plan:** Use the archive button on the plan header to hide it from the default view. Toggle *Show Archived* to include archived plans.

---

### 11.2 Test Runs (Execution)

The **Test Runs** sub-tab is where you record test execution results.

1. Click **+ New Test Run** to start an execution session — select one or more Test Plans to include.
2. For each test case:
   - Set status: **Pass**, **Fail**, **Skip**, or **Blocked**.
   - (Optional) Enter **Actual Result**, **Test Data**, and **Notes**.
   - Optionally **Generate Bug Report** directly from a failed test case.
3. Complete the run to save an execution snapshot.

Execution history is shown per run with pass/fail counts and a percentage. Toggle *Show Archived Runs* to include older runs.

---

### 11.3 Reports

An aggregated dashboard showing:

- Overall pass rate across all test plans
- Per-plan metrics (total, passed, failed, skipped)
- Trend charts if multiple execution runs exist
- Quick-access to generate bug reports for failing test cases

---

### 11.4 Coverage Matrix

Displays a matrix of **issues (Linear/Jira) vs. test cases**, showing which issues have coverage and how many test cases are linked to each.

Toggle the **view mode** between:
- **Issue view** — rows are issues, columns are test case statuses
- **Test Case view** — rows are test cases with their linked issue IDs

---

### 11.5 Regression Builder

Build a smoke / regression subset from existing test cases:

1. Select test plans to include.
2. Filter by priority, status, or tags.
3. A prioritised list of recommended test cases for the regression run is generated.
4. Save the subset as a new Test Plan to use in Test Runs.

---

## 12. Test Data

**Navigation:** Tools sidebar → **Test Data** (under **QA BASIC**)

### Purpose
Store and organise test data as reusable key-value groups, and access a library of SAP Commerce ImpEx templates.

---

### 12.1 Test Data Groups

A **Group** is a named collection of key-value entries (e.g. "Customer Accounts", "Product Codes", "Promo Codes").

| Action | How |
|---|---|
| **Create a group** | Click **+ New Group**, give it a name and category |
| **Add an entry** | Open a group → click **+ Add Entry** → enter Key, Value, an optional Description, Tags, and Environment |
| **Copy a value** | Click the copy icon on an entry card — the value is placed on the clipboard |
| **Delete an entry** | Click the delete (trash) icon on the entry card |
| **Delete a group** | Select it and use the **Delete Group** button |

**Categories** available: Users, Products, Promotions, Cart, Orders, Credentials, URLs, Other.  
**Environment** field on entries lets you mark which environment (Dev / Staging / Prod) each data value applies to.  
Changes auto-save when any field is modified.

---

### 12.2 SAP Commerce ImpEx Templates

A built-in library of ready-to-use SAP Commerce ImpEx scripts. Templates are read-only reference snippets — copy and adapt them for your environment.

**Available template categories:**

| Category | Templates included |
|---|---|
| **Products** | Create Product (Staged), Set Product Price |
| **Customers** | Create B2C Customer, Create B2B Customer, Create B2B Unit, Create User Group, Customer Delivery Address |
| **Stock** | Set Stock Level, Force Out Of Stock |
| **Promotions** | Percentage Discount, Fixed Amount Discount, Free Gift (BOGO), Single-Use Coupon Code |
| **Catalog** | Catalog Sync Job (Staged → Online) |

Use the **category filter** buttons (All / Products / Customers / Promotions / Stock / Catalog) to narrow down the list. Each template card shows:
- Template name
- Category badge (colour-coded)
- Short description
- The ImpEx script in a monospace preview box with a **Copy** button

Placeholder tokens in the scripts (e.g. `{{productCode}}`, `{{customerEmail}}`) should be replaced with actual values from your Test Data groups.

---

## 13. Checklists

**Navigation:** Tools sidebar → **Checklists** (under **QA BASIC**)

### Purpose
Reusable, stateful QA checklists for pre-deployment gates, release sign-offs, SAP Commerce verifications, and any other repeatable procedure. Each checklist is per-project and tracks individual item completion with a live progress bar.

### How to use

| Action | How |
|---|---|
| **Create a checklist** | Click **+ New Checklist** in the sidebar — a blank checklist named "New Checklist" is created |
| **Load built-in templates** | Click **Load SAP Built-ins** — inserts the five built-in SAP Commerce checklists if they don't already exist |
| **Rename / recategorise** | Select a checklist → edit the **Name** and **Category** fields → **Save** |
| **Add an item** | Click **+ Add Item** — a new item row appears; type the step text inline |
| **Check / uncheck an item** | Click the checkbox on any item row — progress updates immediately and is auto-saved |
| **Delete an item** | Click the trash icon on the item row |
| **Reset all items** | Click **Reset All** — unchecks every item in the checklist |
| **Delete a checklist** | Click **Delete** → confirm |

### Categories

| Category | Intended use |
|---|---|
| **Pre-Deployment** | Go/no-go gate checks before releasing to staging or production |
| **Release Sign-off** | Final QA lead sign-off steps for a release candidate |
| **SAP Commerce** | Platform-specific checks (catalog sync, ImpEx, CronJobs, Backoffice) |
| **OCC Contract** | Verification that OCC API endpoints meet their expected contract |
| **Smoke Test** | Minimal post-deployment verification steps |
| **Custom** | Any user-defined purpose |

### Built-in SAP Commerce templates

Loaded via **Load SAP Built-ins**; existing built-ins are never duplicated:

| Template | Category | Items |
|---|---|---|
| **Pre-Deployment QA** | Pre-Deployment | 7 items — critical bug status, regression suite, performance, security scan, smoke test, rollback plan, release notes |
| **Catalog Sync Verification** | SAP Commerce | 7 items — staged/online item counts, sync job log, spot-check products, Solr re-index, search results, storefront PDP |
| **OCC API Contract Validation** | OCC Contract | 8 items — product GET, cart POST, order POST, error codes, OAuth2, pagination |
| **CronJob Health Check** | SAP Commerce | 6 items — Solr, CatalogSync, CleanUp, ProcessOrders, stuck-job check, trigger intervals |
| **ImpEx Import Validation** | SAP Commerce | 6 items — data model validation, dev-env test import, error log, Backoffice visibility, rollback ImpEx, encoding |

### Progress tracking

A **progress bar** at the bottom of the editor shows `done / total (%)` in real time. The sidebar list also shows the current `x/y checked` count for each checklist at a glance.

---

## 14. Environments

**Navigation:** Tools sidebar → **Environments** (under **QA ADVANCED**)

### Purpose
Store connection details for every environment in your project (Development, Staging, Production, Custom) and perform live health checks.

### How to use

| Action | How |
|---|---|
| **Add an environment** | Click **+ Add Environment** — a new environment named "New Environment" is created |
| **Configure** | Select an environment → fill in all fields → **Save** |
| **Set as default** | Tick **Default** and save, or click **Switch Active** — the active environment is used by SAP HAC and other tools |
| **Test connection** | Click **Test Connection** — sends an HTTP GET to the Base URL and displays the status code |
| **Check all health** | Click **Check All Health** — pings the Health Check URL of every environment simultaneously |
| **Delete** | Click **Delete Environment** → confirm |

### Environment fields

| Field | Description |
|---|---|
| **Name** | Human-readable label (e.g. "Staging EU") |
| **Type** | Development / Staging / Production / Custom |
| **Base URL** | Primary URL (e.g. `https://staging.example.com`) |
| **Health Check URL** | Endpoint polled for the live health indicator (coloured dot in the sidebar list) |
| **HAC URL** | SAP Hybris Administration Console URL — required for SAP HAC features |
| **Backoffice URL** | SAP Commerce Backoffice URL |
| **Storefront URL** | Storefront URL |
| **Solr Admin URL** | Solr Admin UI URL |
| **OCC Base Path** | OCC API base path (e.g. `/occ/v2`) |
| **Username / Password** | Credentials stored securely in Windows Credential Manager |
| **Notes** | Free-text notes about the environment |

**Health indicator** — a coloured dot is shown next to each environment name in the list: 🟢 Healthy, 🔴 Unhealthy, ⚫ Unknown. Health is polled in the background while the Environments page is open.

---

## 15. API Playground

**Navigation:** Tools sidebar → **API** (under **QA ADVANCED**)

### Purpose
A lightweight API client for sending HTTP requests and inspecting responses — similar to Postman, built directly into QAssistant with SAP Commerce OCC and HAC template libraries.

---

### 15.1 Managing Requests

| Action | How |
|---|---|
| **Create a request** | Click **+ New Request** — a blank GET request is created |
| **Load OCC templates** | Click **Load OCC Templates** — inserts a pre-built library of SAP Commerce OCC API calls (carts, products, users, etc.) if they don't already exist |
| **Load HAC templates** | Click **Load HAC Templates** — inserts a library of HAC-related calls |
| **Save a request** | Edit the request fields and click **Save** |
| **Delete a request** | Click **Delete** → confirm |

The left panel lists all saved requests, grouped and colour-coded by HTTP method (GET=green, POST=blue, PUT=yellow, PATCH=purple, DELETE=red).

---

### 15.2 Sending Requests

1. Select or create a request.
2. Fill in:
   - **Name** — display label
   - **Category** — OCC / HAC / Jira / Linear / Custom
   - **Method** — GET / POST / PUT / PATCH / DELETE
   - **URL** — the full request URL
   - **Headers** — one `Key: Value` per line
   - **Body** — JSON body for POST / PUT / PATCH requests
3. Click **Send**.

**Response panel** shows:
- HTTP status code with colour coding (green < 300, yellow < 400, red ≥ 400)
- Duration in milliseconds
- Pretty-printed JSON body (or raw text)
- Response headers

The response body can be copied using the **Copy** button.

---

### 15.3 Request History

Each request stores the last **20 responses** automatically. The History tab within the response panel shows previous calls with their status codes, timestamps, and durations. Click any history entry to restore its response body.

---

### 15.4 Response Comparison

The Compare tab lets you select two history entries side-by-side to diff the response bodies — useful for detecting regressions between API versions.

---

## 16. SAP HAC

**Navigation:** Tools sidebar → **SAP HAC** (under **QA ADVANCED**)

### Purpose
Direct integration with **SAP Commerce Hybris Administration Console (HAC)** — monitor cronjobs, compare catalog sync states, run FlexSearch queries, and execute ImpEx scripts, all without opening a browser.

### Connecting

1. Select an environment from the **Environment** picker (only environments with a HAC URL are listed).
2. Click **Connect** — QAssistant logs in to HAC using the credentials stored for that environment.
3. The connection status shows "✓ Connected" or "✗ Login failed".

The page has four tabs:

---

### 16.1 Cronjobs

- Click **Refresh** to fetch all cronjob entries from HAC.
- The table shows: **Code**, **Status**, **Last Result**, **Next Activation Time**, **Trigger Active**.
- Status is colour-coded: green (FINISHED), red (ERROR), blue (RUNNING), grey (other).
- **Filter** buttons: All / Running / Error / Finished / Warning.
- **Critical Alerts** — a banner appears automatically if any cronjob is in ERROR or has been running for an unusually long time.
- Click **History** on a row to fetch and display the execution history for that specific cronjob.

---

### 16.2 Catalog Sync

- Select **Source** and **Target** catalog versions.
- Click **Compare** — QAssistant reads both catalog version sync states and computes a diff showing items only in source, only in target, or differing between them.
- Previous comparison results are stored in a **Sync History** list for the current session — click any history entry to view its diff again.

---

### 16.3 FlexSearch

- Type a FlexSearch query in the editor (e.g. `SELECT {pk} FROM {Product} WHERE {code} = 'myProduct'`).
- Click **Execute** — results are displayed in a scrollable data table.
- Results can be **copied to clipboard** as a tab-delimited string.

---

### 16.4 ImpEx

- Paste or type an ImpEx script in the editor.
- Click **Execute** — QAssistant posts the script to HAC's ImpEx import endpoint.
- Execution output (errors, warnings, success count) is shown below.
- Click **Clear** to reset the editor.

---

## 17. Settings

**Opened by:** Gear icon (⚙) in the title bar.

Settings are organised in sections. Credentials are stored in **Windows Credential Manager** and are scoped to the currently selected project (displayed at the top: *"Configuring keys for: Project Name"*).

---

### 17.1 General

| Setting | Description |
|---|---|
| **Minimise to Tray** | When enabled, the Close and Minimise buttons hide the window to the system tray instead of exiting. Double-click the tray icon to restore. Default: enabled |

---

### 17.2 Automation API

An optional local HTTP API that lets automation frameworks (Playwright, Cypress, Selenium, etc.) communicate with QAssistant at runtime.

| Setting | Description |
|---|---|
| **Enable Automation API** | Toggle to start / stop the local HTTP listener |
| **Port** | The port number to listen on (default: `5248`). Click **Save Port** then toggle the API off/on to apply |
| **API Key** | Auto-generated secret key that must be sent as a header in all requests. Click **Regenerate Key** to issue a new one |

**Endpoints exposed (localhost only):**
- `GET /testcases` — returns test cases for the selected project
- `POST /results` — accepts execution results from a test runner

---

### 17.3 Linear Integration

| Field | Description |
|---|---|
| **API Key** | Your Linear personal API key (Settings → API in Linear.app). Click **Open Linear API Settings** to go there directly |
| **Team ID** | The Linear team ID to sync issues from |

Buttons: **Save**, **Test Connection** (verifies credentials and shows the number of teams found), **Disconnect** (removes stored credentials for this project).

---

### 17.4 Jira Integration

| Field | Description |
|---|---|
| **Domain** | Your Jira domain, e.g. `yourcompany.atlassian.net` |
| **Email** | The email address associated with your Atlassian account |
| **API Token** | Your Atlassian API token. Click **Open Atlassian API Tokens** to go there |
| **Project Key** | The Jira project key to sync issues from (e.g. `QA`) |

Buttons: **Save**, **Test Connection**, **Disconnect**.

---

### 17.5 Google AI (Gemini)

| Field | Description |
|---|---|
| **Gemini API Key** | Your Google AI Studio API key — used for AI analysis on Tasks and AI-driven test case generation in Tests |

Button: **Save API Key**.

---

### 17.6 SAP Commerce Context

| Setting | Description |
|---|---|
| **Enable SAP Commerce Context** | When on, the AI analysis prompt includes additional SAP Commerce domain context to improve relevance of analysis for SAP projects |

---

### 17.7 Storage Diagnostics

Shows the absolute file paths where QAssistant stores its data:

| Path | Content |
|---|---|
| **Data path** | The JSON file where all project data is persisted |
| **Log path** | Application log file location |

A **Refresh Projects Sidebar** button is available in case the sidebar gets out of sync.

---

## Appendix A — Data Storage

All project data is persisted locally as a JSON file in `%AppData%\QAssistant\`. Files uploaded via Notes or Files are copied to `%AppData%\QAssistant\Files\`. Credentials are stored in the Windows Credential Manager and never written to disk in plain text.

---

## Appendix B — Supported File Types

| Category | Extensions |
|---|---|
| Images | `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.bmp` |
| Video | `.mp4`, `.webm` |
| Audio | `.mp3`, `.wav`, `.ogg`, `.flac` |
| Documents | `.pdf`, `.txt`, `.md`, `.docx`, `.doc`, `.rtf` |
| Data | `.csv`, `.json`, `.xml`, `.html`, `.xlsx` |
| Archives | `.zip` |
| Other | Any file type (shown as generic attachment) |

---

## Appendix C — Keyboard Shortcuts Summary

| Shortcut | Page | Action |
|---|---|---|
| `Ctrl+/` | Tasks | Toggle shortcut help overlay |
| `Escape` | Tasks | Close overlay / lightbox / detail panel |
| `Ctrl+1` | Tasks | Switch to Manual mode |
| `Ctrl+2` | Tasks | Switch to Linear mode |
| `Ctrl+R` | Tasks | Refresh (Linear/Jira mode) |
| `Ctrl+N` | Tasks | New task (Manual mode) |
| `Ctrl+S` | Tasks | Save task changes |
| `Ctrl+D` | Tasks | Delete task |
| `Ctrl+O` | Tasks | Open task in Linear |
| `Ctrl+C` | Notes | Copy selected note |
| `Ctrl+X` | Notes | Cut selected note |
| `Ctrl+V` | Notes | Paste copied/cut note |

---

*Documentation generated for QAssistant — © 2026 Lewandowskista. Licensed under the GNU Affero General Public License v3.*
