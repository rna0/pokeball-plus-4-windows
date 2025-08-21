using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;

namespace PokeballPlus4Windows
{
    // Enums and structs from DS4Windows for protocol compatibility
    public enum DsState : byte
    {
        Disconnected = 0x00,
        Reserved = 0x01,
        Connected = 0x02
    };

    public enum DsConnection : byte
    {
        None = 0x00,
        Usb = 0x01,
        Bluetooth = 0x02
    };

    public enum DsModel : byte
    {
        None = 0,
        DS3 = 1,
        DS4 = 2,
        Generic = 3
    }

    public enum DsBattery : byte
    {
        None = 0x00,
        Dying = 0x01,
        Low = 0x02,
        Medium = 0x03,
        High = 0x04,
        Full = 0x05,
        Charging = 0xEE,
        Charged = 0xEF
    };

    [StructLayout(LayoutKind.Explicit, Size = 100)]
    public unsafe struct PadDataRspPacket
    {
        [FieldOffset(0)] public fixed byte initCode[4];
        [FieldOffset(4)] public ushort protocolVersion;
        [FieldOffset(6)] public ushort messageLen;
        [FieldOffset(8)] public int crc;
        [FieldOffset(12)] public uint serverId;
        [FieldOffset(16)] public uint messageType;
        [FieldOffset(20)] public byte padId;
        [FieldOffset(21)] public byte padState;
        [FieldOffset(22)] public byte model;
        [FieldOffset(23)] public byte connectionType;
        [FieldOffset(24)] public fixed byte address[6];
        [FieldOffset(30)] public byte batteryStatus;
        [FieldOffset(31)] public byte isActive;
        [FieldOffset(32)] public uint packetCounter;
        [FieldOffset(36)] public byte buttons1;
        [FieldOffset(37)] public byte buttons2;
        [FieldOffset(38)] public byte psButton;
        [FieldOffset(39)] public byte touchButton;
        [FieldOffset(40)] public byte lx;
        [FieldOffset(41)] public byte ly;
        [FieldOffset(42)] public byte rx;
        [FieldOffset(43)] public byte ry;
        [FieldOffset(44)] public byte dpadLeft;
        [FieldOffset(45)] public byte dpadDown;
        [FieldOffset(46)] public byte dpadRight;
        [FieldOffset(47)] public byte dpadUp;
        [FieldOffset(48)] public byte square;
        [FieldOffset(49)] public byte cross;
        [FieldOffset(50)] public byte circle;
        [FieldOffset(51)] public byte triangle;
        [FieldOffset(52)] public byte r1;
        [FieldOffset(53)] public byte l1;
        [FieldOffset(54)] public byte r2;
        [FieldOffset(55)] public byte l2;
        [FieldOffset(56)] public byte touch1Active;
        [FieldOffset(57)] public byte touch1PacketId;
        [FieldOffset(58)] public ushort touch1X;
        [FieldOffset(60)] public ushort touch1Y;
        [FieldOffset(62)] public byte touch2Active;
        [FieldOffset(63)] public byte touch2PacketId;
        [FieldOffset(64)] public ushort touch2X;
        [FieldOffset(66)] public ushort touch2Y;
        [FieldOffset(68)] public ulong totalMicroSec;
        [FieldOffset(76)] public float accelXG;
        [FieldOffset(80)] public float accelYG;
        [FieldOffset(84)] public float accelZG;
        [FieldOffset(88)] public float angVelPitch;
        [FieldOffset(92)] public float angVelYaw;
        [FieldOffset(96)] public float angVelRoll;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CemuhookPadData
    {
        public byte padId;
        public byte padState;
        public byte model;
        public byte connectionType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] macAddress;
        public byte batteryStatus;
        public byte isActive;
        public uint packetCounter;
        public ulong timestamp;
        public float axisX;
        public float axisY;
        public float accelX;
        public float accelY;
        public float accelZ;
        public float gyroX;
        public float gyroY;
        public float gyroZ;
        public ushort buttons;
    }

    public unsafe class CemuhookUdpServer : IDisposable
    {
        private readonly UdpClient _udpServer;
        private readonly int _port;
        private uint _serverId;
        private uint _packetCounter = 0;
        private bool _running;
        private Thread _listenerThread;
        private Func<int, CemuhookPadData> _getPadDataForSlot; // Now supports multiple pads
        private const int NUMBER_SLOTS = 4;
        private const ushort MaxProtocolVersion = 1001;
        private const int DATA_RSP_PACKET_LEN = 100;

        public CemuhookUdpServer(int port = 26760)
        {
            _port = port;
            _udpServer = new UdpClient(_port);
            var randomBuf = new byte[4];
            new Random().NextBytes(randomBuf);
            _serverId = BitConverter.ToUInt32(randomBuf, 0);
        }

