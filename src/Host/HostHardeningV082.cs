using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace KineticNapier.ADOFAIWorkbench.Host
{
    internal static class HostHardeningV082
    {
        private static bool loggingInstalled;

        internal static void InstallGlobalExceptionLogging()
        {
            if (loggingInstalled) return;
            loggingInstalled = true;

            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); } catch { }

            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                WriteCrashLog("WinForms UI thread exception", e != null ? e.Exception : null);
            };

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception exception = e != null ? e.ExceptionObject as Exception : null;
                WriteCrashLog("Unhandled host exception" + (e != null && e.IsTerminating ? " (terminating)" : string.Empty), exception);
            };
        }

        internal static void InstallFormHardening(TcpHostForm form)
        {
            if (form == null) return;

            // A nested AutoSize FlowLayoutPanel can be measured before its TextBox
            // children are added. With the default GrowOnly + wrapping behavior the
            // row can then keep the width of its first Label and clip the TextBox and
            // every control after it. Button-only rows usually expand, which made the
            // bug look input-specific.
            //
            // Re-check after message processing so newly rebuilt pane rows are fixed
            // without touching the pane renderer while it is mutating Controls.
            Timer timer = new Timer { Interval = 100 };
            timer.Tick += delegate
            {
                if (form.IsDisposed)
                {
                    timer.Stop();
                    timer.Dispose();
                    return;
                }

                try { FixPaneLayouts(form); }
                catch (Exception ex) { WriteCrashLog("Failed to normalize pane row layout", ex); }
            };
            form.FormClosed += delegate
            {
                try { timer.Stop(); timer.Dispose(); } catch { }
            };
            timer.Start();
        }

        private static void FixPaneLayouts(Control control)
        {
            if (control == null || control.IsDisposed) return;

            // Walk children first so PreferredSize is based on already-normalized
            // descendants when this container is measured.
            for (int i = 0; i < control.Controls.Count; i++)
                FixPaneLayouts(control.Controls[i]);

            FlowLayoutPanel row = control as FlowLayoutPanel;
            FlowLayoutPanel parentFlow = control.Parent as FlowLayoutPanel;
            if (row == null || parentFlow == null || row.FlowDirection != FlowDirection.LeftToRight) return;

            bool changed = false;
            if (!row.AutoSize)
            {
                row.AutoSize = true;
                changed = true;
            }
            if (row.AutoSizeMode != AutoSizeMode.GrowAndShrink)
            {
                row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                changed = true;
            }
            if (row.WrapContents)
            {
                // Keep one logical Workbench row on one physical line. If it is wider
                // than the pane, the outer AutoScroll panel can scroll horizontally;
                // silently clipping controls is never acceptable.
                row.WrapContents = false;
                changed = true;
            }
            if (row.MinimumSize.Height < 36)
            {
                row.MinimumSize = new Size(row.MinimumSize.Width, 36);
                changed = true;
            }

            if (changed)
            {
                row.PerformLayout();
                parentFlow.PerformLayout();
                row.Invalidate(true);
            }
        }

        internal static void WriteCrashLog(string context, Exception exception)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ADOFAIWorkbench");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "host-error.log");

                using (StreamWriter writer = new StreamWriter(path, true))
                {
                    writer.WriteLine("============================================================");
                    writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    writer.WriteLine(context ?? "Host error");
                    if (exception != null) writer.WriteLine(exception.ToString());
                    else writer.WriteLine("(no exception object)");
                }
            }
            catch { }
        }
    }
}
