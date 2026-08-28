using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace KineticNapier.ADOFAIWorkbench.Host
{
    internal static class HostHardeningV082
    {
        private const string WelcomePaneId = "workbench.welcome";
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
            DockPanel dockPanel = GetField<DockPanel>(form, "dockPanel");
            if (dockPanel == null) return;

            // Do not mutate DockPanel from ActiveContentChanged/ActiveDocumentChanged.
            // Those events are raised while DockPanel Suite is changing docking state;
            // 0.8.2 could therefore re-enter DockPanel by calling Show(Document) while
            // MTE panes were opening as the editor scene came up.
            //
            // A WinForms timer runs only after the current message has completed, so
            // it is a safe place to apply static constraints to the Welcome document.
            Timer timer = new Timer { Interval = 100 };
            timer.Tick += delegate
            {
                if (form.IsDisposed)
                {
                    timer.Stop();
                    timer.Dispose();
                    return;
                }
                RestrictWelcomePane(dockPanel);
            };
            form.FormClosed += delegate
            {
                try { timer.Stop(); timer.Dispose(); } catch { }
            };
            timer.Start();
        }

        private static void RestrictWelcomePane(DockPanel dockPanel)
        {
            if (dockPanel == null) return;
            try
            {
                for (int i = 0; i < dockPanel.Contents.Count; i++)
                {
                    DockContent content = dockPanel.Contents[i] as DockContent;
                    if (content == null || !IsWelcomePane(content)) continue;

                    content.AllowEndUserDocking = false;
                    content.CloseButton = false;
                    content.CloseButtonVisible = false;

                    // Never force a dock-state transition here. If an old saved layout
                    // already has Welcome floating, leave it there but make it immovable.
                    // New layouts create it as Document, where Document-only is safe.
                    if (content.DockState == DockState.Document)
                        content.DockAreas = DockAreas.Document;
                }
            }
            catch (Exception ex)
            {
                WriteCrashLog("Failed to apply Welcome pane constraints", ex);
            }
        }

        private static bool IsWelcomePane(DockContent content)
        {
            try
            {
                FieldInfo field = content.GetType().GetField("paneId", BindingFlags.Instance | BindingFlags.NonPublic);
                string id = field != null ? field.GetValue(content) as string : null;
                return string.Equals(id, WelcomePaneId, StringComparison.Ordinal);
            }
            catch
            {
                return false;
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
