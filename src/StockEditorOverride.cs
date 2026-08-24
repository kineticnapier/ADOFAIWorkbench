using System;
using System.Collections.Generic;
using UnityEngine;

namespace KineticNapier.ADOFAIWorkbench
{
    internal static class StockEditorOverride
    {
        private static scnEditor editor;
        private static readonly Dictionary<GameObject, bool> originalStates = new Dictionary<GameObject, bool>();
        private static readonly string[] MemberNames =
        {
            "settingsPanel",
            "levelEventsPanel",
            "inspectorTabs",
            "inspectorPanels",
            "levelStringPanel",
            "findFloorPanel",
            "bottomPanel",
            "fileActions",
            "filePanel",
            "eventTabs"
        };

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
                if (go == null) continue;
                if (!originalStates.ContainsKey(go)) originalStates.Add(go, go.activeSelf);
                if (go.activeSelf) go.SetActive(false);
            }
        }

        internal static void Restore()
        {
            foreach (KeyValuePair<GameObject, bool> pair in originalStates)
                if (pair.Key != null) pair.Key.SetActive(pair.Value);
            originalStates.Clear();
            editor = null;
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
