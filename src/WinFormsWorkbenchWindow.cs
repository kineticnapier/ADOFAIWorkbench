using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

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
            Invoke(delegate { if (window != null) window.Hide(); });
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
                thread = new Thread(ThreadMain);
                thread.IsBackground = true;
                thread.Name = "ADOFAI Workbench UI";
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
        private static readonly Color WindowBack = Color.FromArgb(24, 26, 31);
        private static readonly Color ChromeBack = Color.FromArgb(35, 38, 46);
        private static readonly Color PaneBack = Color.FromArgb(19, 21, 26);
        private static readonly Color TextColor = Color.FromArgb(225, 228, 235);

        private readonly FlowLayoutPanel toolbar = new FlowLayoutPanel();
        private readonly FlowLayoutPanel launcherPanel = new FlowLayoutPanel();
        private readonly SplitContainer split = new SplitContainer();
        private readonly TabControl leftTabs = new TabControl();
        private readonly TabControl rightTabs = new TabControl();
        private readonly StatusStrip statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel status = new ToolStripStatusLabel();
        private readonly Dictionary<string, OpenPaneState> openPanes = new Dictionary<string, OpenPaneState>(StringComparer.Ordinal);
        private TabControl focusedTabs;

        internal WorkbenchForm()
        {
            Text = "ADOFAI Workbench";
            Width = 1100;
            Height = 720;
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = WindowBack;
            ForeColor = TextColor;

            FormClosing += OnFormClosing;
            BuildToolbar();
            BuildStatus();
            BuildContent();
            focusedTabs = leftTabs;
            RefreshRegistry();
        }

        internal void RefreshRegistry()
        {
            launcherPanel.Controls.Clear();
            IList<IDockablePane> panes = Workbench.GetPanesSnapshot();
            for (int i = 0; i < panes.Count; i++)
            {
                IDockablePane pane = panes[i];
                Button button = MakeButton("+ " + pane.Title);
                string id = pane.Id;
                button.Click += delegate { OpenPane(id); };
                launcherPanel.Controls.Add(button);
            }

            var stale = new List<string>();
            foreach (KeyValuePair<string, OpenPaneState> pair in openPanes)
                if (Workbench.FindPane(pair.Key) == null) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) ClosePane(stale[i], true);
        }

        internal void OpenPane(string id)
        {
            IDockablePane pane = Workbench.FindPane(id);
            if (pane == null) return;

            OpenPaneState existing;
            if (openPanes.TryGetValue(id, out existing))
            {
                existing.Owner.SelectedTab = existing.Page;
                focusedTabs = existing.Owner;
                SetStatus("Focused " + pane.Title);
                return;
            }

            TabControl owner = focusedTabs ?? leftTabs;
            Control view;
            try { view = pane.CreateView(); }
            catch (Exception ex)
            {
                view = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    Dock = DockStyle.Fill,
                    BackColor = PaneBack,
                    ForeColor = TextColor,
                    BorderStyle = BorderStyle.None,
                    Text = "Pane failed to create:" + Environment.NewLine + ex
                };
            }

            view.Dock = DockStyle.Fill;
            TabPage page = new TabPage(pane.Title)
            {
                BackColor = PaneBack,
                ForeColor = TextColor,
                Tag = pane.Id,
                Padding = new Padding(0)
            };
            page.Controls.Add(view);
            owner.TabPages.Add(page);
            owner.SelectedTab = page;
            openPanes[id] = new OpenPaneState(pane, page, owner);
            try { pane.OnOpened(); } catch { }
            SetStatus("Opened " + pane.Title);
        }

        private void ClosePane(string id, bool force)
        {
            OpenPaneState state;
            if (!openPanes.TryGetValue(id, out state)) return;
            if (!force && !state.Pane.CanClose) return;
            state.Owner.TabPages.Remove(state.Page);
            openPanes.Remove(id);
            try { state.Pane.OnClosed(); } catch { }
            state.Page.Dispose();
            SetStatus("Closed " + state.Pane.Title);
            if (rightTabs.TabCount == 0)
            {
                split.Panel2Collapsed = true;
                focusedTabs = leftTabs;
            }
        }

        private void BuildToolbar()
        {
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 38;
            toolbar.WrapContents = false;
            toolbar.AutoScroll = true;
            toolbar.BackColor = ChromeBack;
            toolbar.Padding = new Padding(4);

            Button splitRight = MakeButton("Split Right");
            splitRight.Click += delegate
            {
                split.Panel2Collapsed = false;
                focusedTabs = rightTabs;
                SetStatus("Focused right group");
            };
            toolbar.Controls.Add(splitRight);

            Button left = MakeButton("Left");
            left.Click += delegate { focusedTabs = leftTabs; SetStatus("Focused left group"); };
            toolbar.Controls.Add(left);

            Button right = MakeButton("Right");
            right.Click += delegate
            {
                split.Panel2Collapsed = false;
                focusedTabs = rightTabs;
                SetStatus("Focused right group");
            };
            toolbar.Controls.Add(right);

            launcherPanel.AutoSize = true;
            launcherPanel.WrapContents = false;
            launcherPanel.BackColor = ChromeBack;
            launcherPanel.Margin = new Padding(10, 0, 0, 0);
            toolbar.Controls.Add(launcherPanel);
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

        private void BuildContent()
        {
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterWidth = 5;
            split.Panel2Collapsed = true;
            split.BackColor = ChromeBack;

            ConfigureTabs(leftTabs);
            ConfigureTabs(rightTabs);
            leftTabs.Dock = DockStyle.Fill;
            rightTabs.Dock = DockStyle.Fill;
            leftTabs.MouseDown += delegate { focusedTabs = leftTabs; };
            rightTabs.MouseDown += delegate { focusedTabs = rightTabs; };
            leftTabs.DrawItem += DrawTab;
            rightTabs.DrawItem += DrawTab;
            leftTabs.MouseDown += TabMouseDown;
            rightTabs.MouseDown += TabMouseDown;

            split.Panel1.Controls.Add(leftTabs);
            split.Panel2.Controls.Add(rightTabs);
            Controls.Add(split);
            split.BringToFront();
            toolbar.BringToFront();
            statusStrip.BringToFront();
        }

        private static void ConfigureTabs(TabControl tabs)
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(150, 28);
            tabs.BackColor = PaneBack;
            tabs.ForeColor = TextColor;
        }

        private void DrawTab(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            if (e.Index < 0 || e.Index >= tabs.TabCount) return;
            TabPage page = tabs.TabPages[e.Index];
            string id = page.Tag as string;
            IDockablePane pane = Workbench.FindPane(id);
            Rectangle rect = e.Bounds;
            using (Brush background = new SolidBrush(e.Index == tabs.SelectedIndex ? Color.FromArgb(55, 63, 78) : ChromeBack))
                e.Graphics.FillRectangle(background, rect);
            TextRenderer.DrawText(e.Graphics, page.Text, Font, new Rectangle(rect.X + 8, rect.Y + 5, rect.Width - 30, rect.Height - 6), TextColor, TextFormatFlags.EndEllipsis);
            if (pane != null && pane.CanClose)
                TextRenderer.DrawText(e.Graphics, "×", Font, new Rectangle(rect.Right - 22, rect.Y + 4, 18, rect.Height - 6), TextColor, TextFormatFlags.HorizontalCenter);
        }

        private void TabMouseDown(object sender, MouseEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            focusedTabs = tabs;
            for (int i = 0; i < tabs.TabCount; i++)
            {
                Rectangle rect = tabs.GetTabRect(i);
                if (!rect.Contains(e.Location)) continue;
                string id = tabs.TabPages[i].Tag as string;
                IDockablePane pane = Workbench.FindPane(id);
                if (pane != null && pane.CanClose && e.X >= rect.Right - 24)
                    ClosePane(id, false);
                break;
            }
        }

        private static Button MakeButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                Height = 28,
                Margin = new Padding(2, 1, 2, 1),
                Padding = new Padding(6, 0, 6, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 54, 64),
                ForeColor = TextColor
            };
        }

        private void SetStatus(string text)
        {
            status.Text = text ?? "";
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private sealed class OpenPaneState
        {
            internal readonly IDockablePane Pane;
            internal readonly TabPage Page;
            internal readonly TabControl Owner;

            internal OpenPaneState(IDockablePane pane, TabPage page, TabControl owner)
            {
                Pane = pane;
                Page = page;
                Owner = owner;
            }
        }
    }
}
