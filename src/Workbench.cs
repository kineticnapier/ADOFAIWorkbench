using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace KineticNapier.ADOFAIWorkbench
{
    public interface IDockablePane
    {
        string Id { get; }
        string Title { get; }
        bool CanClose { get; }
        Control CreateView();
        void OnOpened();
        void OnClosed();
    }

    public interface IDockablePaneProvider
    {
        IEnumerable<IDockablePane> CreatePanes();
    }

    public static class Workbench
    {
        private static readonly object Gate = new object();
        private static readonly List<IDockablePaneProvider> Providers = new List<IDockablePaneProvider>();
        private static readonly Dictionary<string, IDockablePane> PanesById = new Dictionary<string, IDockablePane>(StringComparer.Ordinal);
        private static readonly ConcurrentQueue<Action> UnityActions = new ConcurrentQueue<Action>();

        public static IEnumerable<IDockablePane> Panes
        {
            get { return GetPanesSnapshot(); }
        }

        public static void RegisterPaneProvider(IDockablePaneProvider provider)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            lock (Gate)
            {
                if (Providers.Contains(provider)) return;
                Providers.Add(provider);
                AddProviderPanes(provider);
            }
            WinFormsWorkbenchWindowHost.NotifyRegistryChanged();
        }

        public static void UnregisterPaneProvider(IDockablePaneProvider provider)
        {
            if (provider == null) return;
            lock (Gate)
            {
                if (!Providers.Remove(provider)) return;
                RebuildRegistryLocked();
            }
            WinFormsWorkbenchWindowHost.NotifyRegistryChanged();
        }

        public static IDockablePane FindPane(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (Gate)
            {
                IDockablePane pane;
                return PanesById.TryGetValue(id, out pane) ? pane : null;
            }
        }

        public static bool OpenPane(string id)
        {
            if (FindPane(id) == null) return false;
            WinFormsWorkbenchWindowHost.OpenPane(id);
            return true;
        }

        public static void ShowWindow()
        {
            WinFormsWorkbenchWindowHost.ShowWindow();
        }

        public static void HideWindow()
        {
            WinFormsWorkbenchWindowHost.HideWindow();
        }

        public static void RunOnUnityThread(Action action)
        {
            if (action != null) UnityActions.Enqueue(action);
        }

        public static void RunOnUiThread(Action action)
        {
            WinFormsWorkbenchWindowHost.Invoke(action);
        }

        internal static void DrainUnityActions(int maxActions)
        {
            int count = 0;
            Action action;
            while (count < maxActions && UnityActions.TryDequeue(out action))
            {
                count++;
                try { action(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }

        internal static IList<IDockablePane> GetPanesSnapshot()
        {
            lock (Gate)
                return PanesById.Values.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static void RefreshAll()
        {
            lock (Gate) RebuildRegistryLocked();
            WinFormsWorkbenchWindowHost.NotifyRegistryChanged();
        }

        private static void AddProviderPanes(IDockablePaneProvider provider)
        {
            IEnumerable<IDockablePane> panes = provider.CreatePanes();
            if (panes == null) return;
            foreach (IDockablePane pane in panes)
            {
                if (pane == null || string.IsNullOrWhiteSpace(pane.Id)) continue;
                PanesById[pane.Id] = pane;
            }
        }

        private static void RebuildRegistryLocked()
        {
            PanesById.Clear();
            for (int i = 0; i < Providers.Count; i++) AddProviderPanes(Providers[i]);
        }
    }
}
