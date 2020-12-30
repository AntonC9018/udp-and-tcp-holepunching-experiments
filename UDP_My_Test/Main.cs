using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Program
{
    public class Client
    {
        public static void Main(string[] args)
        {
            IPEndPoint server_address;

            if (args.Length == 0)
            {
                server_address = new IPEndPoint(IPAddress.Loopback, 7777);
                System.Console.WriteLine("Using loopback ip and port 7777");
            }
            else if (args.Length == 1)
            {
                server_address = new IPEndPoint(IPAddress.Loopback, Int32.Parse(args[0]));
            }
            else if (args.Length != 2)
            {
                System.Console.WriteLine("Usage: dotnet run SERVER_IP SERVER_PORT");
                return;
            }
            else
            {
                server_address = new IPEndPoint(IPAddress.Parse(args[0]), Int32.Parse(args[1]));
            }
            Start(server_address);
        }

        public static void Start(IPEndPoint server_address)
        {
            var client = new UdpClient();
            client.Send(new byte[] { 0xFF }, 1, server_address);
            var result = client.Receive(ref server_address);
            bool is_host = result[0] == 0xFF;

            if (result[0] == 0xFF)
            {
                System.Console.WriteLine("We are host");
            }
            else if (result[0] == 0xEF)
            {
                System.Console.WriteLine("We are client");
            }
            else
            {
                System.Console.WriteLine($"Unexpected response: {result[0]}");
            }

            var other_peer_bytes = client.Receive(ref server_address);

            string ip_address_string = Encoding.UTF8.GetString(other_peer_bytes);

            System.Console.WriteLine($"The packet data is: {ip_address_string}");


            if (is_host)
            {
                string[] split_addresses = ip_address_string.Split('|');
                string[] other_peer_split_address = split_addresses[0].Split(':');
                string[] host_peer_split_address = split_addresses[1].Split(':');

                var other_peer_endpoint = new IPEndPoint(
                    IPAddress.Parse(other_peer_split_address[0]), Int32.Parse(other_peer_split_address[1]));
                var host_end_point = new IPEndPoint(
                    IPAddress.Parse(host_peer_split_address[0]), Int32.Parse(host_peer_split_address[1]));

                // var tcp_host = new TcpListener(other_peer_endpoint);
                client.Connect(other_peer_endpoint);
                byte[] bytes = client.Receive(ref other_peer_endpoint);
                System.Console.WriteLine($"Received: {bytes[0]:X}");
            }
            else
            {
                string[] host_peer_split_address = ip_address_string.Split(':');
                var host_end_point = new IPEndPoint(
                    IPAddress.Parse(host_peer_split_address[0]), Int32.Parse(host_peer_split_address[1]));
                Thread.Sleep(1000);
                client.Send(new byte[] { 0xAA }, 1, host_end_point);
                System.Console.WriteLine($"Sent: 0xAA");
            }
            client.Dispose();
        }
    }
}