using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Godot;

namespace Goose2Client.Network
{
    public class NetworkClient
    {
        public event Action<Exception>? ConnectionError;
        public event Action? Connected;
        public event Action<Exception>? SocketError;

        public bool IsConnected => socket != null && socket.Connected;
        public bool Pause { get; set; } = false;

        private Socket? socket;
        private string packetBuffer = "";

        private readonly Node dispatcher;   // GameManager autoload Node, used only for thread-safe CallDeferred
        private Thread? recvThread;
        private volatile bool running;

        public NetworkClient(Node dispatcher)
        {
            this.dispatcher = dispatcher;
        }

        public void Connect(string address, int port)
        {
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
        }

        public void Send(string packet)
        {
            packet += '\x1';
            try
            {
                socket!.Send(Encoding.ASCII.GetBytes(packet));
            }
            catch (Exception e)
            {
                SocketError?.Invoke(e);   // Send is called on the main thread
            }
        }

        // Background thread: touches ONLY the socket and packetBuffer. Everything that reaches
        // PacketManager/observers/scene-tree is marshaled to the main thread via CallDeferred.
        private void ReceiveLoop()
        {
            var buffer = new byte[8192];
            try
            {
                while (running)
                {
                    int received = socket!.Receive(buffer);   // blocking; no Select(...,500) poll
                    if (received == 0) break;                 // remote closed the connection

                    packetBuffer += Encoding.ASCII.GetString(buffer, 0, received);
                    if (packetBuffer.Length == 0) continue;

                    string[] packets = packetBuffer.Split('\x1');
                    packetBuffer = packets[packets.Length - 1];   // keep the trailing incomplete fragment

                    // Dispatch every complete packet to the main thread. The Pause flag is honored
                    // in GameManager.HandlePacket (main thread), NOT here.
                    for (int i = 0; i < packets.Length - 1; i++)
                    {
                        dispatcher.CallDeferred("HandlePacket", packets[i]);
                    }
                }
            }
            catch (Exception e)
            {
                if (running)   // only surface errors that aren't from our own Disconnect()
                {
                    GD.Print($"Network Exception: {e}");
                    // marshal the error event to the main thread
                    Callable.From(() => SocketError?.Invoke(e)).CallDeferred();
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

        public void Pong()
        {
            Send($"PONG");
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
