using System;
using System.Collections.Generic;
using UnityEngine;

namespace KineticNapier.ADOFAIWorkbench
{
    public interface IDockablePane
    {
        string Id { get; }
        string Title { get; }
        bool CanClose { get; }
        void Mount(RectTransform parent);
        void Unmount();
    }

    public interface IDockablePaneProvider
    {
        IEnumerable<IDockablePane> CreatePanes();
    }

    public static class Workbench
    {
        private static readonly List<IDockablePaneProvider> providers = new List<IDockablePaneProvider>();
        private static readonly Dictionary<string, IDockablePane> panes = new Dictionary<string, IDockablePane>(StringComparer.Ordinal);

        public static event Action RegistryChanged;

        public static IEnumerable<IDockablePane> Panes { get { return panes.Values; } }

        public static void RegisterPaneProvider(IDockablePaneProvider provider)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            if (providers.Contains(provider)) return;
            providers.Add(provider);
            RefreshProvider(provider);
        }

        public static void UnregisterPaneProvider(IDockablePaneProvider provider)
        {
            if (provider == null) return;
            if (!providers.Remove(provider)) return;
            RebuildRegistry();
        }

        public static IDockablePane FindPane(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            IDockablePane pane;
            return panes.TryGetValue(id, out pane) ? pane : null;
        }

        public static bool OpenPane(string id)
        {
            IDockablePane pane = FindPane(id);
            if (pane == null) return false;
            NativeWorkbenchShell.OpenPane(pane);
            return true;
        }

        internal static void RefreshAll()
        {
            RebuildRegistry();
        }

        private static void RefreshProvider(IDockablePaneProvider provider)
        {
            IEnumerable<IDockablePane> created = provider.CreatePanes();
            if (created != null)
            {
                foreach (IDockablePane pane in created)
                {
                    if (pane == null || string.IsNullOrWhiteSpace(pane.Id)) continue;
                    panes[pane.Id] = pane;
                }
            }
            RaiseRegistryChanged();
        }

        private static void RebuildRegistry()
        {
            panes.Clear();
            for (int i = 0; i < providers.Count; i++)
            {
                IEnumerable<IDockablePane> created = providers[i].CreatePanes();
                if (created == null) continue;
                foreach (IDockablePane pane in created)
                {
                    if (pane == null || string.IsNullOrWhiteSpace(pane.Id)) continue;
                    panes[pane.Id] = pane;
                }
            }
            RaiseRegistryChanged();
        }

        private static void RaiseRegistryChanged()
        {
            Action handler = RegistryChanged;
            if (handler != null) handler();
        }
    }
}