        public void Start(Func<int, CemuhookPadData> getPadDataForSlot)
        {
            _getPadDataForSlot = getPadDataForSlot;
            _running = true;
            _listenerThread = new Thread(ListenLoop) { IsBackground = true };
            _listenerThread.Start();
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    IPEndPoint clientEP = new IPEndPoint(IPAddress.Any, 0);
                    var request = _udpServer.Receive(ref clientEP);
                    var responses = ProcessRequest(request);
                    if (responses != null)
                    {
                        foreach (var response in responses)
                        {
                            _udpServer.Send(response, response.Length, clientEP);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Cemuhook] Exception: {ex}");
                }
            }
        }

        private byte[][] ProcessRequest(byte[] request)
        {
            if (request.Length < 20 || request[0] != (byte)'D' || request[1] != (byte)'S' || request[2] != (byte)'U' || request[3] != (byte)'C')
                return null;
            int currIdx = 4;
            ushort protocolVersion = BitConverter.ToUInt16(request, currIdx);
            currIdx += 2;
            if (protocolVersion > MaxProtocolVersion)
                return null;
            ushort packetSize = BitConverter.ToUInt16(request, currIdx);
            currIdx += 2;
            packetSize += 16;
            if (packetSize > request.Length)
                return null;
            uint crcValue = BitConverter.ToUInt32(request, currIdx);
            request[currIdx++] = 0;
            request[currIdx++] = 0;
            request[currIdx++] = 0;
            request[currIdx++] = 0;
            uint crcCalc = Crc32(request, request.Length);
            if (crcValue != crcCalc)
                return null;
            uint clientId = BitConverter.ToUInt32(request, currIdx);
            currIdx += 4;
            uint messageType = BitConverter.ToUInt32(request, currIdx);
            currIdx += 4;
            var responses = new List<byte[]>();
            if (messageType == 0x100000) // DSUC_VersionReq
            {
                byte[] outputData = new byte[8];
                Array.Copy(BitConverter.GetBytes(0x100000), 0, outputData, 0, 4);
                Array.Copy(BitConverter.GetBytes(MaxProtocolVersion), 0, outputData, 4, 2);
                outputData[6] = 0;
                outputData[7] = 0;
                responses.Add(BuildPacket(outputData, MaxProtocolVersion));
            }
            else if (messageType == 0x100001) // DSUC_ListPorts
            {
                int numPadRequests = BitConverter.ToInt32(request, currIdx);
                currIdx += 4;
                if (numPadRequests < 0 || numPadRequests > NUMBER_SLOTS)
                    return null;
                int requestsIdx = currIdx;
                for (int i = 0; i < numPadRequests; i++)
                {
                    byte currRequest = request[requestsIdx + i];
                    if (currRequest >= NUMBER_SLOTS)
                        return null;
                }
                for (byte i = 0; i < numPadRequests; i++)
                {
                    byte currRequest = request[requestsIdx + i];
                    CemuhookPadData padData = _getPadDataForSlot(currRequest);
                    byte[] outputData = new byte[16];
                    Array.Copy(BitConverter.GetBytes(0x100001), 0, outputData, 0, 4);
                    outputData[4] = padData.padId;
                    outputData[5] = padData.padState;
                    outputData[6] = padData.model;
                    outputData[7] = padData.connectionType;
                    for (int j = 0; j < 6; j++)
                        outputData[8 + j] = padData.macAddress != null && padData.macAddress.Length == 6 ? padData.macAddress[j] : (byte)0;
                    outputData[14] = padData.batteryStatus;
                    outputData[15] = 0;
                    responses.Add(BuildPacket(outputData, MaxProtocolVersion));
                }
            }
            else if (messageType == 0x100002) // DSUC_PadDataReq
            {
                byte regFlags = request[currIdx++];
                byte idToReg = request[currIdx++];
                byte[] macBytes = new byte[6];
                Array.Copy(request, currIdx, macBytes, 0, 6);
                currIdx += 6;
                // For simplicity, always respond with all pads
                for (int i = 0; i < NUMBER_SLOTS; i++)
                {
                    CemuhookPadData padData = _getPadDataForSlot(i);
                    PadDataRspPacket packet = new PadDataRspPacket();
                    BuildPadDataRspPacket(ref packet, padData, _serverId, _packetCounter++);
                    byte[] buf = new byte[DATA_RSP_PACKET_LEN];
                    CopyBytes(ref packet, buf, DATA_RSP_PACKET_LEN);
                    FixPacketCrc(buf);
                    responses.Add(buf);
                }
            }
            return responses.Count > 0 ? responses.ToArray() : null;
        }

