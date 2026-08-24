using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace KineticNapier.ADOFAIWorkbench.Host
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args == null || args.Length < 1 || string.IsNullOrWhiteSpace(args[0])) return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (HostConnection connection = new HostConnection(args[0]))
            using (HostForm form = new HostForm(connection))
            {
                // Do not connect to the pipe before the WinForms message loop starts.
                // If IPC is slow or broken, the Workbench window must still appear and
                // remain responsive.
                form.Shown += delegate
                {
                    form.EnsureVisibleAndForeground();
                    connection.Start(form.ReceiveMessage);
                };

                Application.Run(form);
            }
        }
    }

    internal sealed class HostConnection : IDisposable
    {
        private readonly string pipeName;
        private readonly object writeGate = new object();
        private readonly Queue<string> pending = new Queue<string>();
        private NamedPipeClientStream pipe;
        private StreamReader reader;
        private StreamWriter writer;
        private Thread connectorThread;
        private Thread readerThread;
        private Action<string> receive;
        private bool disposed;

        internal HostConnection(string pipeName)
        {
            this.pipeName = pipeName;
        }

        internal void Start(Action<string> receive)
        {
            this.receive = receive;

            lock (writeGate)
            {
                if (disposed || connectorThread != null) return;

                connectorThread = new Thread(new ThreadStart(ConnectLoop))
                {
                    IsBackground = true,
                    Name = "ADOFAI Workbench Host Connector"
                };
                connectorThread.Start();
            }
        }

        internal void SendAction(string paneId, string actionId, string argument)
        {
            Send("ACTION|" + Encode(paneId) + "|" + Encode(actionId) + "|" + Encode(argument));
        }

        private void ConnectLoop()
        {
            NamedPipeClientStream newPipe = null;
            StreamReader newReader = null;
            StreamWriter newWriter = null;

            try
            {
                newPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
                newPipe.Connect(10000);
                newReader = new StreamReader(newPipe, Encoding.UTF8, false, 4096, true);
                newWriter = new StreamWriter(newPipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

                lock (writeGate)
                {
                    if (disposed)
                    {
                        newReader.Dispose();
                        newWriter.Dispose();
                        newPipe.Dispose();
                        return;
                    }

                    pipe = newPipe;
                    reader = newReader;
                    writer = newWriter;

                    while (pending.Count > 0)
                        writer.WriteLine(pending.Dequeue());
                }

                readerThread = new Thread(new ThreadStart(ReadLoop))
                {
                    IsBackground = true,
                    Name = "ADOFAI Workbench Host IPC"
                };
                readerThread.Start();

                Send("LOG|" + Encode("Host connected on CLR " + Environment.Version));
            }
            catch (Exception ex)
            {
                try
                {
                    if (newReader != null) newReader.Dispose();
                    if (newWriter != null) newWriter.Dispose();
                    if (newPipe != null) newPipe.Dispose();
                }
                catch { }

                Action<string> callback = receive;
                if (callback != null)
                {
                    try { callback("HOST_ERROR|" + Encode("IPC connection failed: " + ex.Message)); }
                    catch { }
                }
            }
        }

        private void Send(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            lock (writeGate)
            {
                if (disposed) return;

                if (writer == null)
                {
                    pending.Enqueue(line);
                    return;
                }

                try
                {
                    writer.WriteLine(line);
                }
                catch
                {
                    pending.Enqueue(line);
                }
            }
        }

        private void ReadLoop()
        {
            StreamReader currentReader;
            lock (writeGate) currentReader = reader;
            if (currentReader == null) return;

            try
            {
                string line;
                while ((line = currentReader.ReadLine()) != null)
                {
                    Action<string> callback = receive;
                    if (callback != null) callback(line);
                }
            }
            catch { }
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

        public void Dispose()
        {
            lock (writeGate)
            {
                disposed = true;
                try { if (reader != null) reader.Dispose(); } catch { }
                try { if (writer != null) writer.Dispose(); } catch { }
                try { if (pipe != null) pipe.Dispose(); } catch { }
                reader = null;
                writer = null;
                pipe = null;
                pending.Clear();
            }
        }
    }

    internal sealed class PaneState
    {
        internal string Id;
        internal string Title;
        internal bool CanClose;
        internal string ViewPayload = string.Empty;
        internal int SyncGeneration;
    }

    internal sealed class HostForm : Form
    {
        private static readonly Color ChromeBack = Color.FromArgb(35, 38, 46);
        private static readonly Color PaneBack = Color.FromArgb(19, 21, 26);
        private static readonly Color TextColor = Color.FromArgb(225, 228, 235);
        private static readonly string StateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADOFAIWorkbench");
        private static readonly string LayoutPath = Path.Combine(StateDirectory, "layout.xml");
        private static readonly string WindowPath = Path.Combine(StateDirectory, "window.txt");

        private readonly HostConnection connection;
        private readonly DockPanel dockPanel = new DockPanel();
        private readonly ToolStrip toolbar = new ToolStrip();
        private readonly ToolStripDropDownButton panesMenu = new ToolStripDropDownButton("Panes");
        private readonly StatusStrip statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel status = new ToolStripStatusLabel("Workbench host ready");
        private readonly Dictionary<string, PaneState> states = new Dictionary<string, PaneState>(StringComparer.Ordinal);
        private readonly Dictionary<string, PaneContent> contents = new Dictionary<string, PaneContent>(StringComparer.Ordinal);
        private int syncGeneration;
        private bool syncing;
        private bool loadingLayout;

        internal HostForm(HostConnection connection)
        {
            this.connection = connection;
            Text = "ADOFAI Workbench";
            Width = 1100;
            Height = 720;
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            BackColor = ChromeBack;
            ForeColor = TextColor;
            AutoScaleMode = AutoScaleMode.Dpi;

            dockPanel.Dock = DockStyle.Fill;
            dockPanel.DocumentStyle = DocumentStyle.DockingWindow;
            dockPanel.AllowEndUserDocking = true;
            dockPanel.AllowEndUserNestedDocking = true;
            dockPanel.ShowDocumentIcon = false;
            dockPanel.Theme = new VS2015DarkTheme();
            Controls.Add(dockPanel);

            toolbar.Dock = DockStyle.Top;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.BackColor = ChromeBack;
            toolbar.ForeColor = TextColor;
            toolbar.Items.Add(panesMenu);
            toolbar.Items.Add(new ToolStripSeparator());
            ToolStripButton save = new ToolStripButton("Save Layout");
            save.Click += delegate { SaveLayout(); };
            ToolStripButton reset = new ToolStripButton("Reset Layout");
            reset.Click += delegate { ResetLayout(); };
            toolbar.Items.Add(save);
            toolbar.Items.Add(reset);
            Controls.Add(toolbar);

            statusStrip.Dock = DockStyle.Bottom;
            statusStrip.BackColor = ChromeBack;
            statusStrip.ForeColor = TextColor;
            statusStrip.Items.Add(status);
            Controls.Add(statusStrip);
            toolbar.BringToFront();
            statusStrip.BringToFront();

            RestoreWindowState();
            RestoreLayout();
            FormClosing += OnFormClosing;
            ResizeEnd += delegate { SaveWindowState(); };
            Move += delegate { if (WindowState == FormWindowState.Normal) SaveWindowState(); };
        }

        internal void EnsureVisibleAndForeground()
        {
            if (IsDisposed) return;

            Rectangle candidate = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            bool visible = false;
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle intersection = Rectangle.Intersect(screen.WorkingArea, candidate);
                if (intersection.Width >= 64 && intersection.Height >= 64)
                {
                    visible = true;
                    break;
                }
            }

            if (!visible)
            {
                Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                Rectangle area = screen.WorkingArea;
                int width = Math.Min(Math.Max(Width, MinimumSize.Width), area.Width);
                int height = Math.Min(Math.Max(Height, MinimumSize.Height), area.Height);
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(
                    area.Left + Math.Max(0, (area.Width - width) / 2),
                    area.Top + Math.Max(0, (area.Height - height) / 2),
                    width,
                    height);
                WindowState = FormWindowState.Normal;
            }

            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            ShowInTaskbar = true;
            if (!Visible) Show();
            BringToFront();
            Activate();
        }

        internal void ReceiveMessage(string line)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate { HandleMessage(line); });
            }
            catch { }
        }

        private void HandleMessage(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string[] parts = line.Split('|');
            switch (parts[0])
            {
                case "SHOW":
                    EnsureVisibleAndForeground();
                    break;
                case "HIDE":
                    Hide();
                    break;
                case "OPEN":
                    if (parts.Length >= 2) OpenPane(HostConnection.Decode(parts[1]));
                    break;
                case "HOST_ERROR":
                    if (parts.Length >= 2) status.Text = HostConnection.Decode(parts[1]);
                    break;
                case "SYNC_BEGIN":
                    syncing = true;
                    syncGeneration++;
                    break;
                case "PANE":
                    if (parts.Length >= 4)
                    {
                        string id = HostConnection.Decode(parts[1]);
                        PaneState state;
                        if (!states.TryGetValue(id, out state))
                            states[id] = state = new PaneState { Id = id };
                        state.Title = HostConnection.Decode(parts[2]);
                        state.CanClose = parts[3] == "1";
                        state.SyncGeneration = syncGeneration;
                        RefreshContent(id);
                    }
                    break;
                case "VIEW":
                    if (parts.Length >= 3)
                    {
                        string id = HostConnection.Decode(parts[1]);
                        PaneState state;
                        if (!states.TryGetValue(id, out state))
                            states[id] = state = new PaneState { Id = id, Title = id, CanClose = true };
                        state.ViewPayload = HostConnection.Decode(parts[2]);
                        state.SyncGeneration = syncGeneration;
                        RefreshContent(id);
                    }
                    break;
                case "SYNC_END":
                    syncing = false;
                    RemoveStaleStates();
                    RebuildPanesMenu();
                    if (dockPanel.Contents.Count == 0 && states.ContainsKey("workbench.welcome"))
                        OpenPane("workbench.welcome");
                    break;
            }
        }

        private void RemoveStaleStates()
        {
            var stale = new List<string>();
            foreach (KeyValuePair<string, PaneState> pair in states)
            {
                if (pair.Value.SyncGeneration != syncGeneration) stale.Add(pair.Key);
            }
            for (int i = 0; i < stale.Count; i++) states.Remove(stale[i]);
            RebuildPanesMenu();
        }

        private void RebuildPanesMenu()
        {
            panesMenu.DropDownItems.Clear();
            var list = new List<PaneState>(states.Values);
            list.Sort(delegate(PaneState a, PaneState b)
            {
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < list.Count; i++)
            {
                PaneState state = list[i];
                string id = state.Id;
                ToolStripMenuItem item = new ToolStripMenuItem(state.Title ?? id);
                item.Click += delegate { OpenPane(id); };
                PaneContent open;
                item.Checked = contents.TryGetValue(id, out open) && open != null && !open.IsDisposed;
                panesMenu.DropDownItems.Add(item);
            }
        }

        private void OpenPane(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            PaneContent content;
            if (contents.TryGetValue(id, out content) && content != null && !content.IsDisposed)
            {
                content.Show();
                content.Activate();
                return;
            }

            content = new PaneContent(this, connection, id);
            contents[id] = content;
            RefreshContent(id);
            content.Show(dockPanel, DockState.Document);
            content.Activate();
            if (!loadingLayout) SaveLayout();
            RebuildPanesMenu();
        }

        private void RefreshContent(string id)
        {
            PaneContent content;
            if (!contents.TryGetValue(id, out content) || content == null || content.IsDisposed) return;
            PaneState state;
            states.TryGetValue(id, out state);
            content.Apply(state);
        }

        internal void ContentClosed(string id, PaneContent content)
        {
            PaneContent current;
            if (contents.TryGetValue(id, out current) && ReferenceEquals(current, content))
                contents.Remove(id);
            RebuildPanesMenu();
            if (!loadingLayout) SaveLayout();
        }

        internal PaneState FindState(string id)
        {
            PaneState state;
            return states.TryGetValue(id, out state) ? state : null;
        }

        private void SaveLayout()
        {
            if (loadingLayout) return;
            try
            {
                Directory.CreateDirectory(StateDirectory);
                dockPanel.SaveAsXml(LayoutPath);
                SaveWindowState();
                status.Text = "Layout saved";
            }
            catch (Exception ex)
            {
                status.Text = "Layout save failed: " + ex.Message;
            }
        }

        private void RestoreLayout()
        {
            if (!File.Exists(LayoutPath)) return;
            loadingLayout = true;
            try
            {
                dockPanel.LoadFromXml(LayoutPath, DeserializeContent);
            }
            catch
            {
                try { File.Delete(LayoutPath); } catch { }
            }
            finally
            {
                loadingLayout = false;
            }
        }

        private IDockContent DeserializeContent(string persistString)
        {
            const string prefix = "pane:";
            if (string.IsNullOrEmpty(persistString) ||
                !persistString.StartsWith(prefix, StringComparison.Ordinal))
                return null;

            string id = persistString.Substring(prefix.Length);
            PaneContent content;
            if (!contents.TryGetValue(id, out content))
                contents[id] = content = new PaneContent(this, connection, id);
            return content;
        }

        private void ResetLayout()
        {
            loadingLayout = true;
            try
            {
                var open = new List<PaneContent>(contents.Values);
                for (int i = 0; i < open.Count; i++)
                {
                    open[i].ForceClose = true;
                    try { open[i].Close(); } catch { }
                }
                contents.Clear();
                try { if (File.Exists(LayoutPath)) File.Delete(LayoutPath); } catch { }
            }
            finally
            {
                loadingLayout = false;
            }

            if (states.ContainsKey("workbench.welcome")) OpenPane("workbench.welcome");
            status.Text = "Layout reset";
        }

        private void RestoreWindowState()
        {
            try
            {
                if (!File.Exists(WindowPath)) return;
                string[] p = File.ReadAllText(WindowPath).Split('|');
                if (p.Length < 5) return;

                int x, y, w, h;
                if (!int.TryParse(p[0], out x) ||
                    !int.TryParse(p[1], out y) ||
                    !int.TryParse(p[2], out w) ||
                    !int.TryParse(p[3], out h))
                    return;

                Rectangle r = new Rectangle(x, y, Math.Max(640, w), Math.Max(420, h));
                bool visible = false;
                foreach (Screen screen in Screen.AllScreens)
                {
                    Rectangle intersection = Rectangle.Intersect(screen.WorkingArea, r);
                    if (intersection.Width >= 64 && intersection.Height >= 64)
                    {
                        visible = true;
                        break;
                    }
                }
                if (!visible) return;

                StartPosition = FormStartPosition.Manual;
                Bounds = r;
                if (p[4] == "max") WindowState = FormWindowState.Maximized;
            }
            catch { }
        }

        private void SaveWindowState()
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                File.WriteAllText(
                    WindowPath,
                    r.X + "|" + r.Y + "|" + r.Width + "|" + r.Height + "|" +
                    (WindowState == FormWindowState.Maximized ? "max" : "normal"));
            }
            catch { }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            SaveLayout();
            Hide();
        }
    }

    internal sealed class PaneContent : DockContent
    {
        private readonly HostForm owner;
        private readonly HostConnection connection;
        private readonly string paneId;
        private readonly FlowLayoutPanel root = new FlowLayoutPanel();
        internal bool ForceClose;

        internal PaneContent(HostForm owner, HostConnection connection, string paneId)
        {
            this.owner = owner;
            this.connection = connection;
            this.paneId = paneId;
            HideOnClose = false;
            DockAreas = DockAreas.Document |
                        DockAreas.DockLeft |
                        DockAreas.DockRight |
                        DockAreas.DockTop |
                        DockAreas.DockBottom |
                        DockAreas.Float;
            BackColor = Color.FromArgb(19, 21, 26);
            ForeColor = Color.FromArgb(225, 228, 235);
            root.Dock = DockStyle.Fill;
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoScroll = true;
            root.Padding = new Padding(12);
            root.BackColor = BackColor;
            Controls.Add(root);
            Apply(owner.FindState(paneId));
        }

        internal void Apply(PaneState state)
        {
            string title = state != null && !string.IsNullOrEmpty(state.Title) ? state.Title : paneId;
            Text = title;
            TabText = title;
            bool canClose = state == null || state.CanClose;
            CloseButton = canClose;
            CloseButtonVisible = canClose;
            Render(state != null ? state.ViewPayload : string.Empty);
        }

        private void Render(string payload)
        {
            root.SuspendLayout();
            try
            {
                while (root.Controls.Count > 0)
                {
                    Control c = root.Controls[0];
                    root.Controls.RemoveAt(0);
                    c.Dispose();
                }

                Control parent = root;
                string[] lines = (payload ?? string.Empty).Split(
                    new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p[0] == "R+")
                    {
                        FlowLayoutPanel row = new FlowLayoutPanel
                        {
                            AutoSize = true,
                            FlowDirection = FlowDirection.LeftToRight,
                            WrapContents = true,
                            Margin = new Padding(0, 2, 0, 4),
                            BackColor = root.BackColor
                        };
                        root.Controls.Add(row);
                        parent = row;
                    }
                    else if (p[0] == "R-")
                    {
                        parent = root;
                    }
                    else if (p[0] == "T" && p.Length >= 4)
                    {
                        float size;
                        if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out size))
                            size = 10f;

                        Label label = new Label
                        {
                            Text = HostConnection.Decode(p[1]),
                            AutoSize = true,
                            MaximumSize = new Size(900, 0),
                            ForeColor = root.ForeColor,
                            Font = new Font(
                                SystemFonts.MessageBoxFont.FontFamily,
                                size,
                                p[3] == "1" ? FontStyle.Bold : FontStyle.Regular),
                            Margin = new Padding(2, 2, 2, 4)
                        };
                        AddTo(parent, label);
                    }
                    else if (p[0] == "B" && p.Length >= 5)
                    {
                        string action = HostConnection.Decode(p[2]);
                        string arg = HostConnection.Decode(p[3]);
                        Button button = new Button
                        {
                            Text = HostConnection.Decode(p[1]),
                            AutoSize = true,
                            MinimumSize = new Size(70, 30),
                            Margin = new Padding(2),
                            Padding = new Padding(6, 0, 6, 0),
                            FlatStyle = FlatStyle.Flat,
                            BackColor = p[4] == "1"
                                ? Color.FromArgb(70, 86, 118)
                                : Color.FromArgb(50, 54, 64),
                            ForeColor = Color.White
                        };
                        button.Click += delegate { connection.SendAction(paneId, action, arg); };
                        AddTo(parent, button);
                    }
                    else if (p[0] == "S" && p.Length >= 2)
                    {
                        int h;
                        if (!int.TryParse(p[1], out h)) h = 4;
                        AddTo(parent, new Panel
                        {
                            Width = 1,
                            Height = Math.Max(0, h),
                            Margin = new Padding(0)
                        });
                    }
                }
            }
            finally
            {
                root.ResumeLayout(true);
            }
        }

        private static void AddTo(Control parent, Control child)
        {
            FlowLayoutPanel flow = parent as FlowLayoutPanel;
            if (flow != null) flow.Controls.Add(child);
        }

        protected override string GetPersistString()
        {
            return "pane:" + paneId;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            PaneState state = owner.FindState(paneId);
            if (!ForceClose && state != null && !state.CanClose)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            owner.ContentClosed(paneId, this);
            base.OnFormClosed(e);
        }
    }
}
