using System.Net;
using System.Net.Sockets;
using static Protobuf.Tcp.IPAddress.IpFamilyOneofCase;
using Google.Protobuf;

namespace Protobuf.Tcp
{
    public static class Tcp_Extensions
    {
        public static System.Net.IPAddress GetAddress(this Protobuf.Tcp.IPEndpoint endpoint)
        {
            switch (endpoint.Address.IpFamilyCase)
            {
                case IpV4:
                    return new System.Net.IPAddress(endpoint.Address.IpV4);
                case IpV6:
                    return new System.Net.IPAddress(endpoint.Address.IpV6.ToByteArray());
                default:
                    return null;
            }
        }

        public static uint ToUInt32(this byte[] bytes)
        {
            if (bytes.Length != 4)
                throw new System.ArgumentException("Byte array must be exactly 4 bytes to be convertible to uint.");

            return System.BitConverter.ToUInt32(bytes);
        }

        public static Protobuf.Tcp.IPAddress Convert(this System.Net.IPAddress address)
        {
            switch (address.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    return new Protobuf.Tcp.IPAddress { IpV4 = address.GetAddressBytes().ToUInt32() };
                case AddressFamily.InterNetworkV6:
                    return new Protobuf.Tcp.IPAddress { IpV6 = ByteString.CopyFrom(address.GetAddressBytes()) };
                default:
                    return null;
            }
        }

        public static Protobuf.Tcp.IPEndpoint Convert(this System.Net.IPEndPoint ep)
        {
            return new Protobuf.Tcp.IPEndpoint
            {
                Address = ep.Address.Convert(),
                Port = ep.Port
            };
        }

        public static System.Net.IPEndPoint Convert(this Protobuf.Tcp.IPEndpoint ep)
        {
            return new System.Net.IPEndPoint(ep.GetAddress(), ep.Port);
        }
    }
}