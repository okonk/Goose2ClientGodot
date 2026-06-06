using System;
using System.Collections.Generic;
using Godot;

namespace Goose2Client.Network
{
    public class PacketManager
    {
        private Dictionary<string, PacketHandler> handlers = new Dictionary<string, PacketHandler>();
        private Dictionary<Type, PacketHandler> typeToHandler = new Dictionary<Type, PacketHandler>();

        public void Listen<T>(Action<object> callback) where T : PacketHandler, new()
        {
            if (!typeToHandler.TryGetValue(typeof(T), out var handler))
            {
                handler = new T();
                handlers[handler.Prefix] = handler;
                typeToHandler[typeof(T)] = handler;
            }

            handler.Observers.Add(callback);
        }

        public void Remove<T>(Action<object> callback) where T : PacketHandler, new()
        {
            typeToHandler[typeof(T)].Observers.Remove(callback);
        }

        public void Clear()
        {
            handlers.Clear();
            typeToHandler.Clear();
        }

        public void Handle(string packet)
        {
            if (packet.Length == 0) return;

            for (int i = 0; i < Math.Min(8, packet.Length); i++)
            {
                if (handlers.TryGetValue(packet.Substring(0, i + 1), out PacketHandler handler))
                {
                    object obj;
                    try
                    {
                        obj = handler.Parse(new PacketParser(packet, handler.Prefix));
                    }
                    catch (Exception e)
                    {
                        GD.Print($"Exception handling packet '{packet}': {e}");
                        return;
                    }

                    handler.CallObservers(obj);

                    return;
                }
            }

            //GD.Print($"Can't handle packet: {packet}");
        }
    }
}
