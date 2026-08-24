using System;
using System.Collections.Concurrent;
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
        // Unity/ADOFAI threads are only allowed to enqueue messages here. They never
        // touch a pipe, StreamWriter, WaitForConnection, or Process.Start directly.
        private static readonly ConcurrentQueue<string> Outbound = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent OutboundSignal = new AutoResetEvent(false);
        private static int workerRunning;
        private static volatile bool showRequested;
        private static volatile bool shutdownRequested;
        private static volatile bool hostReady;

        internal static void ShowWindow()
        {
            showRequested = true;
            EnsureStarted();
            QueueMessage("SHOW");
        }

        internal static void HideWindow()
        {
            showRequested = false;
            QueueMessage("HIDE");
        }

        internal static void OpenPane(string id)
        {
            EnsureStarted();
            QueueMessage("OPEN|" + Encode(id));
        }

        internal static void RegistryChanged()
        {
            EnsureStarted();
            QueueRegistrySnapshot();
        }

        internal static void PublishPane(IDockablePane pane)
        {
            if (pane == null) return;
            try
            {
                WorkbenchPaneView view = pane.BuildView() ?? new WorkbenchPaneView();
                QueueMessage("VIEW|" + Encode(pane.Id) + "|" + Encode(view.Serialize()));
            }
            catch (Exception ex)
            {
                Main.LogError("Failed to publish pane " + pane.Id, ex);
            }
        }

        internal static void Shutdown()
        {
            shutdownRequested = true;
            showRequested = false;
            QueueMessage("HIDE");
            OutboundSignal.Set();
        }

        private static void EnsureStarted()
        {
            if (shutdownRequested) return;
            if (Interlocked.CompareExchange(ref workerRunning, 1, 0) != 0) return;

            Thread worker = new Thread(WorkerMain)
            {
                IsBackground = true,
                Name = "ADOFAI Workbench IPC"
            };
            worker.Start();
        }

        private static void WorkerMain()
        {
            NamedPipeServerStream server = null;
            StreamReader reader = null;
            StreamWriter writer = null;
            Process hostProcess = null;
            volatileBox connected = new volatileBox();

            try
            {
                string exe = Path.Combine(Main.ModDirectory ?? string.Empty, "ADOFAIWorkbench.Host.exe");
                if (!File.Exists(exe))
                {
                    Main.LogError("External Workbench host executable not found: " + exe, null);
                    return;
                }

                string pipeName = "ADOFAIWorkbench_" + Process.GetCurrentProcess().Id + "_" + Guid.NewGuid().ToString("N");

                server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None);

                // Process creation is intentionally on this worker thread as well.
                var start = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "\"" + pipeName + "\"",
                    WorkingDirectory = Main.ModDirectory,
                    UseShellExecute = false
                };
                hostProcess = Process.Start(start);
                Main.Log("Started external Workbench host. PID=" + (hostProcess != null ? hostProcess.Id.ToString() : "?"));

                Main.Log("Waiting for external Workbench host pipe connection on IPC worker...");
                server.WaitForConnection();

                if (shutdownRequested) return;

                reader = new StreamReader(server, Encoding.UTF8, false, 4096, true);
                writer = new StreamWriter(server, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                connected.Value = true;
                Main.Log("External Workbench host connected.");

                StreamReader capturedReader = reader;
                Thread readerThread = new Thread(new ThreadStart(delegate { ReaderMain(capturedReader, connected); }))
                {
                    IsBackground = true,
                    Name = "ADOFAI Workbench IPC Reader"
                };
                readerThread.Start();

                // Send a provisional snapshot immediately. The host sends its
                // "Host connected" LOG only after its reader is running; HandleHostMessage
                // sends another authoritative snapshot at that point.
                QueueRegistrySnapshot();
                if (showRequested) QueueMessage("SHOW");

                while (connected.Value && !shutdownRequested)
                {
                    string message;
                    bool wroteAny = false;
                    while (Outbound.TryDequeue(out message))
                    {
                        wroteAny = true;
                        try
                        {
                            writer.WriteLine(message);
                        }
                        catch (Exception ex)
                        {
                            connected.Value = false;
                            Main.LogError("External Workbench IPC write failed", ex);
                            break;
                        }
                    }

                    if (!connected.Value || shutdownRequested) break;
                    if (!wroteAny) OutboundSignal.WaitOne(100);

                    if (hostProcess != null)
                    {
                        try
                        {
                            if (hostProcess.HasExited)
                            {
                                connected.Value = false;
                                Main.Log("External Workbench host exited.");
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!shutdownRequested)
                    Main.LogError("External Workbench IPC worker failed", ex);
            }
            finally
            {
                connected.Value = false;
                hostReady = false;
                try { if (reader != null) reader.Dispose(); } catch { }
                try { if (writer != null) writer.Dispose(); } catch { }
                try { if (server != null) server.Dispose(); } catch { }

                if (hostProcess != null)
                {
                    try
                    {
                        if (shutdownRequested && !hostProcess.HasExited)
                            hostProcess.Kill();
                    }
                    catch { }
                    try { hostProcess.Dispose(); } catch { }
                }

                Interlocked.Exchange(ref workerRunning, 0);
            }
        }

        private static void ReaderMain(StreamReader reader, volatileBox connected)
        {
            try
            {
                string line;
                while (!shutdownRequested && connected.Value && (line = reader.ReadLine()) != null)
                    HandleHostMessage(line);
            }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                if (!shutdownRequested)
                    Main.LogError("External Workbench IPC reader failed", ex);
            }
            finally
            {
                connected.Value = false;
                hostReady = false;
                OutboundSignal.Set();
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
                string message = Decode(parts[1]);
                Main.Log("Host: " + message);

                // Host emits this only after its pipe reader thread is running. Treat
                // it as the READY handshake and resend the complete pane registry.
                if (!hostReady && message.StartsWith("Host connected", StringComparison.OrdinalIgnoreCase))
                {
                    hostReady = true;
                    QueueRegistrySnapshot();
                    if (showRequested) QueueMessage("SHOW");
                }
            }
        }

        private static void QueueRegistrySnapshot()
        {
            IList<IDockablePane> panes = Workbench.GetPanesSnapshot();
            QueueMessage("SYNC_BEGIN");
            for (int i = 0; i < panes.Count; i++)
            {
                IDockablePane pane = panes[i];
                QueueMessage("PANE|" + Encode(pane.Id) + "|" + Encode(pane.Title) + "|" + (pane.CanClose ? "1" : "0"));
                PublishPane(pane);
            }
            QueueMessage("SYNC_END");
        }

        private static void QueueMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Outbound.Enqueue(message);
            OutboundSignal.Set();
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

        private sealed class volatileBox
        {
            public volatile bool Value;
        }
    }
}
