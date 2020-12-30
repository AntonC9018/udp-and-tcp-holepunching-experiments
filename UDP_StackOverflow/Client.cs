using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UDP_StackOverflow.Common;

namespace UDP_StackOverflow
{
    public class Demo
    {
        private static bool _isRunning;

        private static UdpClient _udpPuncher;
        private static UdpClient _udpClient;
        private static UdpClient _extraUdpClient;
        private static bool _extraUdpClientConnected;

        private static byte _id;

        private static IPEndPoint _localEndPoint;
        private static IPEndPoint _serverUdpEndPoint;
        private static IPEndPoint _partnerPublicUdpEndPoint;
        private static IPEndPoint _partnerLocalUdpEndPoint;

        private static string GetLocalIp()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("Failed to get local IP");
        }

        public Demo(string serverIp, int serverPort, byte id, int localPort = 0)
        {
            _serverUdpEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);
            _id = id;
            // we have to bind all our UdpClients to this endpoint
            _localEndPoint = new IPEndPoint(IPAddress.Parse(GetLocalIp()), localPort);
        }

        public void Start()
        {
            _udpPuncher = new UdpClient(); // this guy is just for punching
            _udpClient = new UdpClient(); // this will keep hole alive, and also can send data
            _extraUdpClient = new UdpClient(); // i think, this guy is the best option for sending data (explained below)

            InitUdpClients(new UdpClient[] { _udpPuncher, _udpClient, _extraUdpClient }, _localEndPoint);

            Task.Run((Action)SendConnectionUdpMessages);
            Task.Run((Action)ListenUdp);

            Console.ReadLine();
            _isRunning = false;
        }

        private void InitUdpClients(IEnumerable<UdpClient> clients, EndPoint localEndPoint)
        {
            // if you don't want to use explicit localPort, you should create here one more UdpClient (X) and send something to server (it will automatically bind X to free port). then bind all clients to this port and close X

            foreach (var udpClient in clients)
            {
                udpClient.ExclusiveAddressUse = false;
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(localEndPoint);
            }
        }

        private void SendConnectionUdpMessages()
        {
            _isRunning = true;

            var messageToServer = Udp.InfoMessage.CreateMessage(
                _id, _localEndPoint.Address, _localEndPoint.Port);
            var messageToPeer = Udp.P2PKeepAliveMessage.CreateMessage();

            while (_isRunning)
            {
                // while we haven't got the partner's address, we will send messages to server
                if (_partnerPublicUdpEndPoint == null && _partnerLocalUdpEndPoint == null)
                {
                    _udpPuncher.Send(messageToServer.data, messageToServer.data.Length, _serverUdpEndPoint);
                    Console.WriteLine($" >>> Sent UDP to server [ {_serverUdpEndPoint.Address} : {_serverUdpEndPoint.Port} ]");
                }
                else
                {
                    // you can skip it. just a demonstration that you still can send messages to server
                    // _udpClient.Send(messageToServer.Data, messageToServer.Data.Length, _serverUdpEndPoint);
                    // Console.WriteLine($" >>> Sent UDP to server [ {_serverUdpEndPoint.Address} : {_serverUdpEndPoint.Port} ]");

                    // THIS is how we punch a hole! we expect the very first message to be dropped by the partner's NAT router
                    // i suppose that this is good idea to send this "keep-alive" messages to peer even if you have already connected,
                    // because AFAIK "hole" for UDP lives ~2 minutes on NAT. so "will we let it die? NEVER!" (c)
                    _udpClient.Send(messageToPeer.data, messageToPeer.data.Length, _partnerPublicUdpEndPoint);
                    _udpClient.Send(messageToPeer.data, messageToPeer.data.Length, _partnerLocalUdpEndPoint);
                    Console.WriteLine($" >>> Sent UDP to peer.public [ {_partnerPublicUdpEndPoint.Address} : {_partnerPublicUdpEndPoint.Port} ]");
                    Console.WriteLine($" >>> Sent UDP to peer.local [ {_partnerLocalUdpEndPoint.Address} : {_partnerLocalUdpEndPoint.Port} ]");

                    // "connected" UdpClient sends data much faster, 
                    // so if you have something that your partner cant wait for (voice, for example), send it this way
                    if (_extraUdpClientConnected)
                    {
                        _extraUdpClient.Send(messageToPeer.data, messageToPeer.data.Length);
                        Console.WriteLine($" >>> Sent UDP to peer.received EP");
                    }
                }

                Thread.Sleep(3000);
            }
        }

        private async void ListenUdp()
        {
            _isRunning = true;

            while (_isRunning)
            {
                try
                {
                    // also important thing!
                    // before the hole has been punched, you must listen for incoming packets using the "puncher
                    // we will close the puncher later.
                    // where you already have p2p connection (and "puncher" closed), use the "non-puncher"
                    if (_partnerPublicUdpEndPoint == null)
                    {
                        var result = await _udpPuncher.ReceiveAsync();
                        if (!_isRunning)
                        {
                            break;
                        }
                        ProcessServerUdpMessage(result.Buffer, result.RemoteEndPoint);
                    }
                    else
                    {
                        var result = await _udpClient.ReceiveAsync();
                        if (!_isRunning)
                        {
                            break;
                        }
                        ProcessPartnerUdpMessage(result.Buffer, result.RemoteEndPoint);
                    }
                }
                catch (SocketException ex)
                {
                    // do something here...
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private static void ProcessServerUdpMessage(byte[] buffer, IPEndPoint remoteEndPoint)
        {
            // if server sends the partner's endpoints, we will store it and (IMPORTANT) close "puncher"
            if (Udp.PeerAddressMessage.TryParse(
                buffer, out Udp.PeerAddressMessage peerAddressMessage))
            {
                Console.WriteLine(" <<< Got response from server");
                _partnerPublicUdpEndPoint = new IPEndPoint(peerAddressMessage.publicIp, peerAddressMessage.publicPort);
                _partnerLocalUdpEndPoint = new IPEndPoint(peerAddressMessage.localIp, peerAddressMessage.localPort);

                _udpPuncher.Close();
            }
            else if (Udp.P2PKeepAliveMessage.TryParse(buffer))
            {
                Console.WriteLine($"Got a keepalive message from server {remoteEndPoint.Address}:{remoteEndPoint.Port}");

                if (!_extraUdpClientConnected)
                {
                    _extraUdpClientConnected = true;
                    _extraUdpClient.Connect(remoteEndPoint);
                }
            }
        }

        private static void ProcessPartnerUdpMessage(byte[] buffer, IPEndPoint remoteEndPoint)
        {
            // since we got this message we know partner's endpoint for sure, 
            // and we can "connect" UdpClient to it, so it will work faster
            if (Udp.P2PKeepAliveMessage.TryParse(buffer))
            {
                Console.WriteLine($"Got a keepalive message from {remoteEndPoint.Address}:{remoteEndPoint.Port}");

                if (!_extraUdpClientConnected)
                {
                    _extraUdpClientConnected = true;
                    _extraUdpClient.Connect(remoteEndPoint);
                }
            }
            else
            {
                // Here, add custom messages. Try using protobuf for that
                Console.WriteLine("???");
            }
        }
    }
}