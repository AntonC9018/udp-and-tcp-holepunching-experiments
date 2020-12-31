using System.Net;
using System.Net.Sockets;
using NetProtobuf;

namespace Tcp_Test
{
    public class Client
    {
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
        }

        public void ConnectToServer()
        {
            server_connection.Connect(server_endpoint);
            var local_endpoint = server_connection.Client;
            var stream = server_connection.GetStream();
            InfoMessage info = new InfoMessage();
            // info.Id
        }
    }

    public class Peer
    {
    }

    public class Host
    {
    }
}