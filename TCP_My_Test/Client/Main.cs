using System.Net;
using System.Threading;

namespace Tcp_Test.Client
{
    public class Program
    {
        public static void Main(string[] args)
        {

            var server_endpoint = new IPEndPoint(IPAddress.Parse("34.122.219.86"), 7777);
            Client client = new Client(server_endpoint);
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

                if (!joined)
                {
                    if (!client.TryCreateLobby("1111"))
                    {
                        System.Console.WriteLine("Couldn't create lobby. Probably some server error");
                        return;
                    }
                    Thread.Sleep(10000);
                    client.Go();
                    client.StartReceiving();
                }
                else
                {
                    client.StartReceiving();
                }
                Thread.Sleep(10000);
            }
            finally
            {
                client.client.Close();
            }
        }
    }
}