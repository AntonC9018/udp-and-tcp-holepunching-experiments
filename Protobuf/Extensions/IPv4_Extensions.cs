using System.Net;

namespace NetProtobuf
{
    public static class IPv4_Endpoint_Extensions
    {
        public static IPEndPoint Convert(this IPv4_Endpoint ep)
        {
            return new IPEndPoint(ep.Address, ep.Port);
        }

        public static int GetInt(this byte[] bytes)
        {
            if (bytes.Length != 4)
                throw new System.ArgumentException("Byte array must be exactly 4 bytes to be convertible to uint.");

            return (((bytes[0] << 8) + bytes[1] << 8) + bytes[2] << 8) + bytes[3];
        }

        public static uint GetIPv4(this IPAddress address)
        {
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();
                return (uint)bytes.GetInt();
            }
            else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                byte[] bytes = address.MapToIPv4().GetAddressBytes();
                return (uint)bytes.GetInt();
            }
            throw new System.Exception("Expected an IP address");
        }

        public static IPv4_Endpoint Convert(this IPEndPoint ep)
        {
            return new IPv4_Endpoint { Address = ep.Address.GetIPv4(), Port = ep.Port };
        }
    }
}