        private byte[] BuildPacket(byte[] usefulData, ushort reqProtocolVersion)
        {
            byte[] packetData = new byte[usefulData.Length + 16];
            int currIdx = 0;
            packetData[currIdx++] = (byte)'D';
            packetData[currIdx++] = (byte)'S';
            packetData[currIdx++] = (byte)'U';
            packetData[currIdx++] = (byte)'S';
            Array.Copy(BitConverter.GetBytes(reqProtocolVersion), 0, packetData, currIdx, 2);
            currIdx += 2;
            Array.Copy(BitConverter.GetBytes((ushort)(packetData.Length - 16)), 0, packetData, currIdx, 2);
            currIdx += 2;
            Array.Clear(packetData, currIdx, 4); // CRC placeholder
            currIdx += 4;
            Array.Copy(BitConverter.GetBytes(_serverId), 0, packetData, currIdx, 4);
            currIdx += 4;
            Array.Copy(usefulData, 0, packetData, currIdx, usefulData.Length);
            FixPacketCrc(packetData);
            return packetData;
        }

        private void FixPacketCrc(byte[] packetBuf)
        {
            Array.Clear(packetBuf, 8, 4);
            uint crcCalc = Crc32(packetBuf, packetBuf.Length);
            Array.Copy(BitConverter.GetBytes(crcCalc), 0, packetBuf, 8, 4);
        }

        private unsafe void BuildPadDataRspPacket(ref PadDataRspPacket packet, CemuhookPadData padData, uint serverId, uint packetCounter)
        {
            packet.initCode[0] = (byte)'D';
            packet.initCode[1] = (byte)'S';
            packet.initCode[2] = (byte)'U';
            packet.initCode[3] = (byte)'S';
            packet.protocolVersion = MaxProtocolVersion;
            packet.messageLen = (ushort)(DATA_RSP_PACKET_LEN - 16);
            packet.crc = 0;
            packet.serverId = serverId;
            packet.messageType = 0x100002;
            packet.padId = padData.padId;
            packet.padState = padData.padState;
            packet.model = padData.model;
            packet.connectionType = padData.connectionType;
            for (int i = 0; i < 6; i++)
                packet.address[i] = padData.macAddress != null && padData.macAddress.Length == 6 ? padData.macAddress[i] : (byte)0;
            packet.batteryStatus = padData.batteryStatus;
            packet.isActive = padData.isActive;
            packet.packetCounter = packetCounter;
            // Map Pokeball data to DS4 fields (simple mapping)
            packet.lx = (byte)(Math.Clamp((padData.axisX + 1f) * 127.5f, 0, 255));
            packet.ly = (byte)(255 - Math.Clamp((padData.axisY + 1f) * 127.5f, 0, 255));
            packet.rx = 128;
            packet.ry = 128;
            packet.buttons1 = (byte)(padData.buttons & 0xFF);
            packet.buttons2 = (byte)((padData.buttons >> 8) & 0xFF);
            packet.psButton = 0;
            packet.touchButton = 0;
            packet.dpadLeft = (padData.buttons & 0x01) != 0 ? (byte)0xFF : (byte)0x00;
            packet.dpadDown = (padData.buttons & 0x02) != 0 ? (byte)0xFF : (byte)0x00;
            packet.dpadRight = (padData.buttons & 0x04) != 0 ? (byte)0xFF : (byte)0x00;
            packet.dpadUp = (padData.buttons & 0x08) != 0 ? (byte)0xFF : (byte)0x00;
            packet.square = 0;
            packet.cross = (padData.buttons & 0x01) != 0 ? (byte)0xFF : (byte)0x00;
            packet.circle = (padData.buttons & 0x02) != 0 ? (byte)0xFF : (byte)0x00;
            packet.triangle = 0;
            packet.r1 = 0;
            packet.l1 = 0;
            packet.r2 = 0;
            packet.l2 = 0;
            packet.touch1Active = 0;
            packet.touch1PacketId = 0;
            packet.touch1X = 0;
            packet.touch1Y = 0;
            packet.touch2Active = 0;
            packet.touch2PacketId = 0;
            packet.touch2X = 0;
            packet.touch2Y = 0;
            packet.totalMicroSec = padData.timestamp;
            packet.accelXG = padData.accelX * 10f;
            packet.accelYG = padData.accelY * 10f;
            packet.accelZG = padData.accelZ * 10f;
            packet.angVelPitch = padData.gyroX * 20f;
            packet.angVelYaw = padData.gyroY * 20f;
            packet.angVelRoll = padData.gyroZ * 20f;
        }

        private unsafe void CopyBytes(ref PadDataRspPacket outReport, byte[] outBuffer, int bufferLen)
        {
            GCHandle h = GCHandle.Alloc(outReport, GCHandleType.Pinned);
            Marshal.Copy(h.AddrOfPinnedObject(), outBuffer, 0, bufferLen);
            h.Free();
        }

        private static uint Crc32(byte[] data, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
            }
            return ~crc;
        }

        public void Dispose()
        {
            _running = false;
            _udpServer.Close();
            _udpServer.Dispose();
        }
    }
}
