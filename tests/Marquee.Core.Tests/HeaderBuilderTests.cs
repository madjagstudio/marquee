using System.Buffers.Binary;
using Marquee.Core.Devices.UniversalScreen88;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class HeaderBuilderTests
{
    [Fact]
    public void GetVer_at_timestamp_1_matches_known_answer()
    {
        var packet = new HeaderBuilder(() => 1).Build(Command.GetVer);

        Assert.Equal(Convert.FromHexString("f132a5d4e3cff85748b82aaacac68f8a"), packet[..16]);
    }

    [Fact]
    public void Packet_is_512_bytes_with_zero_gap_and_trailer()
    {
        var packet = new HeaderBuilder(() => 1).Build(Command.StopPlay);

        Assert.Equal(HeaderBuilder.PacketLength, packet.Length);
        Assert.All(packet[504..510], b => Assert.Equal(0, b));
        Assert.Equal(0xA1, packet[510]);
        Assert.Equal(0x1A, packet[511]);
    }

    [Fact]
    public void Plaintext_has_command_magic_timestamp_and_params()
    {
        var plain = new HeaderBuilder(() => 0x01020304)
            .BuildPlaintext(Command.PushJpg, CommandParams.Length(123456));

        Assert.Equal(500, plain.Length);
        Assert.Equal(0x65, plain[0]);
        Assert.Equal(0x00, plain[1]);
        Assert.Equal(new byte[] { 0x1A, 0x6D }, plain[2..4]);
        Assert.Equal(new byte[] { 0x04, 0x03, 0x02, 0x01 }, plain[4..8]);
        Assert.Equal(new byte[] { 0x00, 0x01, 0xE2, 0x40 }, plain[8..12]);
        Assert.All(plain[12..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Encrypted_packet_decrypts_to_plaintext()
    {
        var packet = new HeaderBuilder(() => 42).Build(Command.Brightness, CommandParams.Byte(55));
        var plain = Packets.DecryptHeader(packet);

        Assert.Equal(0x0E, plain[0]);
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(4)));
        Assert.Equal(55, plain[8]);
    }

    [Fact]
    public void Timestamps_strictly_increase_when_clock_stalls()
    {
        var builder = new HeaderBuilder(() => 5);

        var stamps = Enumerable.Range(0, 3)
            .Select(_ => BinaryPrimitives.ReadUInt32LittleEndian(builder.BuildPlaintext(Command.StopPlay, default).AsSpan(4)))
            .ToArray();

        Assert.Equal(new uint[] { 5, 6, 7 }, stamps);
    }

    [Fact]
    public void Too_many_parameters_throws()
    {
        var builder = new HeaderBuilder(() => 1);

        Assert.Throws<ArgumentException>(() => builder.Build(Command.StopPlay, new byte[493]));
    }
}
