using System.Net;
using System.Net.Sockets;
using GTMP.Shared.Protocol;

namespace GTMP.Server.Core.Networking;

/// <summary>
/// Minimal UDP endpoint for the game protocol: binds a socket, receives datagrams on a
/// background thread and hands decoded packets to a handler.
/// </summary>
/// <remarks>
/// Datagrams that do not match the header, or that arrive after a session was torn
/// down, are dropped and counted rather than surfaced. Untrusted input reaches this
/// class directly, so everything that can be malformed must be survivable.
/// </remarks>
public sealed class UdpServer : IDisposable
{
    private const int ReceiveBufferSize = 2048;

    private readonly Action<IPEndPoint, PacketChannel, PacketType, ReadOnlyMemory<byte>> _onPacket;
    private readonly Action<string, Exception?> _onError;

    private Socket? _socket;
    private Thread? _receiveThread;
    private volatile bool _isRunning;

    private long _droppedPackets;

    public UdpServer(
        Action<IPEndPoint, PacketChannel, PacketType, ReadOnlyMemory<byte>> onPacket,
        Action<string, Exception?> onError)
    {
        _onPacket = onPacket;
        _onError = onError;
    }

    public bool IsRunning => _isRunning;

    public long DroppedPackets => Interlocked.Read(ref _droppedPackets);

    public void Start(int port)
    {
        if (_isRunning) throw new InvalidOperationException("The UDP server is already running.");

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));

        _isRunning = true;
        _receiveThread = new Thread(ReceiveLoop)
        {
            Name = "GTMP UDP receive",
            IsBackground = true
        };
        _receiveThread.Start();
    }

    public void Send(IPEndPoint endPoint, PacketChannel channel, PacketType type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > PacketHeader.MaxPayloadSize)
            throw new ProtocolException($"Payload of {payload.Length} bytes exceeds the {PacketHeader.MaxPayloadSize} byte limit.");

        var datagram = new byte[PacketHeader.Size + payload.Length];

        PacketHeader.Write(datagram, channel, type, payload.Length);
        payload.CopyTo(datagram.AsSpan(PacketHeader.Size));

        SendRaw(endPoint, datagram);
    }

    private void SendRaw(IPEndPoint endPoint, byte[] datagram)
    {
        var socket = _socket;

        if (socket is null || !_isRunning)
            throw new InvalidOperationException("The UDP server is not running.");

        try
        {
            // The explicit (buffer, offset, length) overload is used on purpose: the
            // span based one is ambiguous against the ArraySegment overloads.
            socket.SendTo(datagram, 0, datagram.Length, SocketFlags.None, endPoint);
        }
        catch (SocketException exception)
        {
            // A dead peer or a transient network error must not stop the tick loop.
            _onError($"Failed to send to {endPoint}.", exception);
        }
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[ReceiveBufferSize];

        while (_isRunning)
        {
            try
            {
                // The explicit (buffer, offset, length, flags, ref endpoint) form is used on
                // purpose: the span based overloads are not available on every target and
                // bind ambiguously against the ref EndPoint variants.
                EndPoint anySender = new IPEndPoint(IPAddress.Any, 0);

                var received = _socket!.ReceiveFrom(
                    buffer, 0, buffer.Length, SocketFlags.None, ref anySender);

                var sender = (IPEndPoint)anySender;

                if (received < PacketHeader.Size)
                {
                    Interlocked.Increment(ref _droppedPackets);
                    continue;
                }

                if (!PacketHeader.TryRead(buffer.AsSpan(0, received), out var channel, out var type, out var payloadLength))
                {
                    Interlocked.Increment(ref _droppedPackets);
                    continue;
                }

                var payload = new byte[payloadLength];
                Array.Copy(buffer, PacketHeader.Size, payload, 0, payloadLength);

                _onPacket(sender, channel, type, payload);
            }
            catch (SocketException) when (!_isRunning)
            {
                // Expected: the socket was closed while blocked in ReceiveFrom.
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                _onError("Error while receiving a datagram.", exception);
            }
        }
    }

    public void Dispose()
    {
        _isRunning = false;

        try
        {
            // Shutting the socket down unblocks the receive thread, otherwise Dispose
            // would have to wait for a datagram that may never arrive.
            _socket?.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
            // Already down.
        }

        _socket?.Dispose();
        _socket = null;

        _receiveThread?.Join(TimeSpan.FromSeconds(2));
        _receiveThread = null;
    }
}