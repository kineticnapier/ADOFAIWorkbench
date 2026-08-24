using UnityModManagerNet;

namespace KineticNapier.ADOFAIWorkbench
{
    public static class Main
    {
        internal const string Version = "0.1.4";
        private static bool enabled;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            entry.OnToggle = OnToggle;
            entry.OnUpdate = OnUpdate;
            Workbench.RegisterPaneProvider(new ChartPaneProvider());
            Workbench.RegisterPaneProvider(new WelcomePaneProvider());
            Workbench.RegisterPaneProvider(new StockEditorPaneProvider());
            entry.Logger.Log("ADOFAI Workbench v" + Version + " loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            enabled = value;
            if (!value)
            {
                NativeWorkbenchShell.SetVisible(false);
                ChartCameraViewport.Restore();
                StockEditorOverride.Restore();
            }
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
        {
            if (!enabled) return;
            scnEditor editor = ADOBase.editor;
            if (editor == null)
            {
                NativeWorkbenchShell.SetVisible(false);
                ChartCameraViewport.Restore();
                StockEditorOverride.Restore();
                return;
            }

            StockEditorOverride.Apply(editor);
            NativeWorkbenchShell.Update(editor);
        }
    }
}
