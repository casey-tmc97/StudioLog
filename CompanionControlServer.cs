using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StudioLog.Core
{
    /// <summary>
    /// TCP transport for Bitfocus Companion control. Parses incoming command
    /// lines into events and broadcasts state lines to all connected clients.
    /// Wire protocol: newline-terminated ASCII lines. See
    /// docs/superpowers/specs/2026-07-06-companion-module-design.md.
    /// </summary>
    public class CompanionControlServer : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly List<TcpClient> _clients = new();
        private readonly object _clientsLock = new();
        private readonly object _writeLock = new();
        private bool _disposed;

        public event Action? GenerateToggleRequested;
        public event Action? TimeCodeInRequested;
        public event Action? TimeCodeOutRequested;
        public event Action? MarkRequested;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Called once per newly-accepted client to fetch current state so
        /// it can be sent immediately, before any state actually changes.
        /// </summary>
        public Func<(bool generatorRunning, bool tcInActive, string timecode)>? GetCurrentState { get; set; }

        public void Start(int port)
        {
            if (IsRunning) Stop();

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            IsRunning = true;

            _ = AcceptLoopAsync(_cts.Token);
            Console.WriteLine($"[CompanionControlServer] Listening on port {port}");
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;

            lock (_clientsLock)
            {
                foreach (var client in _clients)
                {
                    try { client.Close(); } catch { }
                }
                _clients.Clear();
            }

            IsRunning = false;
            Console.WriteLine("[CompanionControlServer] Stopped");
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _listener != null)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);

                    lock (_clientsLock)
                    {
                        _clients.Add(client);
                    }

                    var state = GetCurrentState?.Invoke();
                    if (state != null)
                    {
                        SendToClient(client, $"STATE GENERATOR {(state.Value.generatorRunning ? "RUNNING" : "STOPPED")}");
                        SendToClient(client, $"STATE TC_IN_ACTIVE {(state.Value.tcInActive ? "TRUE" : "FALSE")}");
                        SendToClient(client, $"STATE TIMECODE {state.Value.timecode}");
                    }

                    _ = HandleClientAsync(client, token);
                }
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                // Expected on Stop()
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CompanionControlServer] Accept loop error: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new System.IO.StreamReader(stream, Encoding.ASCII);

                while (!token.IsCancellationRequested && client.Connected)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (line == null) break;

                    DispatchCommand(line.Trim());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CompanionControlServer] Client error: {ex.Message}");
            }
            finally
            {
                lock (_clientsLock)
                {
                    _clients.Remove(client);
                }
                try { client.Close(); } catch { }
            }
        }

        private void DispatchCommand(string command)
        {
            switch (command)
            {
                case "GENERATE_TOGGLE":
                    GenerateToggleRequested?.Invoke();
                    break;
                case "TC_IN":
                    TimeCodeInRequested?.Invoke();
                    break;
                case "TC_OUT":
                    TimeCodeOutRequested?.Invoke();
                    break;
                case "MARK":
                    MarkRequested?.Invoke();
                    break;
                default:
                    Console.WriteLine($"[CompanionControlServer] Unknown command: {command}");
                    break;
            }
        }

        public void BroadcastGeneratorState(bool running) =>
            Broadcast($"STATE GENERATOR {(running ? "RUNNING" : "STOPPED")}");

        public void BroadcastTimecodeInActive(bool active) =>
            Broadcast($"STATE TC_IN_ACTIVE {(active ? "TRUE" : "FALSE")}");

        public void BroadcastTimecode(string timecode) =>
            Broadcast($"STATE TIMECODE {timecode}");

        private void Broadcast(string message)
        {
            List<TcpClient> snapshot;
            lock (_clientsLock)
            {
                snapshot = new List<TcpClient>(_clients);
            }

            foreach (var client in snapshot)
            {
                SendToClient(client, message);
            }
        }

        private void SendToClient(TcpClient client, string message)
        {
            try
            {
                if (!client.Connected) return;
                var data = Encoding.ASCII.GetBytes(message + "\n");
                lock (_writeLock)
                {
                    client.GetStream().Write(data, 0, data.Length);
                }
            }
            catch
            {
                // Client will be cleaned up by its own read loop on next iteration.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }
    }
}
