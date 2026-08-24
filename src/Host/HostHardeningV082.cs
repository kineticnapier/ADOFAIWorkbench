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
        private static bool restrictingWelcome;

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

            EventHandler refresh = delegate
            {
                RestrictWelcomePane(dockPanel);
            };

            dockPanel.ActiveContentChanged += refresh;
            dockPanel.ActiveDocumentChanged += refresh;
            form.Shown += delegate { RestrictWelcomePane(dockPanel); };
            form.Activated += delegate { RestrictWelcomePane(dockPanel); };

            RestrictWelcomePane(dockPanel);
        }

        private static void RestrictWelcomePane(DockPanel dockPanel)
        {
            if (dockPanel == null || restrictingWelcome) return;
            restrictingWelcome = true;
            try
            {
                for (int i = 0; i < dockPanel.Contents.Count; i++)
                {
                    DockContent content = dockPanel.Contents[i] as DockContent;
                    if (content == null || !IsWelcomePane(content)) continue;

                    // Welcome is a landing/document page, not a tool pane. Keeping it
                    // out of the drag/dock machinery avoids the crash path reported
                    // when dragging the non-closeable welcome document.
                    content.AllowEndUserDocking = false;
                    content.CloseButton = false;
                    content.CloseButtonVisible = false;

                    if (content.DockState != DockState.Document && content.DockPanel == dockPanel)
                    {
                        try { content.Show(dockPanel, DockState.Document); }
                        catch (Exception ex) { WriteCrashLog("Failed to restore Welcome to document state", ex); }
                    }

                    try { content.DockAreas = DockAreas.Document; }
                    catch (Exception ex) { WriteCrashLog("Failed to restrict Welcome dock areas", ex); }
                }
            }
            catch (Exception ex)
            {
                WriteCrashLog("Failed while hardening Welcome pane", ex);
            }
            finally
            {
                restrictingWelcome = false;
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
