using System.Net;
using System.Net.Sockets;
using NetProtobuf;
using Google.Protobuf;
using static NetProtobuf.Tcp_WithoutRoomRequest.Types;

namespace Tcp_Test.Client
{
    public class Client
    {
        public int id;
        public IPEndPoint server_endpoint;
        public TcpClient server_connection;

        public IPAddress local_address;
        public IPEndPoint local_endpoint;

        private static IPAddress GetLocalIp()
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
            this.local_address = GetLocalIp();
            this.server_connection = new TcpClient();
        }

        public void ConnectToServer()
        {
            server_connection.Connect(server_endpoint);
            var local_endpoint = server_connection.Client;
            System.Console.WriteLine($"Local endpoint: {local_endpoint.LocalEndPoint}");

            InfoMessage info = new InfoMessage();
            info.Id = -1;
            info.LocalEndpoint = (local_endpoint.LocalEndPoint as IPEndPoint).Convert();

            NetworkStream stream = server_connection.GetStream();
            info.WriteTo(stream);

            byte[] id_buff = new byte[4];
            stream.Read(id_buff, 0, 4);
            id = System.BitConverter.ToInt32(id_buff);
        }

        public bool TryJoinRoom(int id, string password, out Peer peer)
        {
            var stream = server_connection.GetStream();
            var request = new Tcp_WithoutRoomRequest();
            var message = new JoinRoomRequest();

            message.PeerId = this.id; // this is not required, actually, since the session is kept
            // actually, the id should be provided by the user so that the server is able to
            // identify them.
            message.Password = password;
            message.RoomId = id;
            request.JoinRoomRequest = message;

            System.Console.WriteLine($"Joining lobby {id}");

            request.WriteTo(stream);

            var response = Tcp_WithoutRoomResponse.Parser.ParseFrom(stream);

            if (response.Success)
            {
                System.Console.WriteLine($"Successfully joined lobby {id}");
                peer = new Peer(this);
                return true;
            }

            peer = null;
            return false;
        }

        public bool TryCreateRoom(string password, out Host host)
        {
            var stream = server_connection.GetStream();
            var request = new Tcp_WithoutRoomRequest();
            var message = new CreateRoomRequest();

            message.HostId = this.id;
            message.Password = password;
            request.CreateRoomRequest = message;

            System.Console.WriteLine($"Creating lobby {id}");

            request.WriteTo(stream);

            var response = Tcp_WithoutRoomResponse.Parser.ParseFrom(stream);

            if (response.Success)
            {
                System.Console.WriteLine($"Successfully created lobby {id}");
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
    }

    public class Host
    {
        public Host(Client client)
        {
        }
    }
}