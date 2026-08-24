using System;
using System.Collections.Generic;
using UnityEngine;

namespace KineticNapier.ADOFAIWorkbench
{
    internal static class StockEditorOverride
    {
        private static scnEditor editor;
        private static readonly Dictionary<GameObject, bool> originalStates = new Dictionary<GameObject, bool>();
        private static readonly HashSet<GameObject> claimed = new HashSet<GameObject>();
        private static readonly string[] MemberNames =
        {
            "settingsPanel",
            "levelEventsPanel",
            "inspectorTabs",
            "inspectorPanels",
            "levelStringPanel",
            "findFloorPanel",
            "bottomPanel",
            "fileActionsPanel",
            "fileActions",
            "filePanel",
            "eventTabs"
        };

        internal static void Claim(GameObject go)
        {
            if (go == null) return;
            claimed.Add(go);

            // Apply() may have hidden this object or one of its descendants earlier in
            // the same frame, before the pane had a chance to claim the subtree.
            // Restore those saved states immediately when ownership moves to a pane.
            var restore = new List<GameObject>();
            foreach (KeyValuePair<GameObject, bool> pair in originalStates)
            {
                if (pair.Key == null) continue;
                if (!IsSameOrChildOf(pair.Key, go)) continue;
                pair.Key.SetActive(pair.Value);
                restore.Add(pair.Key);
            }
            for (int i = 0; i < restore.Count; i++) originalStates.Remove(restore[i]);
        }

        internal static void Release(GameObject go)
        {
            if (go != null) claimed.Remove(go);
        }

        internal static void Apply(scnEditor activeEditor)
        {
            if (activeEditor == null) return;
            if (!ReferenceEquals(editor, activeEditor))
            {
                Restore();
                editor = activeEditor;
            }

            for (int i = 0; i < MemberNames.Length; i++)
            {
                GameObject go = Resolve(activeEditor, MemberNames[i]);
                if (go == null || IsClaimed(go)) continue;
                if (!originalStates.ContainsKey(go)) originalStates.Add(go, go.activeSelf);
                if (go.activeSelf) go.SetActive(false);
            }
        }

        internal static void Restore()
        {
            foreach (KeyValuePair<GameObject, bool> pair in originalStates)
                if (pair.Key != null && !IsClaimed(pair.Key)) pair.Key.SetActive(pair.Value);
            originalStates.Clear();
            claimed.Clear();
            editor = null;
        }

        private static bool IsClaimed(GameObject go)
        {
            if (go == null) return false;
            foreach (GameObject owner in claimed)
                if (owner != null && IsSameOrChildOf(go, owner)) return true;
            return false;
        }

        private static bool IsSameOrChildOf(GameObject candidate, GameObject owner)
        {
            if (candidate == null || owner == null) return false;
            if (ReferenceEquals(candidate, owner)) return true;
            Transform candidateTransform = candidate.transform;
            Transform ownerTransform = owner.transform;
            return candidateTransform != null && ownerTransform != null && candidateTransform.IsChildOf(ownerTransform);
        }

        private static GameObject Resolve(scnEditor target, string name)
        {
            Type type = target.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            object value = null;
            System.Reflection.FieldInfo field = type.GetField(name, flags);
            if (field != null) value = field.GetValue(target);
            else
            {
                System.Reflection.PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.CanRead) value = property.GetValue(target, null);
            }

            GameObject go = value as GameObject;
            if (go != null) return go;
            Component component = value as Component;
            return component != null ? component.gameObject : null;
        }
    }
}
