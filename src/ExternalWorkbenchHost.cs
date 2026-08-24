using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace KineticNapier.ADOFAIWorkbench
{
    internal static class ExternalWorkbenchHost
    {
        private static readonly object Gate = new object();
        private static readonly Queue<string> Pending = new Queue<string>();
        private static NamedPipeServerStream pipe;
        private static StreamReader reader;
        private static StreamWriter writer;
        private static Thread pipeThread;
        private static Process process;
        private static string pipeName;
        private static bool showRequested;

        internal static void ShowWindow()
        {
            showRequested = true;
            EnsureStarted();
            SendOrQueue("SHOW");
        }

        internal static void HideWindow()
        {
            showRequested = false;
            SendOrQueue("HIDE");
        }

        internal static void OpenPane(string id)
        {
            EnsureStarted();
            SendOrQueue("OPEN|" + Encode(id));
        }

        internal static void RegistryChanged()
        {
            EnsureStarted();
            SyncRegistry();
        }

        internal static void PublishPane(IDockablePane pane)
        {
            if (pane == null) return;
            try
            {
                WorkbenchPaneView view = pane.BuildView() ?? new WorkbenchPaneView();
                SendOrQueue("VIEW|" + Encode(pane.Id) + "|" + Encode(view.Serialize()));
            }
            catch (Exception ex)
            {
                Main.LogError("Failed to publish pane " + pane.Id, ex);
            }
        }

        private static void EnsureStarted()
        {
            lock (Gate)
            {
                if (process != null)
                {
                    try { if (!process.HasExited) return; }
                    catch { }
                    process = null;
                }

                string exe = Path.Combine(Main.ModDirectory ?? string.Empty, "ADOFAIWorkbench.Host.exe");
                if (!File.Exists(exe))
                {
                    Main.LogError("External Workbench host executable not found: " + exe, null);
                    return;
                }

                pipeName = "ADOFAIWorkbench_" + Process.GetCurrentProcess().Id + "_" + Guid.NewGuid().ToString("N");
                pipeThread = new Thread(PipeThreadMain)
                {
                    IsBackground = true,
                    Name = "ADOFAI Workbench IPC"
                };
                pipeThread.Start();

                var start = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "\"" + pipeName + "\"",
                    WorkingDirectory = Main.ModDirectory,
                    UseShellExecute = false
                };
                process = Process.Start(start);
                Main.Log("Started external Workbench host. PID=" + (process != null ? process.Id.ToString() : "?"));
            }
        }

        private static void PipeThreadMain()
        {
            try
            {
                using (NamedPipeServerStream server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None))
                {
                    Main.Log("Waiting for external Workbench host pipe connection...");
                    server.WaitForConnection();
                    lock (Gate)
                    {
                        pipe = server;
                        reader = new StreamReader(server, Encoding.UTF8, false, 4096, true);
                        writer = new StreamWriter(server, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                    }

                    Main.Log("External Workbench host connected.");
                    FlushPending();
                    SyncRegistry();
                    if (showRequested) SendOrQueue("SHOW");

                    string line;
                    while (server.IsConnected && (line = reader.ReadLine()) != null)
                        HandleHostMessage(line);
                }
            }
            catch (Exception ex)
            {
                Main.LogError("External Workbench IPC failed", ex);
            }
            finally
            {
                lock (Gate)
                {
                    pipe = null;
                    reader = null;
                    writer = null;
                }
            }
        }

        private static void HandleHostMessage(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string[] parts = line.Split('|');
            if (parts.Length >= 4 && string.Equals(parts[0], "ACTION", StringComparison.Ordinal))
            {
                Workbench.DispatchAction(Decode(parts[1]), Decode(parts[2]), Decode(parts[3]));
            }
            else if (parts.Length >= 2 && string.Equals(parts[0], "LOG", StringComparison.Ordinal))
            {
                Main.Log("Host: " + Decode(parts[1]));
            }
        }

        private static void SyncRegistry()
        {
            IList<IDockablePane> panes = Workbench.GetPanesSnapshot();
            SendOrQueue("SYNC_BEGIN");
            for (int i = 0; i < panes.Count; i++)
            {
                IDockablePane pane = panes[i];
                SendOrQueue("PANE|" + Encode(pane.Id) + "|" + Encode(pane.Title) + "|" + (pane.CanClose ? "1" : "0"));
                PublishPane(pane);
            }
            SendOrQueue("SYNC_END");
        }

        private static void SendOrQueue(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            lock (Gate)
            {
                if (writer == null)
                {
                    Pending.Enqueue(message);
                    return;
                }

                try { writer.WriteLine(message); }
                catch
                {
                    Pending.Enqueue(message);
                    writer = null;
                }
            }
        }

        private static void FlushPending()
        {
            lock (Gate)
            {
                if (writer == null) return;
                while (Pending.Count > 0)
                    writer.WriteLine(Pending.Dequeue());
            }
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
    }
}
