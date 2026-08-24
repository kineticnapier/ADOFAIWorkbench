using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace KineticNapier.ADOFAIWorkbench
{
    internal static class WinFormsWorkbenchWindowHost
    {
        private static readonly object Gate = new object();
        private static readonly Queue<Action> Pending = new Queue<Action>();
        private static Thread thread;
        private static WorkbenchForm window;

        internal static void ShowWindow()
        {
            EnsureStarted();
            Invoke(delegate
            {
                if (window == null) return;
                window.Show();
                if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
                window.Activate();
            });
        }

        internal static void HideWindow()
        {
            Invoke(delegate { if (window != null) window.HideWorkbench(); });
        }

        internal static void OpenPane(string id)
        {
            EnsureStarted();
            Invoke(delegate
            {
                if (window == null) return;
                if (!window.Visible) window.Show();
                window.OpenPane(id);
                window.Activate();
            });
        }

        internal static void NotifyRegistryChanged()
        {
            Invoke(delegate { if (window != null) window.RefreshRegistry(); });
        }

        internal static void Invoke(Action action)
        {
            if (action == null) return;
            WorkbenchForm target;
            lock (Gate)
            {
                target = window;
                if (target == null || !target.IsHandleCreated)
                {
                    Pending.Enqueue(action);
                    return;
                }
            }

            try { target.BeginInvoke(action); }
            catch (InvalidOperationException)
            {
                lock (Gate) Pending.Enqueue(action);
            }
        }

        private static void EnsureStarted()
        {
            lock (Gate)
            {
                if (thread != null) return;
                thread = new Thread(ThreadMain)
                {
                    IsBackground = true,
                    Name = "ADOFAI Workbench UI"
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
        }

        private static void ThreadMain()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            WorkbenchForm created = new WorkbenchForm();
            IntPtr ignored = created.Handle;
            Action[] pending;
            lock (Gate)
            {
                window = created;
                pending = Pending.ToArray();
                Pending.Clear();
            }

            created.Shown += delegate
            {
                for (int i = 0; i < pending.Length; i++)
                {
                    try { pending[i](); } catch { }
                }
            };
            Application.Run(created);
        }
    }

    internal sealed class WorkbenchForm : Form
    {
        private static readonly Color ChromeBack = Color.FromArgb(35, 38, 46);
        private static readonly Color TextColor = Color.FromArgb(225, 228, 235);
        private static readonly string StateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADOFAIWorkbench");
        private static readonly string LayoutPath = Path.Combine(StateDirectory, "layout.xml");
        private static readonly string WindowPath = Path.Combine(StateDirectory, "window.txt");

        private readonly ToolStrip toolbar = new ToolStrip();
        private readonly ToolStripDropDownButton panesMenu = new ToolStripDropDownButton("Panes");
        private readonly ToolStripStatusLabel status = new ToolStripStatusLabel();
        private readonly StatusStrip statusStrip = new StatusStrip();
        private readonly DockPanel dockPanel = new DockPanel();
        private readonly Dictionary<string, PaneDockContent> contents = new Dictionary<string, PaneDockContent>(StringComparer.Ordinal);
        private bool loadingLayout;

        internal WorkbenchForm()
        {
            Text = "ADOFAI Workbench";
            Width = 1100;
            Height = 720;
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ChromeBack;
            ForeColor = TextColor;
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildToolbar();
            BuildStatus();
            BuildDockPanel();
            RestoreWindowState();
            RestoreDockLayout();
            RefreshRegistry();

            FormClosing += OnFormClosing;
            ResizeEnd += delegate { SaveWindowState(); };
            Move += delegate { if (WindowState == FormWindowState.Normal) SaveWindowState(); };
        }

        internal void HideWorkbench()
        {
            SaveLayout();
            Hide();
        }

        internal void RefreshRegistry()
        {
            RebuildPanesMenu();

            foreach (PaneDockContent content in new List<PaneDockContent>(contents.Values))
                content.RefreshPaneBinding();

            if (!loadingLayout && dockPanel.Contents.Count == 0)
            {
                IDockablePane welcome = Workbench.FindPane("workbench.welcome");
                if (welcome != null) OpenPane(welcome.Id);
            }
        }

        internal void OpenPane(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            PaneDockContent existing;
            if (contents.TryGetValue(id, out existing) && !existing.IsDisposed)
            {
                existing.RefreshPaneBinding();
                existing.Show();
                existing.Activate();
                SetStatus("Focused " + existing.Text);
                return;
            }

            IDockablePane pane = Workbench.FindPane(id);
            if (pane == null) return;

            PaneDockContent content = new PaneDockContent(this, id);
            contents[id] = content;
            content.Show(dockPanel, DockState.Document);
            content.Activate();
            SetStatus("Opened " + pane.Title);
            SaveLayout();
        }

        internal void NotifyContentClosed(string id, PaneDockContent content)
        {
            PaneDockContent current;
            if (contents.TryGetValue(id, out current) && ReferenceEquals(current, content))
                contents.Remove(id);
            if (!loadingLayout) SaveLayout();
        }

        internal void NotifyContentStateChanged()
        {
            if (!loadingLayout) SaveLayout();
        }

        private void BuildToolbar()
        {
            toolbar.Dock = DockStyle.Top;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.BackColor = ChromeBack;
            toolbar.ForeColor = TextColor;
            toolbar.RenderMode = ToolStripRenderMode.System;

            toolbar.Items.Add(panesMenu);
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(MakeToolbarButton("Save Layout", delegate { SaveLayout(); SetStatus("Layout saved"); }));
            toolbar.Items.Add(MakeToolbarButton("Reset Layout", ResetLayout));
            Controls.Add(toolbar);
        }

        private void BuildStatus()
        {
            statusStrip.Dock = DockStyle.Bottom;
            statusStrip.BackColor = ChromeBack;
            statusStrip.ForeColor = TextColor;
            status.Text = "Workbench ready";
            statusStrip.Items.Add(status);
            Controls.Add(statusStrip);
        }

        private void BuildDockPanel()
        {
            dockPanel.Dock = DockStyle.Fill;
            dockPanel.DocumentStyle = DocumentStyle.DockingWindow;
            dockPanel.Theme = new VS2015DarkTheme();
            dockPanel.AllowEndUserDocking = true;
            dockPanel.ShowDocumentIcon = false;
            Controls.Add(dockPanel);
            dockPanel.BringToFront();
            toolbar.BringToFront();
            statusStrip.BringToFront();
        }

        private void RebuildPanesMenu()
        {
            panesMenu.DropDownItems.Clear();
            IList<IDockablePane> panes = Workbench.GetPanesSnapshot();
            for (int i = 0; i < panes.Count; i++)
            {
                IDockablePane pane = panes[i];
                ToolStripMenuItem item = new ToolStripMenuItem(pane.Title);
                string id = pane.Id;
                item.Click += delegate { OpenPane(id); };
                PaneDockContent open;
                item.Checked = contents.TryGetValue(id, out open) && open != null && !open.IsDisposed;
                panesMenu.DropDownItems.Add(item);
            }
        }

        private void RestoreDockLayout()
        {
            if (!File.Exists(LayoutPath)) return;

            loadingLayout = true;
            try
            {
                dockPanel.LoadFromXml(LayoutPath, DeserializeDockContent);
            }
            catch (Exception ex)
            {
                SetStatus("Layout reset after load error: " + ex.Message);
                try { File.Delete(LayoutPath); } catch { }
                CloseAllDockContents();
            }
            finally
            {
                loadingLayout = false;
            }
        }

        private IDockContent DeserializeDockContent(string persistString)
        {
            const string prefix = "pane:";
            if (string.IsNullOrWhiteSpace(persistString) || !persistString.StartsWith(prefix, StringComparison.Ordinal))
                return null;

            string id = persistString.Substring(prefix.Length);
            PaneDockContent existing;
            if (contents.TryGetValue(id, out existing) && existing != null && !existing.IsDisposed)
                return existing;

            PaneDockContent content = new PaneDockContent(this, id);
            contents[id] = content;
            return content;
        }

        private void SaveLayout()
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                dockPanel.SaveAsXml(LayoutPath);
                SaveWindowState();
                RebuildPanesMenu();
            }
            catch (Exception ex)
            {
                SetStatus("Layout save failed: " + ex.Message);
            }
        }

        private void ResetLayout()
        {
            loadingLayout = true;
            try
            {
                CloseAllDockContents();
                try { if (File.Exists(LayoutPath)) File.Delete(LayoutPath); } catch { }
            }
            finally
            {
                loadingLayout = false;
            }

            IDockablePane welcome = Workbench.FindPane("workbench.welcome");
            if (welcome != null) OpenPane(welcome.Id);
            RebuildPanesMenu();
            SetStatus("Layout reset");
        }

        private void CloseAllDockContents()
        {
            IDockContent[] open = new IDockContent[dockPanel.Contents.Count];
            for (int i = 0; i < dockPanel.Contents.Count; i++) open[i] = dockPanel.Contents[i];
            for (int i = 0; i < open.Length; i++)
            {
                PaneDockContent content = open[i] as PaneDockContent;
                if (content == null) continue;
                content.ForceClose = true;
                try
                {
                    content.DockHandler.DockPanel = null;
                    content.Close();
                }
                catch { }
            }
            contents.Clear();

            foreach (FloatWindow floatWindow in new List<FloatWindow>(dockPanel.FloatWindows))
            {
                try { floatWindow.Dispose(); } catch { }
            }
        }

        private void RestoreWindowState()
        {
            try
            {
                if (!File.Exists(WindowPath)) return;
                string[] parts = File.ReadAllText(WindowPath).Split('|');
                if (parts.Length < 5) return;

                int x, y, width, height;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)) return;
                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)) return;

                Rectangle saved = new Rectangle(x, y, Math.Max(MinimumSize.Width, width), Math.Max(MinimumSize.Height, height));
                bool visible = false;
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(saved))
                    {
                        visible = true;
                        break;
                    }
                }
                if (!visible) return;

                StartPosition = FormStartPosition.Manual;
                Bounds = saved;
                if (string.Equals(parts[4], "max", StringComparison.OrdinalIgnoreCase))
                    WindowState = FormWindowState.Maximized;
            }
            catch { }
        }

        private void SaveWindowState()
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                string state = WindowState == FormWindowState.Maximized ? "max" : "normal";
                File.WriteAllText(
                    WindowPath,
                    bounds.X.ToString(CultureInfo.InvariantCulture) + "|" +
                    bounds.Y.ToString(CultureInfo.InvariantCulture) + "|" +
                    bounds.Width.ToString(CultureInfo.InvariantCulture) + "|" +
                    bounds.Height.ToString(CultureInfo.InvariantCulture) + "|" + state);
            }
            catch { }
        }

        private ToolStripButton MakeToolbarButton(string text, Action action)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.ForeColor = TextColor;
            button.Click += delegate { if (action != null) action(); };
            return button;
        }

        private void SetStatus(string text)
        {
            status.Text = text ?? string.Empty;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            SaveLayout();
            Hide();
        }
    }

    internal sealed class PaneDockContent : DockContent
    {
        private readonly WorkbenchForm owner;
        private readonly string paneId;
        private IDockablePane boundPane;
        private Control boundView;

        internal bool ForceClose;

        internal PaneDockContent(WorkbenchForm owner, string paneId)
        {
            this.owner = owner;
            this.paneId = paneId;
            HideOnClose = false;
            DockAreas = DockAreas.Document | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom | DockAreas.Float;
            BackColor = Color.FromArgb(19, 21, 26);
            ForeColor = Color.FromArgb(225, 228, 235);
            AutoScaleMode = AutoScaleMode.Dpi;
            DockStateChanged += delegate { owner.NotifyContentStateChanged(); };
            RefreshPaneBinding();
        }

        internal void RefreshPaneBinding()
        {
            IDockablePane pane = Workbench.FindPane(paneId);
            if (ReferenceEquals(pane, boundPane) && boundView != null && !boundView.IsDisposed)
                return;

            if (boundPane != null)
            {
                try { boundPane.OnClosed(); } catch { }
            }

            if (boundView != null)
            {
                Controls.Remove(boundView);
                try { boundView.Dispose(); } catch { }
                boundView = null;
            }

            boundPane = pane;
            string title = pane != null ? pane.Title : paneId;
            Text = title;
            TabText = title;
            CloseButton = pane == null || pane.CanClose;
            CloseButtonVisible = pane == null || pane.CanClose;

            if (pane == null)
            {
                Label waiting = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = "Waiting for pane provider: " + paneId,
                    ForeColor = Color.FromArgb(170, 175, 185),
                    BackColor = BackColor
                };
                boundView = waiting;
                Controls.Add(waiting);
                return;
            }

            try
            {
                boundView = pane.CreateView();
                if (boundView == null) throw new InvalidOperationException("CreateView returned null.");
                boundView.Dock = DockStyle.Fill;
                Controls.Add(boundView);
                pane.OnOpened();
            }
            catch (Exception ex)
            {
                TextBox error = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    BorderStyle = BorderStyle.None,
                    BackColor = BackColor,
                    ForeColor = ForeColor,
                    Text = "Pane failed to create:" + Environment.NewLine + ex
                };
                boundView = error;
                Controls.Add(error);
            }
        }

        protected override string GetPersistString()
        {
            return "pane:" + paneId;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ForceClose && boundPane != null && !boundPane.CanClose)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (boundPane != null)
            {
                try { boundPane.OnClosed(); } catch { }
            }
            boundPane = null;
            boundView = null;
            owner.NotifyContentClosed(paneId, this);
            base.OnFormClosed(e);
        }
    }
}
