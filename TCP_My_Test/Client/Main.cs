using System.Net;
using System.Threading;

namespace Tcp_Test.Client
{
    public class Program
    {
        private static IPEndPoint[] server_endpoints = new IPEndPoint[]
        {
            new IPEndPoint(IPAddress.Parse("34.122.219.86"), 7777),
            new IPEndPoint(IPAddress.Parse("35.204.93.215"), 7777)
        };

        public static void Main(string[] args)
        {
            TwoPersonTest();
        }

        public static void NatConicityTest()
        {
            Client client1 = new Client(server_endpoints[0]);
            client1.ConnectToServer();
            Client client2 = new Client(server_endpoints[1]);
            client2.ReuseEndPoint(client1.client.LocalEndPoint);
            client2.ConnectToServer();
            Thread.Sleep(10000);
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