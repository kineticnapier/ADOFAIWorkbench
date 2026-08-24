using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace KineticNapier.ADOFAIWorkbench
{
    internal static class ExternalWorkbenchHost
    {
        // Unity/ADOFAI threads only mutate local state or enqueue messages here.
        // Process creation and every socket operation live exclusively on background
        // worker threads so a broken Workbench can never stall the Unity main thread.
        private static readonly ConcurrentQueue<string> Outbound = new ConcurrentQueue<string>();
        private static readonly ConcurrentQueue<string> OpenRequests = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent Signal = new AutoResetEvent(false);

        private static int workerRunning;
        private static volatile bool showRequested;
        private static volatile bool shutdownRequested;
        private static volatile bool hostReady;
        private static volatile bool registryDirty = true;

        internal static void ShowWindow()
        {
            showRequested = true;
            EnsureStarted();
            if (hostReady) QueueMessage("SHOW");
        }

        internal static void HideWindow()
        {
            showRequested = false;
            if (hostReady) QueueMessage("HIDE");
        }

        internal static void OpenPane(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            OpenRequests.Enqueue(id);
            EnsureStarted();
            Signal.Set();
        }

        internal static void RegistryChanged()
        {
            registryDirty = true;
            EnsureStarted();
            Signal.Set();
        }

        internal static void PublishPane(IDockablePane pane)
        {
            if (pane == null) return;
            if (!hostReady)
            {
                registryDirty = true;
                return;
            }

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
            Signal.Set();
        }

        private static void EnsureStarted()
        {
            if (shutdownRequested) return;
            if (Interlocked.CompareExchange(ref workerRunning, 1, 0) != 0) return;

            Thread worker = new Thread(new ThreadStart(WorkerMain))
            {
                IsBackground = true,
                Name = "ADOFAI Workbench TCP IPC"
            };
            worker.Start();
        }

        private static void WorkerMain()
        {
            TcpListener listener = null;
            TcpClient client = null;
            NetworkStream stream = null;
            StreamReader reader = null;
            StreamWriter writer = null;
            Process hostProcess = null;
            ConnectionState connection = new ConnectionState();

            try
            {
                string exe = Path.Combine(Main.ModDirectory ?? string.Empty, "ADOFAIWorkbench.Host.exe");
                if (!File.Exists(exe))
                {
                    Main.LogError("External Workbench host executable not found: " + exe, null);
                    return;
                }

                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(1);
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                int parentPid = Process.GetCurrentProcess().Id;

                var start = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = port.ToString() + " \"" + token + "\" " + parentPid.ToString(),
                    WorkingDirectory = Main.ModDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                hostProcess = Process.Start(start);
                Main.Log("Started external Workbench TCP host. PID=" +
                    (hostProcess != null ? hostProcess.Id.ToString() : "?") +
                    " port=" + port.ToString());

                // Poll instead of blocking in AcceptTcpClient so shutdown can interrupt
                // connection setup without touching the listener from Unity's thread.
                while (!shutdownRequested && !listener.Pending())
                {
                    if (hostProcess != null)
                    {
                        try
                        {
                            if (hostProcess.HasExited)
                                throw new InvalidOperationException("Workbench host exited before connecting.");
                        }
                        catch (InvalidOperationException) { throw; }
                        catch { }
                    }
                    Thread.Sleep(25);
                }

                if (shutdownRequested) return;

                client = listener.AcceptTcpClient();
                client.NoDelay = true;
                stream = client.GetStream();
                reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

                string hello = reader.ReadLine();
                string expectedHello = "HELLO|" + token;
                if (!string.Equals(hello, expectedHello, StringComparison.Ordinal))
                    throw new InvalidDataException("Workbench host authentication failed.");

                connection.Connected = true;
                hostReady = true;
                registryDirty = true;
                Main.Log("External Workbench TCP host connected.");

                StreamReader capturedReader = reader;
                Thread readerThread = new Thread(new ThreadStart(delegate
                {
                    ReaderMain(capturedReader, connection);
                }))
                {
                    IsBackground = true,
                    Name = "ADOFAI Workbench TCP Reader"
                };
                readerThread.Start();

                while (connection.Connected && !shutdownRequested)
                {
                    bool wroteAny = false;

                    if (registryDirty)
                    {
                        registryDirty = false;
                        SendRegistrySnapshot(writer);
                        wroteAny = true;
                    }

                    string openId;
                    while (OpenRequests.TryDequeue(out openId))
                    {
                        writer.WriteLine("OPEN|" + Encode(openId));
                        wroteAny = true;
                    }

                    string message;
                    while (Outbound.TryDequeue(out message))
                    {
                        writer.WriteLine(message);
                        wroteAny = true;
                    }

                    if (showRequested && !connection.ShowSent)
                    {
                        writer.WriteLine("SHOW");
                        connection.ShowSent = true;
                        wroteAny = true;
                    }
                    else if (!showRequested && connection.ShowSent)
                    {
                        writer.WriteLine("HIDE");
                        connection.ShowSent = false;
                        wroteAny = true;
                    }

                    if (!wroteAny) Signal.WaitOne(50);

                    if (hostProcess != null)
                    {
                        try
                        {
                            if (hostProcess.HasExited)
                            {
                                connection.Connected = false;
                                Main.Log("External Workbench host exited.");
                            }
                        }
                        catch { }
                    }
                }

                if (shutdownRequested && connection.Connected)
                {
                    try { writer.WriteLine("EXIT"); } catch { }
                }
            }
            catch (Exception ex)
            {
                if (!shutdownRequested)
                    Main.LogError("External Workbench TCP IPC failed", ex);
            }
            finally
            {
                connection.Connected = false;
                hostReady = false;

                try { if (reader != null) reader.Dispose(); } catch { }
                try { if (writer != null) writer.Dispose(); } catch { }
                try { if (stream != null) stream.Dispose(); } catch { }
                try { if (client != null) client.Close(); } catch { }
                try { if (listener != null) listener.Stop(); } catch { }

                if (hostProcess != null)
                {
                    try
                    {
                        if (shutdownRequested && !hostProcess.HasExited)
                        {
                            if (!hostProcess.WaitForExit(250)) hostProcess.Kill();
                        }
                    }
                    catch { }
                    try { hostProcess.Dispose(); } catch { }
                }

                Interlocked.Exchange(ref workerRunning, 0);
            }
        }

        private static void ReaderMain(StreamReader reader, ConnectionState connection)
        {
            try
            {
                string line;
                while (!shutdownRequested && connection.Connected && (line = reader.ReadLine()) != null)
                    HandleHostMessage(line);
            }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                if (!shutdownRequested)
                    Main.LogError("External Workbench TCP reader failed", ex);
            }
            finally
            {
                connection.Connected = false;
                hostReady = false;
                Signal.Set();
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

        private static void SendRegistrySnapshot(StreamWriter writer)
        {
            IList<IDockablePane> panes = Workbench.GetPanesSnapshot();
            writer.WriteLine("SYNC_BEGIN");

            for (int i = 0; i < panes.Count; i++)
            {
                IDockablePane pane = panes[i];
                writer.WriteLine("PANE|" + Encode(pane.Id) + "|" + Encode(pane.Title) + "|" +
                    (pane.CanClose ? "1" : "0"));

                try
                {
                    WorkbenchPaneView view = pane.BuildView() ?? new WorkbenchPaneView();
                    writer.WriteLine("VIEW|" + Encode(pane.Id) + "|" + Encode(view.Serialize()));
                }
                catch (Exception ex)
                {
                    Main.LogError("Failed to build pane snapshot " + pane.Id, ex);
                }
            }

            writer.WriteLine("SYNC_END");
            Main.Log("Sent Workbench registry snapshot. Panes=" + panes.Count.ToString());
        }

        private static void QueueMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Outbound.Enqueue(message);
            Signal.Set();
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

        private sealed class ConnectionState
        {
            public volatile bool Connected;
            public bool ShowSent;
        }
    }
}
