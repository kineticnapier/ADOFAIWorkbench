using System;
using System.Collections.Generic;
using UnityEngine;

namespace KineticNapier.ADOFAIWorkbench
{
    internal sealed class StockEditorPaneProvider : IDockablePaneProvider
    {
        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return new StockEditorPane("adofai.settings", "ADOFAI Settings", "settingsPanel");
            yield return new StockEditorPane("adofai.events", "ADOFAI Events", "levelEventsPanel");
            yield return new StockEditorPane("adofai.file", "ADOFAI File", "fileActionsPanel", "fileActions");
        }
    }

    internal sealed class StockEditorPane : IDockablePane
    {
        private readonly string id;
        private readonly string title;
        private readonly string[] memberNames;
        private GameObject target;
        private Transform originalParent;
        private int originalSiblingIndex;
        private bool originalActive;
        private RectState originalRect;

        internal StockEditorPane(string id, string title, params string[] memberNames)
        {
            this.id = id;
            this.title = title;
            this.memberNames = memberNames ?? new string[0];
        }

        public string Id { get { return id; } }
        public string Title { get { return title; } }
        public bool CanClose { get { return true; } }

        public void Mount(RectTransform parent)
        {
            Unmount();
            scnEditor editor = ADOBase.editor;
            if (editor == null || parent == null) return;

            target = ResolveAny(editor, memberNames);
            if (target == null) return;

            RectTransform rect = target.transform as RectTransform;
            if (rect == null)
            {
                target = null;
                return;
            }

            originalParent = rect.parent;
            originalSiblingIndex = rect.GetSiblingIndex();
            originalActive = target.activeSelf;
            originalRect = RectState.Capture(rect);
            Vector2 nativeSize = new Vector2(Mathf.Max(1f, rect.rect.width), Mathf.Max(1f, rect.rect.height));

            StockEditorOverride.Claim(target);
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = nativeSize;
            rect.localScale = Vector3.one;
            target.SetActive(true);
        }

        public void Unmount()
        {
            if (target == null) return;
            RectTransform rect = target.transform as RectTransform;
            if (rect != null && originalParent != null)
            {
                rect.SetParent(originalParent, false);
                rect.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, Math.Max(0, originalParent.childCount - 1)));
                originalRect.Apply(rect);
            }
            target.SetActive(originalActive);
            StockEditorOverride.Release(target);
            target = null;
            originalParent = null;
        }

        private static GameObject ResolveAny(scnEditor editor, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                GameObject go = Resolve(editor, names[i]);
                if (go != null) return go;
            }
            return null;
        }

        private static GameObject Resolve(scnEditor editor, string name)
        {
            Type type = editor.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            object value = null;
            System.Reflection.FieldInfo field = type.GetField(name, flags);
            if (field != null) value = field.GetValue(editor);
            else
            {
                System.Reflection.PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.CanRead) value = property.GetValue(editor, null);
            }

            GameObject go = value as GameObject;
            if (go != null) return go;
            Component component = value as Component;
            return component != null ? component.gameObject : null;
        }

        private struct RectState
        {
            internal Vector2 AnchorMin;
            internal Vector2 AnchorMax;
            internal Vector2 Pivot;
            internal Vector2 AnchoredPosition;
            internal Vector2 SizeDelta;
            internal Vector2 OffsetMin;
            internal Vector2 OffsetMax;
            internal Vector3 LocalScale;

            internal static RectState Capture(RectTransform rect)
            {
                return new RectState
                {
                    AnchorMin = rect.anchorMin,
                    AnchorMax = rect.anchorMax,
                    Pivot = rect.pivot,
                    AnchoredPosition = rect.anchoredPosition,
                    SizeDelta = rect.sizeDelta,
                    OffsetMin = rect.offsetMin,
                    OffsetMax = rect.offsetMax,
                    LocalScale = rect.localScale
                };
            }

            internal void Apply(RectTransform rect)
            {
                rect.anchorMin = AnchorMin;
                rect.anchorMax = AnchorMax;
                rect.pivot = Pivot;
                rect.anchoredPosition = AnchoredPosition;
                rect.sizeDelta = SizeDelta;
                rect.offsetMin = OffsetMin;
                rect.offsetMax = OffsetMax;
                rect.localScale = LocalScale;
            }
        }
    }
}
