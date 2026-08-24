using System;
using System.Collections.Generic;
using ADOFAI.EditorToolkit.Game;
using UnityEngine;

namespace KineticNapier.ADOFAIWorkbench
{
    internal static class NativeWorkbenchShell
    {
        private const string HostName = "ADOFAI Workbench Root";
        private const float ToolbarHeight = 38f;
        private const float TabHeight = 34f;
        private const float StatusHeight = 22f;
        private const float Gap = 3f;

        private static readonly DockWorkspace workspace = new DockWorkspace();
        private static readonly Dictionary<DockGroup, IDockablePane> mounted = new Dictionary<DockGroup, IDockablePane>();
        private static scnEditor editor;
        private static RectTransform host;
        private static RectTransform frame;
        private static string signature;
        private static string status = "Workbench ready.";
        private static bool defaultSeeded;

        static NativeWorkbenchShell()
        {
            Workbench.RegistryChanged += Invalidate;
        }

        internal static void Update(scnEditor activeEditor)
        {
            if (activeEditor == null) return;
            if (!ReferenceEquals(editor, activeEditor))
            {
                UnmountAll();
                editor = activeEditor;
                host = null;
                frame = null;
                signature = null;
                defaultSeeded = false;
            }

            EnsureHost();
            host.gameObject.SetActive(true);
            EnsureDefaultPane();

            string next = BuildSignature();
            if (!string.Equals(signature, next, StringComparison.Ordinal))
            {
                Rebuild();
                signature = BuildSignature();
            }
        }

        internal static void SetVisible(bool value)
        {
            if (host != null) host.gameObject.SetActive(value);
            if (!value) UnmountAll();
        }

        internal static void OpenPane(IDockablePane pane)
        {
            if (pane == null) return;
            workspace.OpenInFocused(pane.Id);
            status = "Opened " + pane.Title + ".";
            Invalidate();
        }

        private static void EnsureDefaultPane()
        {
            if (defaultSeeded) return;
            defaultSeeded = true;
            foreach (IDockablePane pane in Workbench.Panes)
            {
                if (pane == null) continue;
                workspace.OpenInFocused(pane.Id);
                break;
            }
        }

        private static void EnsureHost()
        {
            if (host != null) return;
            host = ADOFAIEditorUiHost.GetOrCreateOverlayRoot(HostName);
            Stretch(host, Vector2.zero, Vector2.zero);
        }

        private static void Rebuild()
        {
            EnsureHost();
            UnmountAll();
            if (frame != null)
            {
                frame.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(frame.gameObject);
            }

            frame = CreateRect(host, "WorkbenchFrame");
            Stretch(frame, Vector2.zero, Vector2.zero);
            RectTransform background = CreatePanel(frame, "Background", new Color(0.075f, 0.08f, 0.10f, 0.98f), false, null);
            Stretch(background, Vector2.zero, Vector2.zero);

            RectTransform toolbar = CreatePanel(frame, "Toolbar", new Color(0.12f, 0.13f, 0.16f, 0.99f), false, null);
            AnchorTop(toolbar, ToolbarHeight);
            CreateButton(toolbar, "SplitRight", "Split Right", 8f, 4f, 104f, 30f, delegate
            {
                workspace.SplitFocused(DockSplitDirection.Columns);
                status = "Split right.";
                Invalidate();
            }, false);
            CreateButton(toolbar, "SplitDown", "Split Down", 118f, 4f, 100f, 30f, delegate
            {
                workspace.SplitFocused(DockSplitDirection.Rows);
                status = "Split down.";
                Invalidate();
            }, false);
            CreateLabel(toolbar, "Title", "ADOFAI Workbench", 234f, 7f, 190f, 26f, 17f);

            float launchX = 430f;
            foreach (IDockablePane pane in Workbench.Panes)
            {
                if (pane == null) continue;
                float width = Mathf.Clamp(34f + pane.Title.Length * 6f, 70f, 142f);
                if (launchX + width > toolbar.rect.width - 8f) break;
                IDockablePane captured = pane;
                CreateButton(toolbar, "Open_" + pane.Id, "+ " + pane.Title, launchX, 4f, width, 30f, delegate
                {
                    OpenPane(captured);
                }, false);
                launchX += width + 4f;
            }

            RectTransform body = CreateRect(frame, "DockRoot");
            Stretch(body, new Vector2(0f, StatusHeight), new Vector2(0f, -ToolbarHeight));
            BuildNode(body, workspace.Root);

            RectTransform statusBar = CreatePanel(frame, "StatusBar", new Color(0.10f, 0.11f, 0.14f, 0.99f), false, null);
            AnchorBottom(statusBar, StatusHeight);
            CreateLabel(statusBar, "Status", status, 8f, 1f, 1000f, 20f, 13f);
        }

