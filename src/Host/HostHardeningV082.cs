using System;
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
            // Intentionally no DockPanel event hooks here.
            //
            // 0.8.2 tried to force the Welcome pane back to Document state from
            // ActiveContentChanged / ActiveDocumentChanged. Those events fire while
            // DockPanel Suite is mutating its own docking state, so initiating another
            // Show/Dock operation from inside them can re-enter DockPanel internals.
            // Pane-specific docking constraints are now applied once, when the pane
            // content is constructed, before it is ever shown.
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
