using System;
using System.Collections;
using System.Collections.Generic;

namespace Goose2Client.Network
{
    public abstract class PacketHandler
    {
        public List<Action<object>> Observers = new();

        public virtual void CallObservers(object obj)
        {
            for (int i = 0; i < Observers.Count; i++)
            {
                Observers[i].Invoke(obj);
            }
        }
    }
}
