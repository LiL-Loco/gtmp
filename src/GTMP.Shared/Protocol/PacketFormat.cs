using System.Buffers.Binary;
using System.Text;

namespace GTMP.Shared.Protocol;

/// <summary>
/// Fixed size datagram header that precedes every payload.
/// </summary>
/// <remarks>
/// Layout, little endian:
/// <code>
/// 0  magic (ushort)     0x4754, guards against unrelated UDP traffic
/// 1  protocol version   bumped whenever the contract changes incompatibly
/// 2  channel            see <see cref="PacketChannel"/>
/// 3  type               see <see cref="PacketType"/>
/// 4  payload length     ushort, capped so a datagram always fits one MTU
/// </code>
/// </remarks>
public static class PacketHeader
{
    /// <summary>'G' 'T' - identifies our traffic.</summary>
    public const ushort Magic = 0x4754;

    public const byte ProtocolVersion = 1;

    public const int Size = 8;

    /// <summary>
    /// 1400 bytes leaves headroom below the 1500 byte MTU for IP and UDP headers, so a
    /// payload is never fragmented. Anything larger has to be chunked by the sender.
    /// </summary>
    public const int MaxPayloadSize = 1400;

    public static void Write(Span<byte> destination, PacketChannel channel, PacketType type, int payloadLength)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, Magic);
        destination[2] = ProtocolVersion;
        destination[3] = (byte)channel;
        destination[4] = (byte)type;
        destination[5] = 0; // reserved for flags
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], (ushort)payloadLength);
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out PacketChannel channel, out PacketType type, out int payloadLength)
    {
        channel = default;
        type = default;
        payloadLength = 0;

        if (source.Length < Size) return false;

        if (BinaryPrimitives.ReadUInt16LittleEndian(source) != Magic) return false;

        if (source[2] != ProtocolVersion) return false;

        var payload = BinaryPrimitives.ReadUInt16LittleEndian(source[6..]);

        // Trust the length only after bounding it against what was actually received.
        if (payload > MaxPayloadSize) return false;
        if (source.Length < Size + payload) return false;

        channel = (PacketChannel)source[3];
        type = (PacketType)source[4];
        payloadLength = payload;

        return true;
    }
}

/// <summary>
/// Length-prefixed little endian writer for packet payloads.
/// </summary>
public sealed class PacketWriter
{
    private byte[] _buffer;
    private int _length;

    public PacketWriter(int capacity = PacketHeader.MaxPayloadSize)
    {
        _buffer = new byte[Math.Max(capacity, 32)];
    }

    public int Length => _length;

    public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

    private void EnsureCapacity(int additionalBytes)
    {
        if (_length + additionalBytes <= _buffer.Length) return;

        var size = _buffer.Length;
        while (size < _length + additionalBytes) size *= 2;

        Array.Resize(ref _buffer, size);
    }

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteInt16(short value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_length), value);
        _length += 2;
    }

    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length), value);
        _length += 2;
    }

    public void WriteInt32(int value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_length), value);
        _length += 4;
    }

    public void WriteUInt32(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_length), value);
        _length += 4;
    }

    public void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

    /// <summary>Writes a length-prefixed UTF-8 string. Null is written as length -1.</summary>
    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteInt32(-1);
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(byteCount);

        EnsureCapacity(byteCount);
        Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_length));
        _length += byteCount;
    }

    /// <summary>Writes a string with a hard cap, so a hostile name cannot blow up a packet.</summary>
    public void WriteString(string? value, int maxByteCount)
    {
        if (value is null)
        {
            WriteInt32(-1);
            return;
        }

        var truncated = Encoding.UTF8.GetByteCount(value) > maxByteCount
            ? Truncate(value, maxByteCount)
            : value;

        WriteString(truncated);
    }

    private static string Truncate(string value, int maxByteCount)
    {
        var length = Math.Min(value.Length, maxByteCount);

        while (length > 0 && Encoding.UTF8.GetByteCount(value.AsSpan(0, length)) > maxByteCount)
            length--;

        return value[..length];
    }
}

/// <summary>Length-prefixed little endian reader, mirror image of <see cref="PacketWriter"/>.</summary>
public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _source;

    private int _position;

    public PacketReader(ReadOnlySpan<byte> source)
    {
        _source = source;
        _position = 0;
    }

    public int Remaining => _source.Length - _position;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (Remaining < count)
            throw new ProtocolException($"Packet truncated: wanted {count} bytes, {Remaining} left.");

        var slice = _source.Slice(_position, count);
        _position += count;
        return slice;
    }

    public byte ReadByte() => Take(1)[0];

    public bool ReadBool() => ReadByte() != 0;

    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

    public string? ReadString()
    {
        var byteCount = ReadInt32();

        if (byteCount < 0) return null;
        if (byteCount > Remaining)
            throw new ProtocolException($"Packet declares a {byteCount} byte string but only {Remaining} bytes remain.");

        return Encoding.UTF8.GetString(Take(byteCount));
    }
}

/// <summary>Raised when a datagram does not match the protocol contract.</summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message)
    {
    }
}