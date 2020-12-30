using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Program
{
    public class Server
    {
        public static void Main(string[] args)
        {
            int port = args.Length == 1 ? System.Int32.Parse(args[0]) : 7777;
            Start(port);
        }

        public static void Start(int port)
        {
            UdpClient udpListener = new UdpClient(port);

            System.Console.WriteLine($"Listening on port {port}");

            while (true)
            {
                IPEndPoint host_ipEndPoint = new IPEndPoint(IPAddress.Any, port);

                byte[] host_received_bytes = udpListener.Receive(ref host_ipEndPoint);
                System.Console.WriteLine($"Got message from {host_ipEndPoint.Address}:{host_ipEndPoint.Port}");

                if (host_received_bytes.Length != 1)
                {
                    System.Console.WriteLine("Skipping, since the length is not 1");
                    continue;
                }
                if (host_received_bytes[0] != 0xFF)
                {
                    System.Console.WriteLine("Skipping, since expecting 0xFF");
                    continue;
                }

                System.Console.WriteLine($"Making: {host_ipEndPoint.Address}:{host_ipEndPoint.Port} the host");
                udpListener.Send(new byte[] { 0xFF }, 1, host_ipEndPoint);

                System.Console.WriteLine("Waiting for the second client...");

                IPEndPoint partner_ipEndPoint = new IPEndPoint(IPAddress.Any, port);

                while (true)
                {
                    byte[] partner_received_bytes = udpListener.Receive(ref partner_ipEndPoint);

                    if (partner_ipEndPoint.Address == host_ipEndPoint.Address
                        && partner_ipEndPoint.Port == host_ipEndPoint.Port)
                    {
                        System.Console.WriteLine("Received another message from the host. Ignoring.");
                    }
                    else
                    {
                        break;
                    }
                }

                System.Console.WriteLine($"Making {partner_ipEndPoint.Address}:{partner_ipEndPoint.Port} the partner");

                udpListener.Send(new byte[] { 0xEF }, 1, partner_ipEndPoint);

                byte[] partner_ip_bytes = Encoding.ASCII.GetBytes(partner_ipEndPoint.ToString());
                byte[] host_ip_bytes = Encoding.ASCII.GetBytes(host_ipEndPoint.ToString());

                byte[] host_and_partner_ip_bytes = new byte[partner_ip_bytes.Length + host_ip_bytes.Length + 1];
                partner_ip_bytes.CopyTo(host_and_partner_ip_bytes, 0);
                host_and_partner_ip_bytes[partner_ip_bytes.Length] = (byte)'|';
                host_ip_bytes.CopyTo(host_and_partner_ip_bytes, partner_ip_bytes.Length + 1);

                udpListener.Send(host_and_partner_ip_bytes, host_and_partner_ip_bytes.Length, host_ipEndPoint);
                udpListener.Send(host_ip_bytes, host_ip_bytes.Length, partner_ipEndPoint);

                System.Console.WriteLine("Sent all the info");
            }
        }
    }
}