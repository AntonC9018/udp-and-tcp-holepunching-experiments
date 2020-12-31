using System.Net;

namespace Tcp_Test.Client
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var server_endpoint = new IPEndPoint(IPAddress.Parse("35.241.171.193"), 7777);
            Client client = new Client(server_endpoint);
            client.ConnectToServer();
        }
    }
}