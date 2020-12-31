using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Tcp_Test.Server
{
    public class Server
    {
        // First, the clients would establish a tcp connection. This connection stays.
        // As they do that, they would send the server their private ip address.
        // The server would find out their public address based on where the request came from. 
        public Dictionary<int, Tcp_Session> sessions;
        public int currentId;
        public Dictionary<int, Lobby> lobbies;

        public TcpListener listener;

        public Server(int port)
        {
            lobbies = new Dictionary<int, Lobby>();
            sessions = new Dictionary<int, Tcp_Session>();
            listener = new TcpListener(IPAddress.Any, port);

            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
            listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }

        public void Log(string str)
        {
            System.Console.WriteLine($"Server | {str}");
        }

        public void Start()
        {
            try
            {
                listener.Start();
                Log($"Listening on {listener.LocalEndpoint}");

                while (true)
                {
                    var client = listener.AcceptTcpClient();
                    var session = new Tcp_Session(++currentId, client);
                    new Thread(() => session.Start(this)).Start();
                }
            }
            catch (System.Exception e)
            {
                System.Console.WriteLine($"Server error: {e}");
            }
            finally
            {
                foreach (var peer in sessions.Values)
                {
                    if (peer.client.Connected)
                    {
                        peer.Log("Closing connection to server");
                        peer.client.Close();
                    }
                }
            }
        }
    }
}