using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UDP_StackOverflow.Common;

namespace UDP_StackOverflow.Server
{
    class Server
    {
        private bool _isRunning;
        private UdpClient _udpClient;
        private readonly Dictionary<byte, PeerContext> _contexts = new Dictionary<byte, PeerContext>();

        private readonly Dictionary<byte, byte> Mappings = new Dictionary<byte, byte>
        {
            {1, 2},
            {2, 1},
        };

        public Server(int port)
        {
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            ListenUdp();

            Console.ReadLine();
            _isRunning = false;
        }

        private async void ListenUdp()
        {
            _isRunning = true;

            while (_isRunning)
            {
                try
                {
                    var receivedResults = await _udpClient.ReceiveAsync();

                    if (!_isRunning)
                    {
                        break;
                    }

                    ProcessUdpMessage(receivedResults.Buffer, receivedResults.RemoteEndPoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private void ProcessUdpMessage(byte[] buffer, IPEndPoint remoteEndPoint)
        {
            if (!Udp.InfoMessage.TryParse(buffer, out Udp.InfoMessage message))
            {
                Console.WriteLine($">>> Got bad UDP {remoteEndPoint.Address}:{remoteEndPoint.Port}");
                _udpClient.Send(new byte[] { 1 }, 1, remoteEndPoint);
                return;
            }

            Console.WriteLine($">>> Got UDP from {message.id}. {remoteEndPoint.Address}:{remoteEndPoint.Port}");

            if (!_contexts.TryGetValue(message.id, out PeerContext context))
            {
                context = new PeerContext
                {
                    peerId = message.id,
                    publicUdpEndPoint = remoteEndPoint,
                    localUdpEndPoint = new IPEndPoint(message.localIp, message.localPort),
                };

                _contexts.Add(context.peerId, context);
            }

            byte partnerId = Mappings[context.peerId];
            if (!_contexts.TryGetValue(partnerId, out context))
            {
                _udpClient.Send(new byte[] { 1 }, 1, remoteEndPoint);
                return;
            }

            var response = Udp.PeerAddressMessage.CreateMessage(
                partnerId,
                context.publicUdpEndPoint.Address,
                context.publicUdpEndPoint.Port,
                context.localUdpEndPoint.Address,
                context.localUdpEndPoint.Port
            );

            _udpClient.Send(response.data, response.data.Length, remoteEndPoint);

            Console.WriteLine($" <<< Responsed to {message.id}");
        }
    }

    public class PeerContext
    {
        public byte peerId;
        public IPEndPoint publicUdpEndPoint;
        public IPEndPoint localUdpEndPoint;
    }
}