using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KineticNapier.ADOFAIWorkbench
{
    public interface IDockablePane
    {
        string Id { get; }
        string Title { get; }
        bool CanClose { get; }
        WorkbenchPaneView BuildView();
        void HandleAction(string actionId, string argument);
    }

    public interface IDockablePaneProvider
    {
        IEnumerable<IDockablePane> CreatePanes();
    }

    public sealed class WorkbenchPaneView
    {
        private readonly List<string> lines = new List<string>();

        public WorkbenchPaneView Text(string text, float size, bool bold)
        {
            lines.Add("T\t" + Encode(text) + "\t" + size.ToString(CultureInfo.InvariantCulture) + "\t" + (bold ? "1" : "0"));
            return this;
        }

        public WorkbenchPaneView Button(string label, string actionId, string argument, bool selected)
        {
            lines.Add("B\t" + Encode(label) + "\t" + Encode(actionId) + "\t" + Encode(argument) + "\t" + (selected ? "1" : "0"));
            return this;
        }

        public WorkbenchPaneView BeginRow()
        {
            lines.Add("R+");
            return this;
        }

        public WorkbenchPaneView EndRow()
        {
            lines.Add("R-");
            return this;
        }

        public WorkbenchPaneView Spacer(int height)
        {
            lines.Add("S\t" + Math.Max(0, height).ToString(CultureInfo.InvariantCulture));
            return this;
        }

        internal string Serialize()
        {
            return string.Join("\n", lines.ToArray());
        }

        internal static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        internal static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
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
            ExternalWorkbenchHost.RegistryChanged();
        }

        public static void UnregisterPaneProvider(IDockablePaneProvider provider)
        {
            if (provider == null) return;
            lock (Gate)
            {
                if (!Providers.Remove(provider)) return;
                RebuildRegistryLocked();
            }
            ExternalWorkbenchHost.RegistryChanged();
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
            ExternalWorkbenchHost.OpenPane(id);
            return true;
        }

        public static void PublishPane(string id)
        {
            IDockablePane pane = FindPane(id);
            if (pane != null) ExternalWorkbenchHost.PublishPane(pane);
        }

        public static void ShowWindow()
        {
            ExternalWorkbenchHost.ShowWindow();
        }

        public static void HideWindow()
        {
            ExternalWorkbenchHost.HideWindow();
        }

        public static void RunOnUnityThread(Action action)
        {
            if (action != null) UnityActions.Enqueue(action);
        }

        internal static void DispatchAction(string paneId, string actionId, string argument)
        {
            RunOnUnityThread(delegate
            {
                IDockablePane pane = FindPane(paneId);
                if (pane == null) return;
                try { pane.HandleAction(actionId ?? string.Empty, argument ?? string.Empty); }
                catch (Exception ex) { Main.LogError("Pane action failed: " + paneId + "/" + actionId, ex); }
            });
        }

        internal static void DrainUnityActions(int maxActions)
        {
            int count = 0;
            Action action;
            while (count < maxActions && UnityActions.TryDequeue(out action))
            {
                count++;
                try { action(); }
                catch (Exception ex) { Main.LogError("Unity action failed", ex); }
            }
        }

        internal static IList<IDockablePane> GetPanesSnapshot()
        {
            lock (Gate)
                return PanesById.Values.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
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
