# ADOFAIWorkbench

A standalone IDE-style tool window for A Dance of Fire and Ice mods.

ADOFAIWorkbench deliberately leaves the stock ADOFAI editor UI and rendering untouched. The Workbench runs in its own WinForms window and hosts arbitrary tool panes registered by consumer mods.

## Features

- Recursive horizontal / vertical split tree.
- Tabbed panes in every dock group.
- Drag tabs between groups.
- Drop a tab near the left / right / top / bottom edge of a group to create a new split there.
- Close and reopen panes.
- Close groups by merging their panes into the neighboring group.
- Persist window bounds, split ratios, pane placement, tab order, active tabs and focused group.
- Layout is stored at `%AppData%\ADOFAIWorkbench\layout.xml`.
- Consumer actions can be queued safely back to Unity's main thread.

## Architecture

```text
ADOFAI / Unity main thread
        ^
        | Workbench.RunOnUnityThread(...)
        | command queue
        v
ADOFAI Workbench UI thread
        |
        +-- recursive split tree
        +-- tab groups
        +-- consumer panes
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
