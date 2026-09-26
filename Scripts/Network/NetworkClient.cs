using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Godot;
using Goose2Client.Diagnostics;
using Goose2Client.Logs;
using Goose2Client.Network.Packets;

namespace Goose2Client.Network
{
    public class NetworkClient
    {
        public event Action<Exception>? ConnectionError;
        public event Action? Connected;
        public event Action<Exception>? SocketError;
        public event Action? Disconnected;

        public bool IsConnected => socket != null && socket.Connected;
        public bool Pause { get; set; } = false;
        public int PendingPacketCount => _packetInbox.Count;
        internal MainThreadStallMonitor? StallMonitor { get; set; }

        private Socket? socket;
        private string packetBuffer = "";

        private readonly PacketInbox _packetInbox = new();
        private Thread? recvThread;
        private volatile bool running;
        private readonly object _sendLock = new();

        public int DrainPackets(int maximum, Action<string> dispatch)
            => _packetInbox.Drain(maximum, dispatch);

        public int DrainPackets(int maximum, Func<bool> hasBudget, Action<string> dispatch)
            => _packetInbox.Drain(maximum, hasBudget, dispatch);

        public void Connect(string address, int port)
        {
            // Always tear down any prior connection first. This is unconditional (not gated on
            // recvThread.IsAlive) because a receive thread that exited on its own (e.g. the server
            // closed the connection -> Receive returned 0) leaves the old socket open; calling
            // Disconnect() here closes it and prevents a socket/FD leak across reconnects.
            // Disconnect() is idempotent and safe when there is nothing to tear down.
            Disconnect();

            try
            {
                packetBuffer = "";
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(address, port);

                running = true;
                recvThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "NetworkReceive" };
                recvThread.Start();

                Connected?.Invoke();   // Connect() is called on the main thread, so this is already main-thread; no marshaling needed
            }
            catch (Exception e)
            {
                ConnectionError?.Invoke(e);
            }
        }

        public void Disconnect()
        {
            running = false;   // signal the receive loop to stop
            try
            {
                if (socket != null && socket.Connected)
                    socket.Shutdown(SocketShutdown.Both);
            }
            catch { /* ignore shutdown errors */ }
            finally
            {
                socket?.Close();   // closing unblocks the blocking Receive in the thread
            }

            if (recvThread != null && recvThread.IsAlive)
                recvThread.Join(TimeSpan.FromSeconds(1));   // bounded wait; thread is background so it can't outlive the app
            recvThread = null;

            socket = null;
            packetBuffer = "";
            _packetInbox.Clear();
        }

