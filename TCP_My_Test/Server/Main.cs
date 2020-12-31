
namespace Tcp_Test.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Server server = new Server(7777);
            server.Start();
        }
    }
}