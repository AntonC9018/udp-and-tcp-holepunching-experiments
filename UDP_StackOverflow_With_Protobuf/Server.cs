using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using NetProtobuf;

namespace UDP_StackOverflow.WithProtobuf
{
    class Server
    {
        private bool _isRunning;
        private UdpClient _udpClient;
        private readonly Dictionary<int, PeerContext> _contexts = new Dictionary<int, PeerContext>();

        private readonly Dictionary<int, int> Mappings = new Dictionary<int, int>
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
            try
            {
                InfoMessage infoMessage = InfoMessage.Parser.ParseFrom(buffer);
                Console.WriteLine($">>> Got bad UDP {remoteEndPoint.Address}:{remoteEndPoint.Port}");
                _udpClient.Send(new byte[] { 1 }, 1, remoteEndPoint);
                return;
            }
            catch { }

            PeerAddressMessage peerAddressMessage = PeerAddressMessage.Parser.ParseFrom(buffer);
            Console.WriteLine($">>> Got UDP from {peerAddressMessage.Id}. {remoteEndPoint.Address}:{remoteEndPoint.Port}");

            if (!_contexts.TryGetValue(peerAddressMessage.Id, out PeerContext context))
            {
                context = new PeerContext
                {
                    peerId = peerAddressMessage.Id,
                    publicUdpEndPoint = peerAddressMessage.PublicEndpoint.Convert(),
                    localUdpEndPoint = peerAddressMessage.LocalEndpoint.Convert(),
                };

                _contexts.Add(context.peerId, context);
            }

            int partnerId = Mappings[context.peerId];
            if (!_contexts.TryGetValue(partnerId, out context))
            {
                _udpClient.Send(new byte[] { 1 }, 1, remoteEndPoint);
                return;
            }

            var response = new PeerAddressMessage
            {
                Id = partnerId,
                PublicEndpoint = context.publicUdpEndPoint.Convert(),
                LocalEndpoint = context.localUdpEndPoint.Convert()
            };

            byte[] bytes = response.ToByteArray();
            _udpClient.Send(bytes, bytes.Length, remoteEndPoint);

            Console.WriteLine($" <<< Responsed to {response.Id}");
        }
    }

    public class PeerContext
    {
        public int peerId;
        public IPEndPoint publicUdpEndPoint;
        public IPEndPoint localUdpEndPoint;
    }
}