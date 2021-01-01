using System.Collections.Generic;
using Protobuf.Tcp;

namespace Tcp_Test.Server
{
    public class Lobby
    {
        public int id;
        public int host_id;
        public int capacity;
        public Dictionary<int, Tcp_Session> peers;

        public Lobby(int id, int host_id)
        {
            this.id = id;
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

        public IEnumerable<int> GetNonHostPeerIds()
        {
            foreach (var peer_id in peers.Keys)
            {
                if (peer_id != host_id)
                {
                    yield return peer_id;
                }
            }
        }

        public LobbyInfo GetInfo()
        {
            var info = new LobbyInfo
            {
                LobbyId = id,
                HostId = host_id,
                Capacity = capacity
            };
            info.PeerIds.AddRange(GetNonHostPeerIds());
            return info;
        }
    }
}