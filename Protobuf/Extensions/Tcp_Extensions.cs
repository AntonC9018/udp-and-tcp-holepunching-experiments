using System.Net;
using System.Net.Sockets;
using static Protobuf.Tcp.IPAddress.IpFamilyOneofCase;
using Google.Protobuf;

namespace Protobuf.Tcp
{
    public static class Tcp_Extensions
    {
        public static System.Net.IPAddress GetAddress(this IPEndpoint endpoint)
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

        public static IPAddress Convert(this System.Net.IPAddress address)
        {
            switch (address.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    return new IPAddress { IpV4 = address.GetAddressBytes().ToUInt32() };
                case AddressFamily.InterNetworkV6:
                    return new IPAddress { IpV6 = ByteString.CopyFrom(address.GetAddressBytes()) };
                default:
                    return null;
            }
        }

        public static IPEndpoint Convert(this IPEndPoint ep)
        {
            return new IPEndpoint
            {
                Address = ep.Address.Convert(),
                Port = ep.Port
            };
        }
    }
}