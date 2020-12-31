using System.Net;

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
                client.ConnectToServer();

                if (client.TryJoinRoom(2, "1111", out Peer peer))
                {
                    System.Console.WriteLine("Managed to join the group 2. Shutting down...");
                    client.server_connection.Close();
                    return;
                }
                else
                {
                    System.Console.WriteLine("Couldn't join the group 2");
                }

                if (client.TryCreateRoom("1111", out Host host))
                {
                    System.Console.WriteLine("Created room");
                }
                else
                {
                    System.Console.WriteLine("Could not create room");
                }
            }
            finally
            {
                client.server_connection.Close();
            }
        }
    }
}