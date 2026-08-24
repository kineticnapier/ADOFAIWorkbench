# ADOFAIWorkbench

A dockable IDE-style workspace shell for the A Dance of Fire and Ice level editor.

ADOFAIWorkbench keeps the stock `scnEditor` / `LevelData` / chart world as the backend while allowing editor tools to provide arbitrary dockable panes. A chart editor, inspector, timeline, console, browser-like view, or another mod can all participate in the same split/tab workspace.

## Architecture

- **AdofaiEditorToolkit**: low-level ADOFAI/editor bridge and native UI host.
- **ADOFAIWorkbench**: tabs, splits, docking model, pane registry, focus and workspace shell.
- **Consumer mods** (for example ADOFAIMultiTileEditor): register their own `IDockablePaneProvider` implementations.

Only one stock `scnEditor` is assumed. A pane may use the live editor backend, a snapshot, or a completely unrelated UI.

## Initial API

```csharp
Workbench.RegisterPaneProvider(provider);
Workbench.OpenPane("my-pane-id");
```

Providers expose `IDockablePane` objects. Each pane receives a `RectTransform` when mounted and owns the UI below that transform.

The first implementation focuses on a reusable native Unity-uGUI shell and a recursive split tree. Drag/drop docking indicators and persisted layouts are intentionally left for the next iteration.
