using System;
using System.Linq;
using System.Net;
using System.Text;

namespace UDP_StackOverflow.Common
{
    public static class Udp
    {
        public static readonly int GuidLength = 16;
        public static readonly int PeerIdLength = 1;
        public static readonly int IpLength = 4;
        public static readonly int IntLength = 4;

        public static readonly byte[] Prefix = { 12, 23, 34, 45 };

        private static byte[] JoinBytes(params byte[][] bytes)
        {
            var result = new byte[bytes.Sum(x => x.Length)];
            int pos = 0;

            for (int i = 0; i < bytes.Length; i++)
            {
                for (int j = 0; j < bytes[i].Length; j++, pos++)
                {
                    result[pos] = bytes[i][j];
                }
            }

            return result;
        }

        #region Helper extensions

        private static bool StartsWith(this byte[] bytes, byte[] value, int offset = 0)
        {
            if (bytes == null || value == null || bytes.Length < offset + value.Length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (bytes[i + offset] < value[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] ToUnicodeBytes(this string bytes)
        {
            return Encoding.Unicode.GetBytes(bytes);
        }

        private static byte[] Take(this byte[] bytes, int offset, int length)
        {
            return bytes.Skip(offset).Take(length).ToArray();
        }

        public static bool IsSuitableUdpMessage(this byte[] bytes)
        {
            return bytes.StartsWith(Prefix);
        }

        public static int GetInt(this byte[] bytes)
        {
            if (bytes.Length != 4)
                throw new ArgumentException("Byte array must be exactly 4 bytes to be convertible to uint.");

            return (((bytes[0] << 8) + bytes[1] << 8) + bytes[2] << 8) + bytes[3];
        }

        public static byte[] ToByteArray(this int value)
        {
            return new[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            };
        }

        #endregion

        #region Messages

        public abstract class Message
        {
            public readonly byte[] data;

            protected Message(byte[] data)
            {
                this.data = data;
            }
        }

        public class InfoMessage : Message
        {
            private static readonly byte[] MessagePrefix = { 41, 57 };
            private static readonly int MessageLength =
                Prefix.Length + MessagePrefix.Length + PeerIdLength + IpLength + IntLength;

            public readonly byte id;
            public readonly IPAddress localIp;
            public readonly int localPort;

            private InfoMessage(byte[] data, byte id, IPAddress localIp, int localPort)
                : base(data)
            {
                this.id = id;
                this.localIp = localIp;
                this.localPort = localPort;
            }

            public static InfoMessage CreateMessage(byte id, IPAddress localIp, int localPort)
            {
                var data = JoinBytes(
                    Prefix,
                    MessagePrefix,
                    new byte[] { id },
                    localIp.GetAddressBytes(),
                    localPort.ToByteArray()
                );

                return new InfoMessage(data, id, localIp, localPort);
            }

            public static bool TryParse(byte[] data, out InfoMessage message)
            {
                message = null;

                if (!data.StartsWith(Prefix))
                    return false;
                if (!data.StartsWith(MessagePrefix, Prefix.Length))
                    return false;
                if (data.Length != MessageLength)
                    return false;

                int index = Prefix.Length + MessagePrefix.Length;
                byte id = data[index];

                index += PeerIdLength;
                byte[] localIpBytes = data.Take(index, IpLength);
                var localIp = new IPAddress(localIpBytes);

                index += IpLength;
                byte[] localPortBytes = data.Take(index, IntLength);
                int localPort = localPortBytes.GetInt();

                message = new InfoMessage(data, id, localIp, localPort);

                return true;
            }
        }

        public class PeerAddressMessage : Message
        {
            private static readonly byte[] MessagePrefix = { 36, 49 };
            private static readonly int MessageLength = Prefix.Length + MessagePrefix.Length + PeerIdLength + (IpLength + IntLength) * 2;

            public byte id;
            public IPAddress publicIp;
            public int publicPort;
            public IPAddress localIp;
            public int localPort;

            private PeerAddressMessage(byte[] data, byte id, IPAddress publicIp, int publicPort, IPAddress localIp, int localPort)
                : base(data)
            {
                this.id = id;
                this.publicIp = publicIp;
                this.publicPort = publicPort;
                this.localIp = localIp;
                this.localPort = localPort;
            }

            public static PeerAddressMessage CreateMessage(byte id, IPAddress publicIp, int publicPort, IPAddress localIp, int localPort)
            {
                var data = JoinBytes(Prefix, MessagePrefix, new[] { id },
                    publicIp.GetAddressBytes(), publicPort.ToByteArray(),
                    localIp.GetAddressBytes(), localPort.ToByteArray());

                return new PeerAddressMessage(data, id, publicIp, publicPort, localIp, localPort);
            }

            public static bool TryParse(byte[] data, out PeerAddressMessage message)
            {
                message = null;

                if (!data.StartsWith(Prefix))
                    return false;
                if (!data.StartsWith(MessagePrefix, Prefix.Length))
                    return false;
                if (data.Length != MessageLength)
                    return false;

                int index = Prefix.Length + MessagePrefix.Length;
                byte id = data[index];

                index += PeerIdLength;
                byte[] publicIpBytes = data.Take(index, IpLength);
                var publicIp = new IPAddress(publicIpBytes);

                index += IpLength;
                byte[] publicPortBytes = data.Take(index, IntLength);
                int publicPort = publicPortBytes.GetInt();

                index += IntLength;
                byte[] localIpBytes = data.Take(index, IpLength);
                var localIp = new IPAddress(localIpBytes);

                index += IpLength;
                byte[] localPortBytes = data.Take(index, IntLength);
                int localPort = localPortBytes.GetInt();

                message = new PeerAddressMessage(data, id, publicIp, publicPort, localIp, localPort);

                return true;
            }
        }

        public class P2PKeepAliveMessage : Message
        {
            private static readonly byte[] MessagePrefix = { 11, 19 };
            private static P2PKeepAliveMessage _message;

            private P2PKeepAliveMessage(byte[] data) : base(data)
            {
            }

            public static bool TryParse(byte[] data)
            {
                if (!data.StartsWith(Prefix))
                    return false;
                if (!data.StartsWith(MessagePrefix, Prefix.Length))
                    return false;

                return true;
            }

            public static P2PKeepAliveMessage CreateMessage()
            {
                if (_message == null)
                {
                    var data = JoinBytes(Prefix, MessagePrefix);
                    _message = new P2PKeepAliveMessage(data);
                }

                return _message;
            }
        }

        #endregion
    }
}