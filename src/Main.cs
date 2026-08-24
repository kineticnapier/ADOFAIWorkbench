using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;

namespace KineticNapier.ADOFAIWorkbench
{
    public static class Main
    {
        internal const string Version = "0.6.0";
        private static bool enabled;
        private static UnityModManager.ModEntry modEntry;

        internal static string ModDirectory { get; private set; }

        public static bool Load(UnityModManager.ModEntry entry)
        {
            modEntry = entry;
            ModDirectory = ResolveModDirectory(entry);
            entry.OnToggle = OnToggle;
            entry.OnUpdate = OnUpdate;

            try
            {
                Workbench.RegisterPaneProvider(new WelcomePaneProvider());
                entry.Logger.Log("ADOFAI Workbench v" + Version + " loaded (external .NET Framework DockPanel host). ModDir=" + ModDirectory);
                return true;
            }
            catch (Exception ex)
            {
                LogError("ADOFAI Workbench load failed", ex);
                return false;
            }
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            enabled = value;
            try
            {
                if (value) Workbench.ShowWindow();
                else Workbench.HideWindow();
                return true;
            }
            catch (Exception ex)
            {
                LogError("ADOFAI Workbench toggle failed", ex);
                return false;
            }
        }

        private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
        {
            if (!enabled) return;
            Workbench.DrainUnityActions(64);
        }

        internal static void Log(string message)
        {
            try
            {
                if (modEntry != null && modEntry.Logger != null)
                    modEntry.Logger.Log("[ADOFAIWorkbench] " + message);
            }
            catch { }
        }

        internal static void LogError(string message, Exception ex)
        {
            string text = "[ADOFAIWorkbench] ERROR: " + message;
            if (ex != null) text += Environment.NewLine + ex;
            try
            {
                if (modEntry != null && modEntry.Logger != null)
                    modEntry.Logger.Log(text);
            }
            catch { }
        }

        private static string ResolveModDirectory(UnityModManager.ModEntry entry)
        {
            try
            {
                if (entry != null)
                {
                    Type type = entry.GetType();
                    PropertyInfo property = type.GetProperty("Path", BindingFlags.Public | BindingFlags.Instance);
                    if (property != null)
                    {
                        string value = property.GetValue(entry, null) as string;
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }

                    FieldInfo field = type.GetField("Path", BindingFlags.Public | BindingFlags.Instance);
                    if (field != null)
                    {
                        string value = field.GetValue(entry) as string;
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
                }
            }
            catch { }

            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(location)) return Path.GetDirectoryName(location);
            }
            catch { }

            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
