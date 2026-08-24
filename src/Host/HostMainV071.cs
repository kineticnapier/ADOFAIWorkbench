using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace KineticNapier.ADOFAIWorkbench.Host
{
    internal static class TcpProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            int port;
            int parentPid;
            if (args == null || args.Length < 3 ||
                !int.TryParse(args[0], out port) ||
                string.IsNullOrWhiteSpace(args[1]) ||
                !int.TryParse(args[2], out parentPid))
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            StartParentGuard(parentPid);

            using (TcpHostConnection connection = new TcpHostConnection(port, args[1]))
            using (TcpHostForm form = new TcpHostForm(connection))
            {
                form.Shown += delegate
                {
                    form.EnsureVisibleAndForeground();
                    connection.Start(form.ReceiveMessage);
                };
                Application.Run(form);
            }
        }

        private static void StartParentGuard(int parentPid)
        {
            Thread thread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    using (Process parent = Process.GetProcessById(parentPid))
                        parent.WaitForExit();
                }
                catch { }
                try { Environment.Exit(0); } catch { }
            }))
            {
                IsBackground = true,
                Name = "ADOFAI Workbench Parent Guard"
            };
            thread.Start();
        }
    }

    internal sealed class TcpHostConnection : IDisposable
    {
        private readonly int port;
        private readonly string token;
        private readonly object writeGate = new object();
        private readonly Queue<string> pending = new Queue<string>();
        private TcpClient client;
        private NetworkStream stream;
        private StreamReader reader;
        private StreamWriter writer;
        private Thread connectorThread;
        private Action<string> receive;
        private bool disposed;

        internal TcpHostConnection(int port, string token)
        {
            this.port = port;
            this.token = token;
        }

        internal void Start(Action<string> callback)
        {
            receive = callback;
            lock (writeGate)
            {
                if (disposed || connectorThread != null) return;
                connectorThread = new Thread(new ThreadStart(ConnectLoop))
                {
                    IsBackground = true,
                    Name = "ADOFAI Workbench TCP Connector"
                };
                connectorThread.Start();
            }
        }

        internal void SendAction(string paneId, string actionId, string argument)
        {
            Send("ACTION|" + Encode(paneId) + "|" + Encode(actionId) + "|" + Encode(argument));
        }

        internal void SendLog(string text)
        {
            Send("LOG|" + Encode(text));
        }

        private void ConnectLoop()
        {
            TcpClient newClient = null;
            NetworkStream newStream = null;
            StreamReader newReader = null;
            StreamWriter newWriter = null;
            try
            {
                newClient = new TcpClient { NoDelay = true };
                Exception lastError = null;
                bool connected = false;
                for (int i = 0; i < 200 && !disposed; i++)
                {
                    try
                    {
                        newClient.Connect(IPAddress.Loopback, port);
                        connected = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Thread.Sleep(25);
                    }
                }
                if (!connected) throw new IOException("Could not connect to ADOFAI bridge.", lastError);

                newStream = newClient.GetStream();
                newReader = new StreamReader(newStream, Encoding.UTF8, false, 4096, true);
                newWriter = new StreamWriter(newStream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                newWriter.WriteLine("HELLO|" + token);

                lock (writeGate)
                {
                    if (disposed)
                    {
                        newReader.Dispose();
                        newWriter.Dispose();
                        newStream.Dispose();
                        newClient.Close();
                        return;
                    }
                    client = newClient;
                    stream = newStream;
                    reader = newReader;
                    writer = newWriter;
                    while (pending.Count > 0) writer.WriteLine(pending.Dequeue());
                }

                Thread readThread = new Thread(new ThreadStart(ReadLoop))
                {
                    IsBackground = true,
                    Name = "ADOFAI Workbench TCP Reader"
                };
                readThread.Start();
                SendLog("TCP host connected on CLR " + Environment.Version);
            }
            catch (Exception ex)
            {
                try { if (newReader != null) newReader.Dispose(); } catch { }
                try { if (newWriter != null) newWriter.Dispose(); } catch { }
                try { if (newStream != null) newStream.Dispose(); } catch { }
                try { if (newClient != null) newClient.Close(); } catch { }
                DeliverLocal("HOST_ERROR|" + Encode("IPC connection failed: " + ex.Message));
            }
        }

        private void ReadLoop()
        {
            StreamReader current;
            lock (writeGate) current = reader;
            if (current == null) return;
            try
            {
                string line;
                while ((line = current.ReadLine()) != null) DeliverLocal(line);
            }
            catch { }
            finally { DeliverLocal("DISCONNECTED"); }
        }

        private void DeliverLocal(string line)
        {
            Action<string> callback = receive;
            if (callback == null) return;
            try { callback(line); } catch { }
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
                try { writer.WriteLine(line); } catch { }
            }
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
                try { if (stream != null) stream.Dispose(); } catch { }
                try { if (client != null) client.Close(); } catch { }
                reader = null;
                writer = null;
                stream = null;
                client = null;
                pending.Clear();
            }
        }
    }

    internal sealed class TcpPaneState
    {
        internal string Id;
        internal string Title;
        internal bool CanClose;
        internal string ViewPayload = string.Empty;
        internal int SyncGeneration;
    }

    internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        internal BufferedFlowLayoutPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }
    }

    internal sealed class UiSpec
    {
        internal string Kind;
        internal string Text;
        internal float Size;
        internal bool Bold;
        internal string Action;
        internal string Argument;
        internal bool Selected;
        internal int Height;
    }

    internal sealed class ButtonBinding
    {
        internal string Action;
        internal string Argument;
    }

    internal sealed class TcpHostForm : Form
    {
        private static readonly Color ChromeBack = Color.FromArgb(35, 38, 46);
        private static readonly Color TextColor = Color.FromArgb(225, 228, 235);
        private static readonly string StateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ADOFAIWorkbench");
        private static readonly string LayoutPath = Path.Combine(StateDirectory, "layout.xml");
        private static readonly string WindowPath = Path.Combine(StateDirectory, "window.txt");

        private readonly TcpHostConnection connection;
        private readonly DockPanel dockPanel = new DockPanel();
        private readonly ToolStrip toolbar = new ToolStrip();
        private readonly ToolStripDropDownButton panesMenu = new ToolStripDropDownButton("Panes");
        private readonly ToolStripSeparator paneShortcutSeparator = new ToolStripSeparator();
        private readonly ToolStripSeparator layoutSeparator = new ToolStripSeparator();
        private readonly ToolStripButton saveButton = new ToolStripButton("Save Layout");
        private readonly ToolStripButton resetButton = new ToolStripButton("Reset Layout");
        private readonly List<ToolStripItem> paneShortcutItems = new List<ToolStripItem>();
        private readonly StatusStrip statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel status = new ToolStripStatusLabel("Waiting for ADOFAI...");
        private readonly Dictionary<string, TcpPaneState> states = new Dictionary<string, TcpPaneState>(StringComparer.Ordinal);
        private readonly Dictionary<string, TcpPaneContent> contents = new Dictionary<string, TcpPaneContent>(StringComparer.Ordinal);
        private Font tabFont;
        private int syncGeneration;
        private bool loadingLayout;
        private bool forceExit;

        internal TcpHostForm(TcpHostConnection connection)
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
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            ConfigureDockPanel();
            ConfigureToolbar();
            ConfigureStatus();

            RebuildPanesUi();
            RestoreWindowState();
            RestoreLayout();

            FormClosing += OnFormClosing;
            ResizeEnd += delegate { SaveWindowState(); };
            Move += delegate { if (WindowState == FormWindowState.Normal) SaveWindowState(); };
        }

        private void ConfigureDockPanel()
        {
            dockPanel.Dock = DockStyle.Fill;
            dockPanel.DocumentStyle = DocumentStyle.DockingWindow;
            dockPanel.AllowEndUserDocking = true;
            dockPanel.AllowEndUserNestedDocking = true;
            dockPanel.ShowDocumentIcon = false;

            VS2015DarkTheme theme = new VS2015DarkTheme();
            tabFont = new Font("Segoe UI", 12.0f, FontStyle.Regular, GraphicsUnit.Point);
            theme.Skin.DockPaneStripSkin.TextFont = tabFont;
            dockPanel.Theme = theme;
            Controls.Add(dockPanel);
        }

        private void ConfigureToolbar()
        {
            toolbar.Dock = DockStyle.Top;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.BackColor = ChromeBack;
            toolbar.ForeColor = TextColor;
            toolbar.AutoSize = false;
            toolbar.Height = 34;
            toolbar.Padding = new Padding(4, 3, 4, 3);
            panesMenu.AutoSize = true;
            panesMenu.ToolTipText = "Open a pane";
            saveButton.Click += delegate { SaveLayout(); status.Text = "Layout saved"; };
            resetButton.Click += delegate { ResetLayout(); };
            toolbar.Items.Add(panesMenu);
            toolbar.Items.Add(paneShortcutSeparator);
            toolbar.Items.Add(layoutSeparator);
            toolbar.Items.Add(saveButton);
            toolbar.Items.Add(resetButton);
            Controls.Add(toolbar);
            toolbar.BringToFront();
        }

        private void ConfigureStatus()
        {
            statusStrip.Dock = DockStyle.Bottom;
            statusStrip.BackColor = ChromeBack;
            statusStrip.ForeColor = TextColor;
            statusStrip.Items.Add(status);
            Controls.Add(statusStrip);
            statusStrip.BringToFront();
        }

        internal void EnsureVisibleAndForeground()
        {
            if (IsDisposed) return;
            Rectangle candidate = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            bool visible = false;
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle intersection = Rectangle.Intersect(screen.WorkingArea, candidate);
                if (intersection.Width >= 64 && intersection.Height >= 64) { visible = true; break; }
            }
            if (!visible)
            {
                Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                Rectangle area = screen.WorkingArea;
                int width = Math.Min(Math.Max(Width, MinimumSize.Width), area.Width);
                int height = Math.Min(Math.Max(Height, MinimumSize.Height), area.Height);
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(area.Left + Math.Max(0, (area.Width - width) / 2),
                    area.Top + Math.Max(0, (area.Height - height) / 2), width, height);
                WindowState = FormWindowState.Normal;
            }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            BringToFront();
            Activate();
        }

        internal void ReceiveMessage(string line)
        {
            if (IsDisposed) return;
            try { BeginInvoke((MethodInvoker)delegate { HandleMessage(line); }); } catch { }
        }

        private void HandleMessage(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string[] parts = line.Split('|');
            switch (parts[0])
            {
                case "SHOW": EnsureVisibleAndForeground(); break;
                case "HIDE": Hide(); break;
                case "OPEN": if (parts.Length >= 2) OpenPane(TcpHostConnection.Decode(parts[1])); break;
                case "EXIT":
                case "DISCONNECTED": RequestExit(); break;
                case "HOST_ERROR":
                    if (parts.Length >= 2) status.Text = TcpHostConnection.Decode(parts[1]);
                    break;
                case "SYNC_BEGIN":
                    syncGeneration++;
                    status.Text = "Connected | syncing panes...";
                    break;
                case "PANE":
                    if (parts.Length >= 4)
                    {
                        string id = TcpHostConnection.Decode(parts[1]);
                        TcpPaneState state;
                        if (!states.TryGetValue(id, out state)) states[id] = state = new TcpPaneState { Id = id };
                        state.Title = TcpHostConnection.Decode(parts[2]);
                        state.CanClose = parts[3] == "1";
                        state.SyncGeneration = syncGeneration;
                        RefreshContent(id);
                    }
                    break;
                case "VIEW":
                    if (parts.Length >= 3)
                    {
                        string id = TcpHostConnection.Decode(parts[1]);
                        string payload = TcpHostConnection.Decode(parts[2]);
                        TcpPaneState state;
                        if (!states.TryGetValue(id, out state)) states[id] = state = new TcpPaneState { Id = id, Title = id, CanClose = true };
                        state.SyncGeneration = syncGeneration;
                        if (!string.Equals(state.ViewPayload, payload, StringComparison.Ordinal))
                        {
                            state.ViewPayload = payload;
                            RefreshContent(id);
                        }
                    }
                    break;
                case "SYNC_END":
                    RemoveStaleStates();
                    RebuildPanesUi();
                    status.Text = "Connected | Panes=" + states.Count.ToString();
                    Text = "ADOFAI Workbench";
                    connection.SendLog("Synced panes=" + states.Count.ToString());
                    if (dockPanel.Contents.Count == 0 && states.ContainsKey("workbench.welcome")) OpenPane("workbench.welcome");
                    break;
            }
        }

        private void RemoveStaleStates()
        {
            var stale = new List<string>();
            foreach (KeyValuePair<string, TcpPaneState> pair in states)
                if (pair.Value.SyncGeneration != syncGeneration) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) states.Remove(stale[i]);
        }

        private void RebuildPanesUi()
        {
            panesMenu.DropDownItems.Clear();
            for (int i = 0; i < paneShortcutItems.Count; i++)
            {
                toolbar.Items.Remove(paneShortcutItems[i]);
                paneShortcutItems[i].Dispose();
            }
            paneShortcutItems.Clear();

            if (states.Count == 0)
            {
                ToolStripMenuItem empty = new ToolStripMenuItem("(No panes received)") { Enabled = false };
                panesMenu.DropDownItems.Add(empty);
                return;
            }

            var list = new List<TcpPaneState>(states.Values);
            list.Sort(delegate(TcpPaneState a, TcpPaneState b)
            {
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });

            int insertAt = toolbar.Items.IndexOf(layoutSeparator);
            for (int i = 0; i < list.Count; i++)
            {
                TcpPaneState state = list[i];
                string id = state.Id;
                ToolStripMenuItem item = new ToolStripMenuItem(state.Title ?? id);
                item.Click += delegate { OpenPane(id); };
                TcpPaneContent open;
                item.Checked = contents.TryGetValue(id, out open) && open != null && !open.IsDisposed;
                panesMenu.DropDownItems.Add(item);

                ToolStripButton shortcut = new ToolStripButton(state.Title ?? id)
                {
                    DisplayStyle = ToolStripItemDisplayStyle.Text,
                    AutoSize = true,
                    ToolTipText = "Open " + (state.Title ?? id)
                };
                shortcut.Click += delegate { OpenPane(id); };
                paneShortcutItems.Add(shortcut);
                toolbar.Items.Insert(insertAt++, shortcut);
            }
        }

        private void OpenPane(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            TcpPaneState state;
            if (!states.TryGetValue(id, out state)) { status.Text = "Unknown pane: " + id; return; }

            TcpPaneContent content;
            if (contents.TryGetValue(id, out content) && content != null && !content.IsDisposed)
            {
                content.Show();
                content.Activate();
                return;
            }
            content = new TcpPaneContent(this, connection, id);
            contents[id] = content;
            content.Apply(state);
            content.Show(dockPanel, DockState.Document);
            content.Activate();
            if (!loadingLayout) SaveLayout();
            RebuildPanesUi();
        }

        private void RefreshContent(string id)
        {
            TcpPaneContent content;
            if (!contents.TryGetValue(id, out content) || content == null || content.IsDisposed) return;
            TcpPaneState state;
            states.TryGetValue(id, out state);
            content.Apply(state);
        }

        internal void ContentClosed(string id, TcpPaneContent content)
        {
            TcpPaneContent current;
            if (contents.TryGetValue(id, out current) && ReferenceEquals(current, content)) contents.Remove(id);
            RebuildPanesUi();
            if (!loadingLayout) SaveLayout();
        }

        internal TcpPaneState FindState(string id)
        {
            TcpPaneState state;
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
            }
            catch (Exception ex) { status.Text = "Layout save failed: " + ex.Message; }
        }

        private void RestoreLayout()
        {
            if (!File.Exists(LayoutPath)) return;
            loadingLayout = true;
            try { dockPanel.LoadFromXml(LayoutPath, DeserializeContent); }
            catch { try { File.Delete(LayoutPath); } catch { } }
            finally { loadingLayout = false; }
        }

        private IDockContent DeserializeContent(string persistString)
        {
            const string prefix = "pane:";
            if (string.IsNullOrEmpty(persistString) || !persistString.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string id = persistString.Substring(prefix.Length);
            TcpPaneContent content;
            if (!contents.TryGetValue(id, out content)) contents[id] = content = new TcpPaneContent(this, connection, id);
            return content;
        }

        private void ResetLayout()
        {
            loadingLayout = true;
            try
            {
                var open = new List<TcpPaneContent>(contents.Values);
                for (int i = 0; i < open.Count; i++)
                {
                    open[i].ForceClose = true;
                    try { open[i].Close(); } catch { }
                }
                contents.Clear();
                try { if (File.Exists(LayoutPath)) File.Delete(LayoutPath); } catch { }
            }
            finally { loadingLayout = false; }
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
                if (!int.TryParse(p[0], out x) || !int.TryParse(p[1], out y) || !int.TryParse(p[2], out w) || !int.TryParse(p[3], out h)) return;
                Rectangle r = new Rectangle(x, y, Math.Max(640, w), Math.Max(420, h));
                bool visible = false;
                foreach (Screen screen in Screen.AllScreens) if (screen.WorkingArea.IntersectsWith(r)) { visible = true; break; }
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
                File.WriteAllText(WindowPath, r.X + "|" + r.Y + "|" + r.Width + "|" + r.Height + "|" +
                    (WindowState == FormWindowState.Maximized ? "max" : "normal"));
            }
            catch { }
        }

        private void RequestExit()
        {
            if (IsDisposed) return;
            forceExit = true;
            try { SaveLayout(); } catch { }
            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!forceExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                SaveLayout();
                Hide();
                return;
            }
            SaveLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && tabFont != null)
            {
                tabFont.Dispose();
                tabFont = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class TcpPaneContent : DockContent
    {
        private const int WM_SETREDRAW = 0x000B;
        private static readonly Color PaneBack = Color.FromArgb(19, 21, 26);
        private static readonly Color TextColor = Color.FromArgb(225, 228, 235);
        private static readonly Color ButtonNormal = Color.FromArgb(50, 54, 64);
        private static readonly Color ButtonSelected = Color.FromArgb(70, 86, 118);

        private readonly TcpHostForm owner;
        private readonly TcpHostConnection connection;
        private readonly string paneId;
        private readonly BufferedFlowLayoutPanel root = new BufferedFlowLayoutPanel();
        private readonly List<Control> renderedControls = new List<Control>();
        private string renderedShape = string.Empty;
        private string renderedPayload = null;
        internal bool ForceClose;

        internal TcpPaneContent(TcpHostForm owner, TcpHostConnection connection, string paneId)
        {
            this.owner = owner;
            this.connection = connection;
            this.paneId = paneId;
            HideOnClose = false;
            DockAreas = DockAreas.Document | DockAreas.DockLeft | DockAreas.DockRight |
                        DockAreas.DockTop | DockAreas.DockBottom | DockAreas.Float;
            BackColor = PaneBack;
            ForeColor = TextColor;
            root.Dock = DockStyle.Fill;
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoScroll = true;
            root.Padding = new Padding(14);
            root.BackColor = BackColor;
            Controls.Add(root);
            Apply(owner.FindState(paneId));
        }

        internal void Apply(TcpPaneState state)
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
            payload = payload ?? string.Empty;
            if (string.Equals(renderedPayload, payload, StringComparison.Ordinal)) return;

            List<UiSpec> specs = Parse(payload);
            string shape = BuildShape(specs);
            if (string.Equals(shape, renderedShape, StringComparison.Ordinal) && renderedControls.Count == CountRenderable(specs))
                UpdateExisting(specs, payload);
            else
                Rebuild(specs, shape, payload);
        }

        private static List<UiSpec> Parse(string payload)
        {
            var specs = new List<UiSpec>();
            string[] lines = payload.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string[] p = lines[i].Split('\t');
                if (p.Length == 0) continue;
                UiSpec spec = new UiSpec { Kind = p[0] };
                if (p[0] == "T" && p.Length >= 4)
                {
                    spec.Text = TcpHostConnection.Decode(p[1]);
                    if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out spec.Size)) spec.Size = 10f;
                    spec.Bold = p[3] == "1";
                }
                else if (p[0] == "B" && p.Length >= 5)
                {
                    spec.Text = TcpHostConnection.Decode(p[1]);
                    spec.Action = TcpHostConnection.Decode(p[2]);
                    spec.Argument = TcpHostConnection.Decode(p[3]);
                    spec.Selected = p[4] == "1";
                }
                else if (p[0] == "S" && p.Length >= 2)
                {
                    if (!int.TryParse(p[1], out spec.Height)) spec.Height = 4;
                }
                specs.Add(spec);
            }
            return specs;
        }

        private static string BuildShape(List<UiSpec> specs)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < specs.Count; i++) b.Append(specs[i].Kind).Append('|');
            return b.ToString();
        }

        private static int CountRenderable(List<UiSpec> specs)
        {
            int count = 0;
            for (int i = 0; i < specs.Count; i++)
                if (specs[i].Kind == "T" || specs[i].Kind == "B" || specs[i].Kind == "S") count++;
            return count;
        }

        private void UpdateExisting(List<UiSpec> specs, string payload)
        {
            root.SuspendLayout();
            try
            {
                int controlIndex = 0;
                for (int i = 0; i < specs.Count; i++)
                {
                    UiSpec spec = specs[i];
                    if (spec.Kind != "T" && spec.Kind != "B" && spec.Kind != "S") continue;
                    Control control = renderedControls[controlIndex++];
                    ApplySpec(control, spec);
                }
                renderedPayload = payload;
            }
            finally { root.ResumeLayout(false); }
        }

        private void Rebuild(List<UiSpec> specs, string shape, string payload)
        {
            SetRedraw(root, false);
            root.SuspendLayout();
            try
            {
                while (root.Controls.Count > 0)
                {
                    Control child = root.Controls[0];
                    root.Controls.RemoveAt(0);
                    child.Dispose();
                }
                renderedControls.Clear();
                Control parent = root;
                for (int i = 0; i < specs.Count; i++)
                {
                    UiSpec spec = specs[i];
                    if (spec.Kind == "R+")
                    {
                        BufferedFlowLayoutPanel row = new BufferedFlowLayoutPanel
                        {
                            AutoSize = true,
                            FlowDirection = FlowDirection.LeftToRight,
                            WrapContents = true,
                            Margin = new Padding(0, 2, 0, 6),
                            BackColor = root.BackColor
                        };
                        root.Controls.Add(row);
                        parent = row;
                    }
                    else if (spec.Kind == "R-") parent = root;
                    else
                    {
                        Control control = CreateControl(spec);
                        if (control != null)
                        {
                            AddTo(parent, control);
                            renderedControls.Add(control);
                        }
                    }
                }
                renderedShape = shape;
                renderedPayload = payload;
            }
            finally
            {
                root.ResumeLayout(true);
                SetRedraw(root, true);
                root.Invalidate(true);
            }
        }

        private Control CreateControl(UiSpec spec)
        {
            if (spec.Kind == "T")
            {
                Label label = new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(1200, 0),
                    ForeColor = root.ForeColor,
                    Margin = new Padding(2, 2, 2, 5)
                };
                ApplySpec(label, spec);
                return label;
            }
            if (spec.Kind == "B")
            {
                Button button = new Button
                {
                    AutoSize = true,
                    MinimumSize = new Size(78, 34),
                    Margin = new Padding(2),
                    Padding = new Padding(8, 1, 8, 1),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.White
                };
                button.FlatAppearance.BorderColor = Color.FromArgb(75, 80, 92);
                button.Click += OnActionButtonClick;
                ApplySpec(button, spec);
                return button;
            }
            if (spec.Kind == "S")
            {
                Panel spacer = new Panel { Width = 1, Margin = new Padding(0) };
                ApplySpec(spacer, spec);
                return spacer;
            }
            return null;
        }

        private void ApplySpec(Control control, UiSpec spec)
        {
            Label label = control as Label;
            if (label != null && spec.Kind == "T")
            {
                if (!string.Equals(label.Text, spec.Text ?? string.Empty, StringComparison.Ordinal)) label.Text = spec.Text ?? string.Empty;
                string fontKey = spec.Size.ToString(CultureInfo.InvariantCulture) + ":" + (spec.Bold ? "1" : "0");
                if (!string.Equals(label.Tag as string, fontKey, StringComparison.Ordinal))
                {
                    Font old = label.Font;
                    label.Font = new Font("Segoe UI", spec.Size, spec.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                    label.Tag = fontKey;
                    if (old != null && !ReferenceEquals(old, SystemFonts.DefaultFont)) old.Dispose();
                }
                return;
            }

            Button button = control as Button;
            if (button != null && spec.Kind == "B")
            {
                if (!string.Equals(button.Text, spec.Text ?? string.Empty, StringComparison.Ordinal)) button.Text = spec.Text ?? string.Empty;
                button.BackColor = spec.Selected ? ButtonSelected : ButtonNormal;
                ButtonBinding binding = button.Tag as ButtonBinding;
                if (binding == null) button.Tag = binding = new ButtonBinding();
                binding.Action = spec.Action ?? string.Empty;
                binding.Argument = spec.Argument ?? string.Empty;
                return;
            }

            Panel panel = control as Panel;
            if (panel != null && spec.Kind == "S") panel.Height = Math.Max(0, spec.Height);
        }

        private void OnActionButtonClick(object sender, EventArgs e)
        {
            Button button = sender as Button;
            ButtonBinding binding = button != null ? button.Tag as ButtonBinding : null;
            if (binding != null) connection.SendAction(paneId, binding.Action, binding.Argument);
        }

        private static void AddTo(Control parent, Control child)
        {
            FlowLayoutPanel flow = parent as FlowLayoutPanel;
            if (flow != null) flow.Controls.Add(child);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static void SetRedraw(Control control, bool enabled)
        {
            if (control == null || !control.IsHandleCreated) return;
            try { SendMessage(control.Handle, WM_SETREDRAW, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero); } catch { }
        }

        protected override string GetPersistString() { return "pane:" + paneId; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            TcpPaneState state = owner.FindState(paneId);
            if (!ForceClose && state != null && !state.CanClose) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            owner.ContentClosed(paneId, this);
            base.OnFormClosed(e);
        }
    }
}
