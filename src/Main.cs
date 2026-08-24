using UnityModManagerNet;

namespace KineticNapier.ADOFAIWorkbench
{
    public static class Main
    {
        internal const string Version = "0.3.1";
        private static bool enabled;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            entry.OnToggle = OnToggle;
            entry.OnUpdate = OnUpdate;
            Workbench.RegisterPaneProvider(new WelcomePaneProvider());
            entry.Logger.Log("ADOFAI Workbench v" + Version + " loaded (standalone WinForms window mode).");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            enabled = value;
            if (value) Workbench.ShowWindow();
            else Workbench.HideWindow();
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
        {
            if (!enabled) return;
            Workbench.DrainUnityActions(64);
        }
    }
}
