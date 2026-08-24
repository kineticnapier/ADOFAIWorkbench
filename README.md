# ADOFAIWorkbench

A standalone IDE-style tool window for A Dance of Fire and Ice mods.

ADOFAIWorkbench deliberately leaves the stock ADOFAI editor UI and rendering untouched. Since ADOFAI runs on Unity/Mono, the docking UI is hosted in a separate .NET Framework 4.8 process and communicates with the mod through a named pipe.

## Features

- Visual Studio-style tabbed panes powered by DockPanel Suite.
- Drag panes to dock left / right / top / bottom or combine them as tabs.
- Floating tool windows.
- Close and reopen panes from the `Panes` menu.
- Persist DockPanel layout with `SaveAsXml` / `LoadFromXml`.
- Layout is stored at `%AppData%\ADOFAIWorkbench\layout.xml`.
- Window bounds are stored separately at `%AppData%\ADOFAIWorkbench\window.txt`.
- Pane actions are dispatched back to Unity's main thread.
- ADOFAI's Canvas, camera and stock editor controls are never reparented or cropped.

## Architecture

```text
ADOFAI / Unity / Mono process
+-------------------------------+
| ADOFAIWorkbench.dll           |
| - pane registry               |
| - snapshots / view protocol   |
| - Unity main-thread queue     |
+---------------+---------------+
                |
                | named pipe
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

DockPanel Suite is intentionally not loaded inside Unity's Mono process. Upstream disables end-user docking when running on Mono, so the external host is part of the design rather than just an isolation convenience.

## Pane API

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

`WorkbenchPaneView` is a small process-safe UI description. Current primitives are text, buttons, rows and spacers.

```csharp
public WorkbenchPaneView BuildView()
{
    return new WorkbenchPaneView()
        .Text("My Tool", 16f, true)
        .BeginRow()
        .Button("Run", "run", "", false)
        .Button("Reset", "reset", "", false)
        .EndRow();
}

public void HandleAction(string actionId, string argument)
{
    // Called on Unity's main thread.
}
```

Register, update and open panes with:

```csharp
Workbench.RegisterPaneProvider(provider);
Workbench.PublishPane("my-pane-id");
Workbench.OpenPane("my-pane-id");
```

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
