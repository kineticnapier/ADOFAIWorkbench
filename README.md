# ADOFAIWorkbench

A standalone IDE-style tool window for A Dance of Fire and Ice mods.

ADOFAIWorkbench deliberately leaves the stock ADOFAI editor UI and rendering untouched. Since ADOFAI runs on Unity/Mono, the docking UI is hosted in a separate .NET Framework 4.8 process. The two processes communicate over an authenticated loopback-only TCP connection (`127.0.0.1`).

## Features

- Visual Studio-style tabbed panes powered by DockPanel Suite.
- Drag tool panes to dock left / right / top / bottom or combine them as tabs.
- Floating tool windows.
- Close and reopen panes from the `Panes` menu.
- Shared localization service for Workbench and third-party mod panes.
- Built-in `Language` pane; language choice is persisted at `%AppData%\ADOFAIWorkbench\language.txt`.
- Persist DockPanel layout with `SaveAsXml` / `LoadFromXml`.
- Layout is stored at `%AppData%\ADOFAIWorkbench\layout.xml`.
- Window bounds are stored separately at `%AppData%\ADOFAIWorkbench\window.txt`.
- Pane actions are dispatched back to Unity's main thread.
- ADOFAI's Canvas, camera and stock editor controls are never reparented or cropped.
- The Host watches the parent ADOFAI PID and terminates automatically when the game exits.
- The Unity side never performs socket I/O or process waits on the Unity main thread.

## Architecture

```text
ADOFAI / Unity / Mono process
+-------------------------------+
| ADOFAIWorkbench.dll           |
| - pane registry               |
| - localization registry       |
| - snapshots / view protocol   |
| - Unity main-thread queue     |
+---------------+---------------+
                |
                | 127.0.0.1 TCP
                | random per-run authentication token
                | pane views / actions
                v
ADOFAIWorkbench.Host.exe
.NET Framework 4.8 process
+-------------------------------+
| DockPanel Suite               |
| - tabs                        |
| - drag docking                |
| - nested splits               |
| - floating windows            |
| - layout persistence          |
+-------------------------------+
```

The bridge opens an ephemeral loopback port and launches the Host with the port, a random per-run token and the ADOFAI parent PID. The Host must authenticate with that token before pane data is sent. Nothing listens on a public network interface.

DockPanel Suite is intentionally not loaded inside Unity's Mono process. Upstream disables end-user docking when running on Mono, so the external host is part of the design rather than just an isolation convenience.

## Pane API

Other UMM mods can add Workbench panes by referencing `ADOFAIWorkbench.dll` and registering an `IDockablePaneProvider`.

```csharp
public interface IDockablePane
{
    string Id { get; }
    string Title { get; }
    bool CanClose { get; }
    WorkbenchPaneView BuildView();
    void HandleAction(string actionId, string argument);
}

public interface IDockablePaneProvider
{
    IEnumerable<IDockablePane> CreatePanes();
}
```

`WorkbenchPaneView` is a small process-safe UI description. Current primitives are text, buttons, text inputs, toggles, rows and spacers.

```csharp
private sealed class MyPane : IDockablePane
{
    public string Id { get { return "example.my-pane"; } }
    public string Title { get { return "My Tool"; } }
    public bool CanClose { get { return true; } }

    public WorkbenchPaneView BuildView()
    {
        return new WorkbenchPaneView()
            .Text("My Tool", 16f, true)
            .Input("value", "set-value")
            .Toggle("Enabled", "enabled", true)
            .BeginRow()
            .Button("Run", "run", "", false)
            .Button("Reset", "reset", "", false)
            .EndRow();
    }

    public void HandleAction(string actionId, string argument)
    {
        // Dispatched on Unity's main thread.
    }
}
```

Register, update and open panes with:

```csharp
Workbench.RegisterPaneProvider(provider);
Workbench.PublishPane("example.my-pane");
Workbench.OpenPane("example.my-pane");
```

Call `Workbench.PublishPane(id)` after the pane state changes so the external Host receives a fresh view snapshot. Call `Workbench.UnregisterPaneProvider(provider)` when the supplying mod is disabled or unloaded.

## Localization API

Localization lives in `ADOFAIWorkbench.dll`; third-party mods do not need to send dictionaries to the external Host. A mod registers one bundle per locale and resolves strings while building its pane.

```csharp
WorkbenchLocalization.Register(
    "example.mod",
    "en-US",
    "English",
    new Dictionary<string, string>
    {
        { "pane.title", "My Tool" },
        { "run", "Run" }
    });

WorkbenchLocalization.Register(
    "example.mod",
    "ja-JP",
    "日本語",
    new Dictionary<string, string>
    {
        { "pane.title", "マイツール" },
        { "run", "実行" }
    });
```

Resolve strings with a stable owner id and key:

```csharp
string title = WorkbenchLocalization.T("example.mod", "pane.title", "My Tool");
string message = WorkbenchLocalization.Format(
    "example.mod", "items", "Items: {0}", itemCount);
```

`T` resolves the current locale, then a neutral-language match, then `en-US` / `en`, and finally the supplied fallback (or the key when no fallback is supplied). Any BCP-47-style locale id can be registered; the built-in Workbench bundle currently supplies `en-US` and `ja-JP`.

Useful API members:

```csharp
string locale = WorkbenchLocalization.CurrentLanguage;
IList<WorkbenchLanguageInfo> languages = WorkbenchLocalization.AvailableLanguages;
WorkbenchLocalization.SetLanguage("ja-JP");
WorkbenchLocalization.LanguageChanged += OnLanguageChanged;
WorkbenchLocalization.UnregisterOwner("example.mod");
```

Changing the language marks the full Workbench pane registry dirty, so pane titles and `BuildView()` output are rebuilt automatically. `LanguageChanged` is available for mods that also cache localized strings outside their pane snapshots.

## Diagnostics

The Host status bar reports its IPC state. After a successful registry sync it shows:

```text
Connected | Panes=N
```

If no pane registry has arrived, the `Panes` menu contains a disabled `(No panes received)` entry instead of appearing to do nothing.

Unhandled Host UI exceptions are appended to:

```text
%AppData%\ADOFAIWorkbench\host-error.log
```

This is separate from the UnityModManager log because the docking Host is a separate process.

## Build output

The Workbench release contains:

```text
ADOFAIWorkbench.dll
ADOFAIWorkbench.Host.exe
WeifenLuo.WinFormsUI.Docking.dll
WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll
Info.json
THIRD_PARTY_NOTICES.md
licenses/DockPanelSuite-MIT.txt
```

`ADOFAIWorkbench.dll` is loaded by UnityModManager. `ADOFAIWorkbench.Host.exe` is launched by the mod and runs under the normal Windows .NET Framework runtime.

## Third-party software

ADOFAIWorkbench uses DockPanel Suite 3.1.1 and its VS2015 theme package. DockPanel Suite is licensed under the MIT License. The required copyright and permission notice is kept in `licenses/DockPanelSuite-MIT.txt` and is also included in release archives.

See `THIRD_PARTY_NOTICES.md` for details.

The DockPanel Suite license does not by itself define the license of ADOFAIWorkbench as a whole.
