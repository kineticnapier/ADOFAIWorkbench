using System.Collections.Generic;
using ADOFAI.EditorToolkit.Game;
using UnityEngine;

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
        private GameObject root;

        public string Id { get { return "workbench.welcome"; } }
        public string Title { get { return "Welcome"; } }
        public bool CanClose { get { return false; } }

        public void Mount(RectTransform parent)
        {
            Unmount();
            root = new GameObject("WelcomePane", typeof(RectTransform));
            RectTransform rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            GameObject title = ADOFAIEditorUiHost.CloneStockObject("findFloorPanelTitle", rect, "Title");
            RectTransform titleRect = title.transform as RectTransform;
            if (titleRect != null)
            {
                titleRect.anchorMin = new Vector2(0.5f, 0.5f);
                titleRect.anchorMax = new Vector2(0.5f, 0.5f);
                titleRect.pivot = new Vector2(0.5f, 0.5f);
                titleRect.anchoredPosition = new Vector2(0f, 20f);
                titleRect.sizeDelta = new Vector2(700f, 50f);
            }
            SetText(title, "ADOFAI Workbench");
            SetFontSize(title, 28f);

            GameObject subtitle = ADOFAIEditorUiHost.CloneStockObject("findFloorPanelTitle", rect, "Subtitle");
            RectTransform subRect = subtitle.transform as RectTransform;
            if (subRect != null)
            {
                subRect.anchorMin = new Vector2(0.5f, 0.5f);
                subRect.anchorMax = new Vector2(0.5f, 0.5f);
                subRect.pivot = new Vector2(0.5f, 0.5f);
                subRect.anchoredPosition = new Vector2(0f, -24f);
                subRect.sizeDelta = new Vector2(850f, 40f);
            }
            SetText(subtitle, "Dockable workspace shell active. Consumer mods can register panes through Workbench.RegisterPaneProvider(...).");
            SetFontSize(subtitle, 15f);
        }

        public void Unmount()
        {
            if (root != null) Object.Destroy(root);
            root = null;
        }

        private static void SetText(GameObject go, string text)
        {
            Component[] components = go.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                System.Reflection.PropertyInfo property = component.GetType().GetProperty("text");
                if (property != null && property.CanWrite && property.PropertyType == typeof(string))
                    property.SetValue(component, text, null);
            }
        }

        private static void SetFontSize(GameObject go, float size)
        {
            Component[] components = go.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                System.Reflection.PropertyInfo property = component.GetType().GetProperty("fontSize");
                if (property != null && property.CanWrite && property.PropertyType == typeof(float))
                    property.SetValue(component, size, null);
            }
        }
    }
}