        public void Send(string packet)
        {
            packet += '\x1';
            StallMonitor?.SetActivity("network-send wait-lock");
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(packet);
                lock (_sendLock)
                {
                    StallMonitor?.SetActivity("network-send socket");
                    socket!.Send(data);
                }
            }
            catch (Exception e)
            {
                SocketError?.Invoke(e);   // Send is called on the main thread
            }
            finally
            {
                StallMonitor?.SetActivity("network-send-complete");
            }
        }

        private void SendKeepalive()
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes("PONG\x1");
                lock (_sendLock)
                {
                    socket?.Send(data);
                }
            }
            catch { /* dead socket; Disconnected/SocketError surfaces it on the main thread */ }
        }

        private void ReceiveLoop()
        {
            var buffer = new byte[8192];
            try
            {
                while (running)
                {
                    int received = socket!.Receive(buffer);   // blocking; no Select(...,500) poll
                    if (received == 0)
                    {
                        if (running)
                            _packetInbox.WhenEmpty(() => Disconnected?.Invoke());
                        break;
                    }

                    packetBuffer += Encoding.ASCII.GetString(buffer, 0, received);
                    if (packetBuffer.Length == 0) continue;

                    string[] packets = packetBuffer.Split('\x1');
                    packetBuffer = packets[packets.Length - 1];   // keep the trailing incomplete fragment

                    for (int i = 0; i < packets.Length - 1; i++)
                    {
                        string packet = packets[i];
                        if (packet.StartsWith("PING"))
                        {
                            SendKeepalive();
                            continue;
                        }
                        _packetInbox.Enqueue(packet);
                    }
                }
            }
            catch (Exception e)
            {
                if (running)   // only surface errors that aren't from our own Disconnect()
                {
                    _packetInbox.WhenEmpty(() =>
                    {
                        GD.Print($"Network Exception: {e}");
                        SocketError?.Invoke(e);
                    });
                }
            }
        }

        // ===== Typed send helpers — copied verbatim from Unity source =====

        public void Login(string username, string password)
        {
            Send($"LOGIN{username},{password},GooseClient");
        }

        public void LoginContinued()
        {
            Send($"LCNT");
        }

        public void DoneLoadingMap()
        {
            Send($"DLM");
        }

        public void Move(Direction d)
        {
            Send($"M{(int)d + 1}");
        }

        public void Face(Direction d)
        {
            Send($"F{(int)d + 1}");
        }

        public void Attack()
        {
            Send($"ATT");
        }

        public void UseItem(int slot)
        {
            if (!CurrentMapFlags.Value.ItemsEnabled)
            {
                GameManager.Instance.Hud?.Chat?.AddChatLine("You can't use items in this map.", ChatType.Server);
                return;
            }

            Send($"USE{slot + 1}");
        }

        public void MoveItemInInventory(int fromSlot, int toSlot)
        {
            Send($"CHANGE{fromSlot + 1},{toSlot + 1}");
        }

        public void SplitStackInInventory(int fromSlot, int toSlot, int splitAmount)
        {
            Send($"SPLIT{fromSlot + 1},{toSlot + 1},{splitAmount}");
        }

        public void MoveInventoryToWindow(int fromSlot, int windowId, int toSlot)
        {
            Send($"ITW{fromSlot + 1},{windowId},{toSlot + 1}");
        }

        public void MoveWindowToInventory(int windowId, int fromSlot, int toSlot)
        {
            Send($"WTI{windowId},{fromSlot + 1},{toSlot + 1}");
        }

        public void MoveWindowToWindow(int fromWindowId, int fromSlot, int toWindowId, int toSlot)
        {
            Send($"WTW{fromWindowId},{fromSlot + 1},{toWindowId},{toSlot + 1}");
        }

        public void Drop(int fromSlot, int amount)
        {
            Send($"DRP{fromSlot + 1},{amount}");
        }

        public void Pickup()
        {
            Send($"GET");
        }

        public void MoveSpell(int fromSlot, int toSlot)
        {
            Send($"SWAP{fromSlot + 1},{toSlot + 1}");
        }

        public void CastSpell(int slot, int targetId)
        {
            Send($"CAST{slot + 1},{targetId}");
        }

        public void RequestSpellInfo(int slot)
        {
            Send($"SID{slot + 1}");
        }

        public void Quit()
        {
            Send($"QUIT");
        }

        public void KillBuff(int id)
        {
            Send($"KBUF{id}");
        }

        public void OpenCombineBag()
        {
            Send($"OCB");
        }

        public void WindowButtonClick(WindowButtons button, int windowId, int npcId, int unknownId1 = 0, int unknownId2 = 0)
        {
            Send($"WBC{(int)button},{windowId},{npcId},{unknownId1},{unknownId2}");
        }

        public bool TryLogQuery(LogQuerySubmission submission, out string error)
        {
            // UI validation restricts type ids to LMT-delivered types, so the count is bounded upstream.
            LogQueryFormatResult result = submission is LogQuerySubmission.Fresh fresh
                ? LogQueryPacket.Format(fresh, int.MaxValue)
                : LogQueryPacket.Format((LogQuerySubmission.Page)submission);
            if (!result.Success || result.Packet == null)
            {
                error = result.Error ?? "query is invalid";
                return false;
            }
            error = "";
            Send(result.Packet);
            return true;
        }

        public void CustomWindowSlots(int lookSlot, int statsSlot)
        {
            // lookSlot/statsSlot are 1-based server slot ids (0 = empty)
            Send(CustomWindowGraphicPacket.FormatCws(lookSlot, statsSlot));
        }

        public void CustomWindowCreate(int lookSlot, int statsSlot, int r, int g, int b, int a, string name)
        {
            Send(CustomWindowGraphicPacket.FormatCwc(lookSlot, statsSlot, r, g, b, a, name));
        }

        public void VendorPurchaseItem(int npcId, int slotId)
        {
            Send($"VPI{npcId},{slotId + 1}");
        }

        public void VendorSellItem(int npcId, int slotId, int stackSize)
        {
            Send($"VSI{npcId},{slotId + 1},{stackSize}");
        }

        public void LeftClick(int x, int y)
        {
            Send($"LC{x + 1},{y + 1}");
        }

        public void RightClick(int x, int y)
        {
            Send($"RC{x + 1},{y + 1}");
        }

        public void ChatMessage(string message)
        {
            Send($";{message}");
        }

        public void Command(string command)
        {
            Send(command);
        }

        public void Emote(int animationId, int graphicFile)
        {
            Send($"EMOT{animationId},{graphicFile}");
        }

        public void DestroyItem(int slotId)
        {
            Send($"DITM{slotId + 1}");
        }

        public void DestroySpell(int slotId)
        {
            Send($"DSPL{slotId + 1}");
        }
    }
}
