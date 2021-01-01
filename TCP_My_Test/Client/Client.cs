using System.Net.Sockets;
using Protobuf.Tcp;
using Google.Protobuf;
using System;
using System.Net;

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

        //     public bool TryJoinRoom(int id, string password, out Peer peer)
        //     {
        //         var stream = server_connection.GetStream();
        //         var request = new Tcp_WithoutRoomRequest();
        //         var message = new JoinRoomRequest();

        //         message.PeerId = this.id; // this is not required, actually, since the session is kept
        //         // actually, the id should be provided by the user so that the server is able to
        //         // identify them.
        //         message.Password = password;
        //         message.RoomId = id;
        //         request.JoinRoomRequest = message;

        //         System.Console.WriteLine($"Joining lobby {id}");

        //         request.WriteDelimitedTo(stream);

        //         var response = Tcp_WithoutRoomResponse.Parser.ParseDelimitedFrom(stream);

        //         if (response.Success)
        //         {
        //             System.Console.WriteLine($"Successfully joined lobby {id}");
        //             peer = new Peer(this);
        //             return true;
        //         }

        //         peer = null;
        //         return false;
        //     }

        //     public bool TryCreateRoom(string password, out Host host)
        //     {
        //         var stream = server_connection.GetStream();
        //         var request = new Tcp_WithoutRoomRequest();
        //         var message = new CreateRoomRequest();

        //         message.HostId = this.id;
        //         message.Password = password;
        //         request.CreateRoomRequest = message;

        //         System.Console.WriteLine($"Creating lobby {id}");

        //         request.WriteDelimitedTo(stream);

        //         var response = Tcp_WithoutRoomResponse.Parser.ParseDelimitedFrom(stream);

        //         if (response.Success)
        //         {
        //             System.Console.WriteLine($"Successfully created lobby {id}");
        //             host = new Host(this);
        //             return true;
        //         }

        //         host = null;
        //         return false;
        //     }
    }

    public class Peer
    {
        public Peer(Client client)
        {
        }
    }

    public class Host
    {
        public Host(Client client)
        {
        }
    }
}