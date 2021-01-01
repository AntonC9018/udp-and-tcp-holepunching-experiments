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
        public System.Net.IPEndPoint server_endpoint;
        public TcpClient client;

        public System.Net.IPAddress private_address;
        public Protobuf.Tcp.IPEndpoint private_endpoint;
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

        public Client(IPEndPoint server_endpoint)
        {
            this.server_endpoint = server_endpoint;
            this.private_address = GetLocalIp();
            this.client = new TcpClient();
            this.id = new Random().Next();
            this.state = Tcp_State.Connecting;
        }

        public InitializationResponse ConnectToServer()
        {
            var socket = client.Client;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
#if (LINUX)
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseUnicastPort, 1);
#endif
            client.Connect(server_endpoint);

            this.private_endpoint = new IPEndpoint
            {
                Address = private_address.Convert(),
                Port = ((IPEndPoint)client.Client.LocalEndPoint).Port
            };
            System.Console.WriteLine($"Local endpoint: {private_endpoint}");

            this.state = Tcp_State.Initialization;
            InitializationRequest info = new InitializationRequest
            {
                ClientId = id,
                PrivateEndpoint = private_endpoint
            };

            NetworkStream stream = client.GetStream();
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

            var stream = client.GetStream();
            request.WriteDelimitedTo(stream);

            var response = JoinLobbyResponse.Parser.ParseDelimitedFrom(stream);

            if (response.LobbyInfo != null)
            {
                System.Console.WriteLine($"Successfully joined lobby: {response.LobbyInfo}");
                this.state = Tcp_State.PeerWithinLobby;
                return true;
            }

            this.state = Tcp_State.WithoutLobby;
            return false;
        }

        public bool TryCreateLobby(string password)
        {
            var stream = client.GetStream();
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
                return true;
            }

            this.state = Tcp_State.WithoutLobby;
            return false;
        }

        public void Go()
        {
            if (state != Tcp_State.HostWithinLobby)
            {
                throw new System.Exception("Must be host to start the lobby");
            }
            var stream = client.GetStream();
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
                        System.Console.WriteLine("Initialize connection!");
                        state = Tcp_State.Closed;
                        break;
                }
            }
        }

        // if there is unexpected data or end of stream, we just throw and get disconnected
        public T ReceiveMessage<T>() where T : IMessage<T>, new()
        {
            var stream = client.GetStream();
            try
            {

                T message = new T();
                message.MergeDelimitedFrom(stream);
                return message;
            }
            catch (System.Exception e)
            {
                client.Close();
                throw e;
            }
        }

        public void ListenPeer()
        {
            var response = ReceiveMessage<PeerWithinLobbyResponse>();

            switch (response.MessageCase)
            {
                case PeerJoinedNotification:
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
                    System.Console.WriteLine($"Starting. Host address info: {response.HostAddressInfo}");
                    var task = Task.Run(() => EstablishOutboundTcp(response.HostAddressInfo));
                    Task.WaitAll(task);
                    System.Console.WriteLine($"Connection to host established? {task.Result != null}");
                    break;

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
                case MakeHostResponse:
                    System.Console.WriteLine("Peer joined");
                    break;

                case HostWithinLobbyResponse.MessageOneofCase.LeaveLobbyResponse:
                    System.Console.WriteLine("Leave lobby response");
                    break;

                case GoResponse:
                    System.Console.WriteLine($"Starting. Peer address info: {response.GoResponse.PeerAddressInfo}");

                    var tasks = new Task<TcpClient>[response.GoResponse.PeerAddressInfo.Count];
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        var info = response.GoResponse.PeerAddressInfo[i];
                        tasks[i] = Task.Run(() => EstablishOutboundTcp(info));
                    }
                    Task.WaitAll(tasks);

                    for (int i = 0; i < tasks.Length; i++)
                    {
                        System.Console.WriteLine($"{i}: Connection established? {tasks[i].Result != null}");
                    }
                    break;

                default:
                    System.Console.WriteLine("Unexpected response/notification");
                    break;
            }
        }

        public TcpClient EstablishOutboundTcp(AddressInfoMessage info)
        {
            TcpClient private_client = new TcpClient();
            TcpClient public_client = new TcpClient();
            TcpListener listener = new TcpListener(private_endpoint.GetAddress(), private_endpoint.Port);
            listener.Start();

            Func<Task<TcpClient>>[] funcs = new Func<Task<TcpClient>>[]
            {
                () => Task.Run(() =>
                {
                    private_client.Connect(info.PrivateEndpoint.Convert());
                    return private_client;
                }),
                () => Task.Run(() =>
                {
                    public_client.Connect(info.PublicEndpoint.Convert());
                    return public_client;
                }),
                () => Task.Run(() => listener.AcceptTcpClient())
            };

            Task<TcpClient>[] tasks = new Task<TcpClient>[]
            {
                funcs[0](),
                funcs[1](),
                funcs[2]()
            };

            int num_errors = 0;
            const int max_errors = 100;

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
                        tasks[index] = funcs[index]();
                    }
                }
                else
                {
                    return tasks[index].Result;
                }
            }
        }
    }
}