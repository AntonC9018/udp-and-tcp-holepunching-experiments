
namespace Tcp_Test
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Server.Server server = new Server.Server(7777);
            server.Start();
        }
    }
}