        private static void BuildNode(RectTransform parent, DockNode node)
        {
            DockGroup group = node as DockGroup;
            if (group != null)
            {
                BuildGroup(parent, group);
                return;
            }

            DockSplit split = node as DockSplit;
            if (split == null) return;
            float ratio = Mathf.Clamp(split.Ratio, 0.15f, 0.85f);
            RectTransform first = CreateRect(parent, "SplitFirst");
            RectTransform second = CreateRect(parent, "SplitSecond");
            if (split.Direction == DockSplitDirection.Columns)
            {
                first.anchorMin = Vector2.zero;
                first.anchorMax = new Vector2(ratio, 1f);
                first.offsetMin = Vector2.zero;
                first.offsetMax = new Vector2(-Gap * 0.5f, 0f);
                second.anchorMin = new Vector2(ratio, 0f);
                second.anchorMax = Vector2.one;
                second.offsetMin = new Vector2(Gap * 0.5f, 0f);
                second.offsetMax = Vector2.zero;
            }
            else
            {
                first.anchorMin = new Vector2(0f, 1f - ratio);
                first.anchorMax = Vector2.one;
                first.offsetMin = new Vector2(0f, Gap * 0.5f);
                first.offsetMax = Vector2.zero;
                second.anchorMin = Vector2.zero;
                second.anchorMax = new Vector2(1f, 1f - ratio);
                second.offsetMin = Vector2.zero;
                second.offsetMax = new Vector2(0f, -Gap * 0.5f);
            }
            BuildNode(first, split.First);
            BuildNode(second, split.Second);
        }

        private static void BuildGroup(RectTransform parent, DockGroup group)
        {
            bool focused = ReferenceEquals(workspace.FocusedGroup, group);
            RectTransform tabs = CreatePanel(parent, "Tabs", focused ? new Color(0.18f, 0.21f, 0.27f, 0.99f) : new Color(0.12f, 0.13f, 0.16f, 0.99f), false, null);
            AnchorTop(tabs, TabHeight);
            CreateButton(tabs, "Focus", focused ? ">" : "", 4f, 3f, 30f, 28f, delegate
            {
                workspace.Focus(group);
                status = "Focused " + group.Id + ".";
                Invalidate();
            }, focused);

            float x = 38f;
            IList<string> ids = group.PaneIds;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                IDockablePane pane = Workbench.FindPane(id);
                if (pane == null) continue;
                bool active = string.Equals(group.ActivePaneId, id, StringComparison.Ordinal);
                string capturedId = id;
                IDockablePane capturedPane = pane;
                float closeWidth = pane.CanClose ? 25f : 0f;
                float titleWidth = Mathf.Clamp(46f + pane.Title.Length * 7f, 78f, 170f);
                CreateButton(tabs, "Tab_" + i, pane.Title, x, 3f, titleWidth, 28f, delegate
                {
                    workspace.Focus(group);
                    group.Open(capturedId);
                    status = "Focused " + capturedPane.Title + ".";
                    Invalidate();
                }, active && focused);
                if (pane.CanClose)
                {
                    CreateButton(tabs, "Close_" + i, "×", x + titleWidth, 3f, closeWidth, 28f, delegate
                    {
                        group.Close(capturedId);
                        status = "Closed " + capturedPane.Title + ".";
                        Invalidate();
                    }, false);
                }
                x += titleWidth + closeWidth + 3f;
            }

            RectTransform content = CreateRect(parent, "Content");
            Stretch(content, Vector2.zero, new Vector2(0f, -TabHeight));
            IDockablePane activePane = Workbench.FindPane(group.ActivePaneId);
            if (activePane == null)
            {
                CreateLabel(content, "Empty", "No pane. Use the + buttons in the toolbar.", 12f, 12f, 460f, 28f, 16f);
                return;
            }

            foreach (KeyValuePair<DockGroup, IDockablePane> pair in mounted)
            {
                if (!ReferenceEquals(pair.Key, group) && ReferenceEquals(pair.Value, activePane))
                {
                    CreateLabel(content, "AlreadyOpen", "This pane is already open in another group.", 12f, 12f, 500f, 28f, 16f);
                    return;
                }
            }

            activePane.Mount(content);
            mounted[group] = activePane;
        }

        private static void UnmountAll()
        {
            var unique = new HashSet<IDockablePane>();
            foreach (KeyValuePair<DockGroup, IDockablePane> pair in mounted)
                if (pair.Value != null && unique.Add(pair.Value)) pair.Value.Unmount();
            mounted.Clear();
        }

