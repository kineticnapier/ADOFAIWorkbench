using System;
using System.Collections.Generic;
using UnityEngine;

namespace KineticNapier.ADOFAIWorkbench
{
    internal static class StockEditorOverride
    {
        private const int MissingResolveRetryFrames = 120;

        private static scnEditor editor;
        private static readonly Dictionary<GameObject, bool> originalStates = new Dictionary<GameObject, bool>();
        private static readonly HashSet<GameObject> claimed = new HashSet<GameObject>();
        private static readonly Dictionary<string, GameObject> resolved = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private static int nextMissingResolveFrame;

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
                ResolveAll(activeEditor);
            }
            else if (Time.frameCount >= nextMissingResolveFrame)
            {
                RefreshMissing(activeEditor);
            }

            for (int i = 0; i < MemberNames.Length; i++)
            {
                GameObject go;
                if (!resolved.TryGetValue(MemberNames[i], out go) || go == null || IsClaimed(go)) continue;
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
            resolved.Clear();
            nextMissingResolveFrame = 0;
            editor = null;
        }

        private static void ResolveAll(scnEditor target)
        {
            resolved.Clear();
            for (int i = 0; i < MemberNames.Length; i++)
                resolved[MemberNames[i]] = Resolve(target, MemberNames[i]);
            nextMissingResolveFrame = Time.frameCount + MissingResolveRetryFrames;
        }

        private static void RefreshMissing(scnEditor target)
        {
            for (int i = 0; i < MemberNames.Length; i++)
            {
                string name = MemberNames[i];
                GameObject go;
                if (resolved.TryGetValue(name, out go) && go != null) continue;
                resolved[name] = Resolve(target, name);
            }
            nextMissingResolveFrame = Time.frameCount + MissingResolveRetryFrames;
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
            if (target == null || string.IsNullOrEmpty(name)) return null;

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
            if (component != null) return component.gameObject;

            Transform found = FindDescendantByName(target.transform, name);
            return found != null ? found.gameObject : null;
        }

        private static Transform FindDescendantByName(Transform root, string name)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null) continue;
                if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
                Transform nested = FindDescendantByName(child, name);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
