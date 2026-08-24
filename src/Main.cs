using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace KineticNapier.ADOFAIWorkbench
{
    public static class Main
    {
        internal const string Version = "0.2.2";
        private static bool enabled;
        private static bool suspendedForGameplay;

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
                suspendedForGameplay = false;
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
                suspendedForGameplay = false;
                NativeWorkbenchShell.SetVisible(false);
                ChartCameraViewport.Restore();
                StockEditorOverride.Restore();
                return;
            }

            bool gameplay = string.Equals(SceneManager.GetActiveScene().name, "scnGame", System.StringComparison.Ordinal);
            if (gameplay)
            {
                if (!suspendedForGameplay)
                {
                    NativeWorkbenchShell.SetVisible(false);
                    ChartCameraViewport.Restore();
                    suspendedForGameplay = true;
                }

                StockEditorOverride.Apply(editor);
                return;
            }

            if (suspendedForGameplay)
            {
                suspendedForGameplay = false;
                Workbench.RefreshAll();
            }

            StockEditorOverride.Apply(editor);
            NativeWorkbenchShell.Update(editor);
        }
    }
}