        private static string BuildSignature()
        {
            var s = new System.Text.StringBuilder();
            s.Append(status).Append('|');
            foreach (DockGroup group in workspace.Groups)
            {
                s.Append(group.Id).Append(':').Append(group.ActivePaneId).Append(':');
                for (int i = 0; i < group.PaneIds.Count; i++) s.Append(group.PaneIds[i]).Append(',');
                s.Append('|');
            }
            foreach (IDockablePane pane in Workbench.Panes)
                if (pane != null) s.Append(pane.Id).Append('=').Append(pane.Title).Append('|');
            return s.ToString();
        }

        private static void Invalidate() { signature = null; }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color, bool interactive, Action callback)
        {
            RectTransform rect = CreateRect(parent, name);
            Type imageType = FindLoadedType("UnityEngine.UI.Image");
            if (imageType == null) throw new InvalidOperationException("UnityEngine.UI.Image is unavailable.");
            Component image = rect.gameObject.AddComponent(imageType);
            SetProperty(image, "color", color);
            SetProperty(image, "raycastTarget", interactive);
            if (interactive && callback != null)
            {
                Type buttonType = FindLoadedType("UnityEngine.UI.Button");
                if (buttonType == null) throw new InvalidOperationException("UnityEngine.UI.Button is unavailable.");
                Component button = rect.gameObject.AddComponent(buttonType);
                SetProperty(button, "targetGraphic", image);
                BindButton(button, callback);
            }
            return rect;
        }

        private static RectTransform CreateButton(Transform parent, string name, string text, float x, float y, float width, float height, Action callback, bool selected)
        {
            RectTransform rect = CreatePanel(parent, name, selected ? new Color(0.28f, 0.34f, 0.46f, 0.99f) : new Color(0.19f, 0.20f, 0.24f, 0.99f), true, callback);
            SetTopLeft(rect, x, y, width, height);
            CreateLabel(rect, "Text", text, 0f, 0f, width, height, 14f);
            return rect;
        }

        private static RectTransform CreateLabel(Transform parent, string name, string text, float x, float y, float width, float height, float fontSize)
        {
            GameObject label = ADOFAIEditorUiHost.CloneStockObject("findFloorPanelTitle", parent, name);
            RectTransform rect = label.transform as RectTransform;
            SetTopLeft(rect, x, y, width, height);
            SetText(label, text);
            SetFontSize(label, fontSize);
            SetRaycast(label, false);
            return rect;
        }

        private static void BindButton(Component button, Action callback)
        {
            object onClick = button.GetType().GetProperty("onClick").GetValue(button, null);
            System.Reflection.MethodInfo remove = onClick.GetType().GetMethod("RemoveAllListeners", Type.EmptyTypes);
            if (remove != null) remove.Invoke(onClick, null);
            System.Reflection.MethodInfo[] methods = onClick.GetType().GetMethods();
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "AddListener" || methods[i].GetParameters().Length != 1) continue;
                Type delegateType = methods[i].GetParameters()[0].ParameterType;
                Delegate listener = Delegate.CreateDelegate(delegateType, callback.Target, callback.Method);
                methods[i].Invoke(onClick, new object[] { listener });
                return;
            }
            throw new InvalidOperationException("Could not bind Button.onClick.");
        }

        private static Type FindLoadedType(string fullName)
        {
            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static void SetProperty(Component component, string name, object value)
        {
            System.Reflection.PropertyInfo property = component.GetType().GetProperty(name);
            if (property != null && property.CanWrite) property.SetValue(component, value, null);
        }

        private static void SetText(GameObject root, string value)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component c = components[i];
                if (c == null) continue;
                System.Reflection.PropertyInfo p = c.GetType().GetProperty("text");
                if (p != null && p.CanWrite && p.PropertyType == typeof(string)) p.SetValue(c, value, null);
            }
        }

        private static void SetFontSize(GameObject root, float value)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component c = components[i];
                if (c == null) continue;
                System.Reflection.PropertyInfo p = c.GetType().GetProperty("fontSize");
                if (p != null && p.CanWrite && p.PropertyType == typeof(float)) p.SetValue(c, value, null);
            }
        }

        private static void SetRaycast(GameObject root, bool value)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component c = components[i];
                if (c == null) continue;
                System.Reflection.PropertyInfo p = c.GetType().GetProperty("raycastTarget");
                if (p != null && p.CanWrite && p.PropertyType == typeof(bool)) p.SetValue(c, value, null);
            }
        }

        private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static void AnchorTop(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
        }

        private static void AnchorBottom(RectTransform rect, float height)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
        }
    }
}
