using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace KineticNapier.ADOFAIWorkbench.Host
{
    internal static class TcpProgramV072
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
                FixChromeLayout(form);
                InstallCloseBehavior(form);

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

        private static void InstallCloseBehavior(TcpHostForm form)
        {
            // TcpHostForm's own close handler intentionally prevents a user-close
            // from terminating the host, but older builds hid the form completely.
            // Keep it discoverable instead: X minimizes to the taskbar, while
            // EXIT/disconnect/ADOFAI shutdown still closes the process normally.
            form.FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason != CloseReason.UserClosing) return;

                e.Cancel = true;
                form.ShowInTaskbar = true;
                if (!form.Visible) form.Show();
                form.WindowState = FormWindowState.Minimized;
            };
        }

        private static void FixChromeLayout(TcpHostForm form)
        {
            ToolStrip toolbar = GetField<ToolStrip>(form, "toolbar");
            DockPanel dockPanel = GetField<DockPanel>(form, "dockPanel");
            StatusStrip statusStrip = GetField<StatusStrip>(form, "statusStrip");
            if (toolbar == null || dockPanel == null || statusStrip == null) return;

            int topHeight = Math.Max(44, toolbar.Font.Height + 22);
            int statusHeight = Math.Max(24, statusStrip.PreferredSize.Height);

            TableLayoutPanel chrome = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = form.BackColor
            };
            chrome.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            chrome.RowStyles.Add(new RowStyle(SizeType.Absolute, topHeight));
            chrome.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            chrome.RowStyles.Add(new RowStyle(SizeType.Absolute, statusHeight));

            form.SuspendLayout();
            chrome.SuspendLayout();
            try
            {
                form.Controls.Remove(toolbar);
                form.Controls.Remove(dockPanel);
                form.Controls.Remove(statusStrip);

                toolbar.Dock = DockStyle.Fill;
                toolbar.AutoSize = false;
                toolbar.Height = topHeight;
                toolbar.MinimumSize = new Size(0, topHeight);
                toolbar.Padding = new Padding(6, 5, 6, 5);
                toolbar.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;

                dockPanel.Dock = DockStyle.Fill;
                dockPanel.Margin = Padding.Empty;

                statusStrip.Dock = DockStyle.Fill;
                statusStrip.AutoSize = false;
                statusStrip.Height = statusHeight;
                statusStrip.SizingGrip = false;

                chrome.Controls.Add(toolbar, 0, 0);
                chrome.Controls.Add(dockPanel, 0, 1);
                chrome.Controls.Add(statusStrip, 0, 2);
                form.Controls.Add(chrome);
                chrome.BringToFront();
            }
            finally
            {
                chrome.ResumeLayout(true);
                form.ResumeLayout(true);
            }
        }

        private static T GetField<T>(object instance, string name) where T : class
        {
            try
            {
                FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                return field != null ? field.GetValue(instance) as T : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
