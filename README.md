# ADOFAIWorkbench

A standalone IDE-style tool window for A Dance of Fire and Ice mods.

ADOFAIWorkbench deliberately leaves the stock ADOFAI editor UI and rendering untouched. The Workbench runs in its own WinForms window and hosts arbitrary tool panes registered by consumer mods.

## Features

- Visual Studio-style tabbed panes powered by DockPanel Suite.
- Drag panes to dock left / right / top / bottom or combine them as tabs.
- Floating tool windows.
- Close and reopen panes from the `Panes` menu.
- Persist DockPanel layout with `SaveAsXml` / `LoadFromXml`.
- Layout is stored at `%AppData%\ADOFAIWorkbench\layout.xml`.
- Window bounds are stored separately at `%AppData%\ADOFAIWorkbench\window.txt`.
- Consumer actions can be queued safely back to Unity's main thread.
- DockPanel dependencies are resolved explicitly from the Workbench mod directory so UnityModManager load context does not have to guess their location.

## Architecture

```text
ADOFAI / Unity main thread
        ^
        | Workbench.RunOnUnityThread(...)
        | command queue
        v
ADOFAI Workbench UI thread (STA)
        |
        +-- DockPanel Suite
        +-- docked / floating panes
        +-- consumer WinForms controls
```

Workbench does not reparent stock ADOFAI controls, crop the game camera, or replace the level editor Canvas.

## API

```csharp
public interface IDockablePane
{
    string Id { get; }
    string Title { get; }
    bool CanClose { get; }
    System.Windows.Forms.Control CreateView();
    void OnOpened();
    void OnClosed();
}

public interface IDockablePaneProvider
{
    IEnumerable<IDockablePane> CreatePanes();
}
```

Register and open panes with:

```csharp
Workbench.RegisterPaneProvider(provider);
Workbench.OpenPane("my-pane-id");
```

From a Workbench pane, enqueue any ADOFAI / Unity operation instead of calling Unity APIs from the UI thread:

```csharp
Workbench.RunOnUnityThread(() =>
{
    // Unity / scnEditor work here.
});
```

Use `Workbench.RunOnUiThread(...)` to publish snapshots or other UI updates back to the Workbench window.

## Third-party software

ADOFAIWorkbench uses DockPanel Suite 3.1.1 and its VS2015 theme package. DockPanel Suite is licensed under the MIT License. The required copyright and permission notice is kept in `licenses/DockPanelSuite-MIT.txt` and is also included in release archives.

See `THIRD_PARTY_NOTICES.md` for details.

The DockPanel Suite license does not by itself define the license of ADOFAIWorkbench as a whole.
