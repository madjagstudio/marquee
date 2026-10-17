using Marquee.Core.Devices.UniversalScreen88;

namespace Marquee.Core.Tests;

public class CommandParamsTests
{
    [Fact]
    public void Byte_is_single_byte() => Assert.Equal(new byte[] { 0x78 }, CommandParams.Byte(120));

    [Fact]
    public void Length_is_big_endian_u32() =>
        Assert.Equal(new byte[] { 0x00, 0x07, 0xD0, 0x00 }, CommandParams.Length(512_000));

    [Fact]
    public void Clock_is_big_endian_year_then_fields_then_mode_2() =>
        Assert.Equal(
            new byte[] { 0x07, 0xEA, 10, 16, 18, 30, 5, 2 },
            CommandParams.Clock(new DateTime(2026, 10, 16, 18, 30, 5)));
}
