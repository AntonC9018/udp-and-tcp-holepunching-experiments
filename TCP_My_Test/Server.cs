using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetProtobuf;
using Google.Protobuf;
using static NetProtobuf.Tcp_WithoutRoomRequest.MessageOneofCase;
using static NetProtobuf.Tcp_WithinRoomRequest.MessageOneofCase;
using static NetProtobuf.Tcp_WithinRoomResponse.Types;

namespace Tcp_Test
{
    public class Server
    {
        // First, the clients would establish a tcp connection. This connection stays.
        // As they do that, they would send the server their private ip address.
        // The server would find out their public address based on where the request came from. 
        public Dictionary<int, Tcp_Session> sessions;
        public int currentId;
        public Dictionary<int, Room> rooms;

        public TcpListener listener;

        public Server(int port)
        {
            rooms = new Dictionary<int, Room>();
            sessions = new Dictionary<int, Tcp_Session>();
            listener = new TcpListener(IPAddress.Any, port);
        }

        public void Start()
        {
            Thread removeDisconnected = new Thread(RemoveDisconnected);

            try
            {
                removeDisconnected.Start();
                listener.Start();

                while (true)
                {
                    var client = listener.AcceptTcpClient();
                    int clientId = ++currentId;
                    var session = new Tcp_Session(++currentId, client);
                    new Thread(() => session.Start(this)).Start();
                }
            }
            catch (System.Exception e)
            {
                System.Console.WriteLine($"Server error: {e}");
            }
            finally
            {
                removeDisconnected.Abort();
            }
        }

        private List<int> indexBuffer = new List<int>();
        private const int removeDisconnectedInterval = 1000;
        public void RemoveDisconnected()
        {
            while (true)
            {
                lock (sessions)
                {
                    foreach (var id in sessions.Keys)
                    {
                        if (!sessions[id].client.Connected)
                        {
                            indexBuffer.Add(id);
                        }
                    }
                    foreach (int id in indexBuffer)
                    {
                        sessions[id].Log($"Tcp session has been discontinued.");
                        sessions.Remove(id);
                    }
                }
                indexBuffer.Clear();
                Thread.Sleep(removeDisconnectedInterval);
            }
        }
    }

    public class Tcp_Session
    {
        public int id;
        public TcpClient client;
        public IPEndPoint private_endpoint;
        public IPEndPoint public_endpoint;
        public State state;

        public enum State
        {
            Initializing, WithoutRoom, WithinRoom, Exiting
        }

        public bool IsInitalized => private_endpoint != null;
        public bool CanBeUsed => IsInitalized && client.Connected;


        public Tcp_Session(int id, TcpClient client)
        {
            this.id = id;
            this.client = client;
            this.state = State.Initializing;
        }

        public void Log(string str)
        {
            System.Console.WriteLine($"{public_endpoint} | {str}");
        }

        public void Start(Server server)
        {
            try
            {
                Initialize();
                state = State.WithoutRoom;
                while (state != State.Exiting)
                {
                    switch (state)
                    {
                        case State.WithoutRoom:
                            ListenForWithoutRoomRequests(server);
                            break;
                        case State.WithinRoom:
                            ListenForWithinRoomRequests(server);
                            break;
                    }
                }
            }
            catch (System.Exception e)
            {
                Log($"Session prematurely ended due to the exception: {e}");
            }

            if (client.Connected)
            {
                client.Close();
            }
        }

        public void Initialize()
        {
            NetworkStream stream = client.GetStream();
            public_endpoint = (IPEndPoint)client.Client.RemoteEndPoint;

            Log($"Session initialization started.");

            var infoMessage = InfoMessage.Parser.ParseFrom(stream);
            private_endpoint = infoMessage.LocalEndpoint.Convert();

            Log($"Session established. Peer's private endpoint: {private_endpoint}");
        }

        public void ListenForWithoutRoomRequests(Server server)
        {
            NetworkStream stream = client.GetStream();
            Tcp_WithoutRoomResponse response = new Tcp_WithoutRoomResponse();

            while (state == State.WithinRoom)
            {
                Tcp_WithoutRoomRequest request = Tcp_WithoutRoomRequest.Parser.ParseFrom(stream);
                switch (request.MessageCase)
                {
                    case Ping:
                        Log($"Pinging with 0x{request.Ping.Data:X}");
                        continue;
                    // this is more concise and readable, but also more susceptible to change
                    // if I add a switch on 
                    case CreateRoomRequest:
                        var room = new Room(id);
                        response.Success = server.rooms.TryAdd(id, room) && room.TryJoin(this);
                        break;
                    case JoinRoomRequest:
                        response.Success = server.rooms.TryGetValue(id, out room) && room.TryJoin(this);
                        break;
                }
                response.WriteTo(stream);
                if (response.Success)
                {
                    state = State.WithinRoom;
                }
            }
        }

        public void ListenForWithinRoomRequests(Server server)
        {
            NetworkStream stream = client.GetStream();

            while (state == State.WithinRoom)
            {
                Tcp_WithinRoomRequest request = Tcp_WithinRoomRequest.Parser.ParseFrom(stream);
                switch (request.MessageCase)
                {
                    case LeaveRequest:
                        if (server.rooms.TryGetValue(id, out Room room))
                        {
                            room.peers.Remove(id);
                            if (room.peers.Count == 0)
                            {
                                server.rooms.Remove(id);
                            }
                            var ack = new LeaveRoomAck();
                            ack.WriteTo(stream);
                            state = State.WithoutRoom;
                        }
                        break;
                    case StartRequest:
                        if (server.rooms.TryGetValue(id, out room) && id == room.host_id)
                        {
                            var host_response = new StartRoomResponseHost();

                            var host_address_message = CreateAddressMessage();

                            // TODO: this should be in a separate thing
                            // Also, stop listening on socket and change state for that session on server
                            var peer_notification = new StartRoomResponsePeer
                            {
                                Host = host_address_message
                            };

                            var response = new Tcp_WithinRoomResponse
                            {
                                StartRoomResponePeer = peer_notification // whatever
                            };

                            foreach (var peer_id in room.peers.Keys)
                            {
                                if (peer_id != id)
                                {
                                    var peer = room.peers[peer_id];
                                    try
                                    {
                                        peer_notification.WriteTo(peer.client.GetStream());
                                        host_response.Peers.Add(peer.CreateAddressMessage());
                                    }
                                    catch
                                    {
                                        peer.Log($"An error has been catched while trying to send PeerInfo");
                                    }
                                }
                            }
                            host_response.WriteTo(stream);
                            // also change state probably to room running or something
                            // delete or lock the room
                        }
                        break;
                }
            }
        }

        public PeerAddressMessage CreateAddressMessage()
        {
            return new PeerAddressMessage
            {
                Id = id,
                LocalEndpoint = private_endpoint.Convert(),
                PublicEndpoint = public_endpoint.Convert()
            };
        }
    }

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