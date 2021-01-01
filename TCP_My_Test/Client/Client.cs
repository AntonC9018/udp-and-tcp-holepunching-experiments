using System.Net.Sockets;
using Protobuf.Tcp;
using Google.Protobuf;
using System;
using System.Net;
using static Protobuf.Tcp.WithoutLobbyRequest.Types;

namespace Tcp_Test.Client
{
    public class Client
    {
        public int id;
        public System.Net.IPEndPoint server_endpoint;
        public TcpClient server_connection;

        public System.Net.IPAddress private_address;
        public Protobuf.Tcp.IPEndpoint private_endpoint;

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
            this.server_connection = new TcpClient();
            this.id = new Random().Next();
        }

        public void ConnectToServer()
        {
            server_connection.Connect(server_endpoint);
            this.private_endpoint = new IPEndpoint
            {
                Address = private_address.Convert(),
                Port = ((IPEndPoint)server_connection.Client.LocalEndPoint).Port
            };
            System.Console.WriteLine($"Local endpoint: {private_endpoint}");

            InitializationRequest info = new InitializationRequest
            {
                ClientId = id,
                PrivateEndpoint = private_endpoint
            };

            NetworkStream stream = server_connection.GetStream();
            info.WriteDelimitedTo(stream);

            var response = InitializationResponse.Parser.ParseDelimitedFrom(stream);
            System.Console.WriteLine(response);
        }

        public bool TryJoinLobby(int id, string password, out Peer peer)
        {
            var stream = server_connection.GetStream();
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
                peer = new Peer(this);
                return true;
            }

            peer = null;
            return false;
        }

        public bool TryCreateLobby(string password, out Host host)
        {
            var stream = server_connection.GetStream();
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
                host = new Host(this);
                return true;
            }

            host = null;
            return false;
        }
    }

    public class Peer
    {
        public Peer(Client client)
        {
        }

        public void StartListening()
        {
            
        }

        public Client LeaveLobby()
        {
            return null;
        }
    }

    public class Host
    {
        public Host(Client client)
        {
        }
    }
}