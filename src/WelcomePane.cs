using System.Collections.Generic;

namespace KineticNapier.ADOFAIWorkbench
{
    internal sealed class WelcomePaneProvider : IDockablePaneProvider
    {
        private readonly WelcomePane pane = new WelcomePane();

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return pane;
        }
    }

    internal sealed class WelcomePane : IDockablePane
    {
        public string Id { get { return "workbench.welcome"; } }
        public string Title { get { return "Welcome"; } }
        public bool CanClose { get { return false; } }

        public WorkbenchPaneView BuildView()
        {
            return new WorkbenchPaneView()
                .Spacer(18)
                .Text("ADOFAI Workbench", 24f, true)
                .Spacer(10)
                .Text("Docking UI runs in a separate .NET Framework process. ADOFAI/Unity stays untouched and communicates over authenticated loopback TCP.", 11f, false)
                .Spacer(8)
                .Text("Consumer pane actions are dispatched back to Unity's main thread.", 10f, false)
                .Spacer(8)
                .Text("Welcome is a fixed document page; tool panes supplied by mods can be docked, split or floated.", 10f, false);
        }

        public void HandleAction(string actionId, string argument)
        {
        }
    }
}
