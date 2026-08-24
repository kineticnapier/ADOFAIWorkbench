using System;
using System.Reflection;
using UnityModManagerNet;

namespace KineticNapier.ADOFAIWorkbench
{
    public static class Main
    {
        internal const string Version = "0.2.4";
        private const int GameplayResumeDelayFrames = 4;

        private static bool enabled;
        private static bool suspendedForGameplay;
        private static int resumeWorkbenchFrame = -1;

        private static bool sceneReflectionInitialized;
        private static MethodInfo getActiveSceneMethod;
        private static PropertyInfo sceneNameProperty;

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
                resumeWorkbenchFrame = -1;
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
                resumeWorkbenchFrame = -1;
                NativeWorkbenchShell.SetVisible(false);
                ChartCameraViewport.Restore();
                StockEditorOverride.Restore();
                return;
            }

            // inStrictlyEditingMode is ADOFAI's own editor/play-mode flag.  Keep the
            // scene-name check as a secondary signal because scene activation can lead or
            // trail the editor flag by a frame during transitions.
            bool gameplay = !editor.inStrictlyEditingMode
                || string.Equals(GetActiveSceneName(), "scnGame", StringComparison.Ordinal);

            if (gameplay)
            {
                if (!suspendedForGameplay)
                {
                    NativeWorkbenchShell.SetVisible(false);
                    ChartCameraViewport.Restore();
                    suspendedForGameplay = true;
                    resumeWorkbenchFrame = -1;
                }

                ChartCameraViewport.ForceFullScreen();
                StockEditorOverride.Apply(editor);
                return;
            }

            if (suspendedForGameplay)
            {
                // SwitchToEditMode() clears the gameplay HUD, but several UI/camera
                // objects settle over the next frames.  Keep the stock camera fullscreen
                // long enough to repaint the whole framebuffer before docking it again.
                if (resumeWorkbenchFrame < 0)
                    resumeWorkbenchFrame = UnityEngine.Time.frameCount + GameplayResumeDelayFrames;

                if (UnityEngine.Time.frameCount < resumeWorkbenchFrame)
                {
                    ChartCameraViewport.ForceFullScreen();
                    StockEditorOverride.Apply(editor);
                    return;
                }

                suspendedForGameplay = false;
                resumeWorkbenchFrame = -1;
                Workbench.RefreshAll();
            }

            StockEditorOverride.Apply(editor);
            NativeWorkbenchShell.Update(editor);
        }

        private static string GetActiveSceneName()
        {
            try
            {
                EnsureSceneReflection();
                if (getActiveSceneMethod == null || sceneNameProperty == null) return null;

                object scene = getActiveSceneMethod.Invoke(null, null);
                return sceneNameProperty.GetValue(scene, null) as string;
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureSceneReflection()
        {
            if (sceneReflectionInitialized) return;
            sceneReflectionInitialized = true;

            Type sceneManagerType = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                sceneManagerType = assemblies[i].GetType("UnityEngine.SceneManagement.SceneManager", false);
                if (sceneManagerType != null) break;
            }

            if (sceneManagerType == null) return;

            getActiveSceneMethod = sceneManagerType.GetMethod(
                "GetActiveScene",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);

            if (getActiveSceneMethod != null)
                sceneNameProperty = getActiveSceneMethod.ReturnType.GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
        }
    }
}
