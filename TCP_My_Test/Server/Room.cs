using System.Collections.Generic;

namespace Tcp_Test.Server
{
    public class Room
    {
        public int host_id;
        public int capacity;
        public Dictionary<int, Tcp_Session> peers;

        public Room(int host_id)
        {
            this.host_id = host_id;
            this.capacity = 2;
            peers = new Dictionary<int, Tcp_Session>();
        }

        public bool TryJoin(Tcp_Session session)
        {
            if (peers.Count >= capacity)
            {
                return false;
            }
            return peers.TryAdd(session.id, session);
        }
    }
}