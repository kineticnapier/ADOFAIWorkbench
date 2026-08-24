using UnityModManagerNet;

namespace KineticNapier.ADOFAIWorkbench
{
    public static class Main
    {
        internal const string Version = "0.1.2";
        private static bool enabled;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            entry.OnToggle = OnToggle;
            entry.OnUpdate = OnUpdate;
            Workbench.RegisterPaneProvider(new WelcomePaneProvider());
            entry.Logger.Log("ADOFAI Workbench v" + Version + " loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            enabled = value;
            if (!value)
            {
                NativeWorkbenchShell.SetVisible(false);
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
                StockEditorOverride.Restore();
                return;
            }

            StockEditorOverride.Apply(editor);
            NativeWorkbenchShell.Update(editor);
        }
    }
}
