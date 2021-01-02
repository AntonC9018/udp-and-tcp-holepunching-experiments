using System.Net;
using System.Threading;
using Protobuf.Tcp;

namespace Tcp_Test.Client
{
    public class Program
    {
        private static IPEndPoint[] server_endpoints = new IPEndPoint[]
        {
            new IPEndPoint(System.Net.IPAddress.Parse("34.122.219.86"), 7777),
            new IPEndPoint(System.Net.IPAddress.Parse("35.204.93.215"), 7777)
        };

        public static void Main(string[] args)
        {
            NatConicityTest();
        }

        public static void NatConicityTest()
        {
            var info = new AddressInfoMessage[2];
            var clients = new Client[2];

            clients[0] = new Client(server_endpoints[0]);
            clients[0].ConnectToServer();

            clients[1] = new Client(server_endpoints[1]);
            clients[1].ReuseEndPoint(clients[0].client.LocalEndPoint);
            clients[1].ConnectToServer();

            info[0] = clients[0].GetMyAddressInfo();
            info[1] = clients[1].GetMyAddressInfo();

            System.Console.WriteLine($"AddressInfo 0: {info[0].ToPrettyString()}");
            System.Console.WriteLine($"AddressInfo 1: {info[1].ToPrettyString()}");

            bool isConic = info[0].PublicEndpoint.Equals(info[1].PublicEndpoint);

            System.Console.WriteLine($"Your nat is {(isConic ? "CONE" : "SIMMETRIC")}");
            System.Console.WriteLine("If public ports and addresses are the same, then your NAT is cone.");
            System.Console.WriteLine("Clients will connect IF ONLY your NAT is CONE.");

            clients[0].client.Close();
            clients[1].client.Close();
        }

        public static void TwoPersonTest()
        {
            Client client = new Client(server_endpoints[0]);
            try
            {
                var response = client.ConnectToServer();
                bool joined = false;

                foreach (var lobby_id in response.SomeLobbyIds)
                {
                    if (client.TryJoinLobby(lobby_id, "1111"))
                    {
                        joined = true;
                        break;
                    }
                }

                new Thread(() => client.StartReceiving()).Start();

                if (!joined)
                {
                    if (!client.TryCreateLobby("1111"))
                    {
                        System.Console.WriteLine("Couldn't create lobby. Probably some server error");
                        return;
                    }
                    Thread.Sleep(10000);
                    client.Go();
                }
                Thread.Sleep(60000);
            }
            finally
            {
                client.client.Close();
            }
        }
    }
}