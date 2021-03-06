using System.Net.Sockets;
using Protobuf.Tcp;
using Google.Protobuf;
using System;
using System.Net;
using static Protobuf.Tcp.WithoutLobbyRequest.Types;
using System.Threading;
using System.Threading.Tasks;
using static Protobuf.Tcp.PeerWithinLobbyResponse.MessageOneofCase;
using static Protobuf.Tcp.HostWithinLobbyResponse.MessageOneofCase;
using System.Linq;
using System.Collections.Generic;
using static Protobuf.Tcp.HostWithinLobbyRequest.Types;

namespace Tcp_Test.Client
{
    public class Client
    {
        public int id;
        public Socket client;
        public NetworkStream stream;
        public LobbyInfo joined_lobby;
        public System.Net.IPEndPoint server_endpoint;
        public IPEndPointMessage private_endpoint;
        public Tcp_State state;

        private static System.Net.IPAddress GetLocalIp()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip;
                }
            }
            throw new System.Exception("Failed to get local IP");
        }

        public Socket CreateSocket()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
#if (LINUX)
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseUnicastPort, 1);
#endif
            return socket;
        }

        public Client(IPEndPoint server_endpoint)
        {
            this.server_endpoint = server_endpoint;
            this.client = CreateSocket();
            this.id = new Random().Next();
            this.state = Tcp_State.Connecting;
        }

        public void ReuseEndPoint(EndPoint endPoint)
        {
            client.Bind(endPoint);
        }

        public InitializationResponse ConnectToServer()
        {
            client.Connect(server_endpoint);

            this.private_endpoint = ((IPEndPoint)client.LocalEndPoint).Convert();

            System.Console.WriteLine($"{client.LocalEndPoint}");
            System.Console.WriteLine($"Local endpoint: {private_endpoint.GetAddress()}:{private_endpoint.Port}");

            this.state = Tcp_State.Initialization;
            this.stream = new NetworkStream(client);

            InitializationRequest info = new InitializationRequest
            {
                ClientId = id,
                PrivateEndpoint = private_endpoint
            };

            info.WriteDelimitedTo(stream);

            var response = InitializationResponse.Parser.ParseDelimitedFrom(stream);
            this.state = Tcp_State.WithoutLobby;

            return response;
        }

        public bool TryJoinLobby(int id, string password)
        {
            var request = new WithoutLobbyRequest();
            var message = new JoinLobbyRequest();

            message.Password = password;
            message.LobbyId = id;
            request.JoinLobbyRequest = message;

            System.Console.WriteLine($"Joining lobby {id}");

            request.WriteDelimitedTo(stream);

            var response = JoinLobbyResponse.Parser.ParseDelimitedFrom(stream);

            if (response.LobbyInfo != null)
            {
                System.Console.WriteLine($"Successfully joined lobby: {response.LobbyInfo}");
                this.state = Tcp_State.PeerWithinLobby;
                this.joined_lobby = response.LobbyInfo;
                return true;
            }

            this.state = Tcp_State.WithoutLobby;
            return false;
        }

        public bool TryCreateLobby(string password)
        {
            var request = new WithoutLobbyRequest();
            var message = new CreateLobbyRequest();

            message.Password = password;
            message.Capacity = 2;
            request.CreateLobbyRequest = message;

            System.Console.WriteLine($"Creating lobby {id}");

            request.WriteDelimitedTo(stream);

            var response = CreateLobbyResponse.Parser.ParseDelimitedFrom(stream);

            if (response.LobbyId != 0)
            {
                System.Console.WriteLine($"Successfully created lobby {response.LobbyId}");
                this.state = Tcp_State.HostWithinLobby;
                this.joined_lobby = new LobbyInfo
                {
                    HostId = id,
                    LobbyId = response.LobbyId,
                    Capacity = message.Capacity
                };
                return true;
            }

            this.state = Tcp_State.WithoutLobby;
            return false;
        }

        public AddressInfoMessage GetMyAddressInfo()
        {
            if (state != Tcp_State.WithoutLobby)
            {
                throw new System.Exception("Must be without lobby to request my address");
            }
            var request = new WithoutLobbyRequest();
            var message = new MyAddressInfoRequest();
            request.MyAddressInfoRequest = message;
            request.WriteDelimitedTo(stream);
            var response = AddressInfoMessage.Parser.ParseDelimitedFrom(stream);
            return response;
        }

        public void Go()
        {
            if (state != Tcp_State.HostWithinLobby)
            {
                throw new System.Exception("Must be host to start the lobby");
            }
            var request = new GoRequest();
            var outerRequest = new HostWithinLobbyRequest();
            outerRequest.GoRequest = request;
            outerRequest.WriteDelimitedTo(stream);
        }

        public void StartReceiving()
        {
            while (state != Tcp_State.Closed && client.Connected)
            {
                switch (state)
                {
                    case Tcp_State.PeerWithinLobby:
                        ListenPeer();
                        break;

                    case Tcp_State.HostWithinLobby:
                        ListenHost();
                        break;

                    case Tcp_State.Closing:
                        System.Console.WriteLine("Closing");
                        state = Tcp_State.Closed;
                        break;
                }
            }
        }

        // if there is unexpected data or end of stream, we just throw and get disconnected
        public T ReceiveMessage<T>() where T : IMessage<T>, new()
        {
            try
            {

                T message = new T();
                message.MergeDelimitedFrom(stream);
                return message;
            }
            catch (System.Exception e)
            {
                int b;
                while ((b = stream.ReadByte()) != -1)
                {
                    System.Console.Write($"{b:X} ");
                }
                client.Close();
                throw e;
            }
        }

        public void ListenPeer()
        {
            var response = ReceiveMessage<PeerWithinLobbyResponse>();

            switch (response.MessageCase)
            {
                case PeerWithinLobbyResponse.MessageOneofCase.PeerJoinedNotification:
                    AddPeerToLobby(response.PeerJoinedNotification);
                    System.Console.WriteLine("Peer joined");
                    break;

                case LeaveLobbyNotification:
                    System.Console.WriteLine("Leave lobby notification");
                    break;

                case BecomeHostNotification:
                    System.Console.WriteLine("Promoted to host");
                    break;

                case PeerWithinLobbyResponse.MessageOneofCase.LeaveLobbyResponse:
                    System.Console.WriteLine("Leave lobby response");
                    break;

                case HostAddressInfo:
                    {
                        var info = response.HostAddressInfo;
                        System.Console.WriteLine(info.ToPrettyString());
                        var task = Task.Run(() => EstablishOutboundTcp(response.HostAddressInfo));
                        Task.WaitAll(task);
                        System.Console.WriteLine($"Connection to host established? {task.Result != null}");
                        state = Tcp_State.Closing;
                        ConnectedToPeerEvent?.Invoke();
                        break;
                    }

                default:
                    System.Console.WriteLine("Unexpected response/notification");
                    break;
            }
        }

        public void ListenHost()
        {
            var response = ReceiveMessage<HostWithinLobbyResponse>();

            switch (response.MessageCase)
            {
                case HostWithinLobbyResponse.MessageOneofCase.PeerJoinedNotification:
                    System.Console.WriteLine("Peer joined");
                    AddPeerToLobby(response.PeerJoinedNotification);
                    break;

                case MakeHostResponse:
                    System.Console.WriteLine("Made smb host");
                    break;

                case HostWithinLobbyResponse.MessageOneofCase.LeaveLobbyResponse:
                    System.Console.WriteLine("Leave lobby response");
                    break;

                case GoResponse:
                    if (response.GoResponse.PeerAddressInfo.Count > 0)
                    {
                        var tasks = new Task<Socket>[response.GoResponse.PeerAddressInfo.Count];
                        for (int i = 0; i < tasks.Length; i++)
                        {
                            var info = response.GoResponse.PeerAddressInfo[i];
                            tasks[i] = Task.Run(() => EstablishOutboundTcp(info));
                            System.Console.WriteLine(info.ToPrettyString());
                        }
                        Task.WaitAll(tasks);

                        for (int i = 0; i < tasks.Length; i++)
                        {
                            System.Console.WriteLine($"{i}: Connection established? {tasks[i].Result != null}");
                        }

                    }
                    ConnectedToPeerEvent?.Invoke();
                    state = Tcp_State.Closing;
                    break;

                default:
                    System.Console.WriteLine("Unexpected response/notification");
                    break;
            }
        }

        public event Action PeerJoinedEvent;

        private void AddPeerToLobby(PeerJoinedNotification peer_joined_notification)
        {
            joined_lobby.PeerIds.Add(peer_joined_notification.PeerId);
            PeerJoinedEvent?.Invoke();
        }

        public event Action ConnectedToPeerEvent;

        public Socket EstablishOutboundTcp(AddressInfoMessage info)
        {
            Socket private_client = CreateSocket();
            Socket public_client = CreateSocket();
            Socket listener = CreateSocket();

            private_client.Bind(client.LocalEndPoint);
            public_client.Bind(client.LocalEndPoint);
            listener.Bind(client.LocalEndPoint);
            listener.Listen(1);

            Func<Task<Socket>>[] funcs = new Func<Task<Socket>>[]
            {
                () => Task.Run(() =>
                {
                    System.Console.WriteLine($"Sending connection request to {info.PrivateEndpoint.Convert()} from {private_client.LocalEndPoint}");
                    private_client.Connect(info.PrivateEndpoint.Convert());
                    return private_client;
                }),
                () => Task.Run(() =>
                {
                    System.Console.WriteLine($"Sending connection request to {info.PublicEndpoint.Convert()} from {public_client.LocalEndPoint}");
                    public_client.Connect(info.PublicEndpoint.Convert());
                    return public_client;
                }),
                () => Task.Run(() => listener.Accept())
            };

            Task<Socket>[] tasks = new Task<Socket>[]
            {
                funcs[0](),
                funcs[1](),
                funcs[2]()
            };

            int num_errors = 0;
            const int max_errors = 10;

            while (true)
            {
                int index = Task.WaitAny(tasks);

                if (tasks[index].IsFaulted)
                {
                    if (++num_errors >= max_errors)
                    {
                        return null;
                    }
                    else
                    {
                        System.Console.WriteLine($"Task number {index} threw an error. Current number of errors: {num_errors}");
                        tasks[index] = funcs[index]();
                    }
                }
                else
                {
                    System.Console.WriteLine($"Task number {index} succeeded.");
                    return tasks[index].Result;
                }
            }
        }
    }
}