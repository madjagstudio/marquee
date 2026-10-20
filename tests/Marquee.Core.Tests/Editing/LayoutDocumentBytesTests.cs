using System.Text;
using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// The file as bytes, read and written as the editor does: UTF-8, byte for byte, with its byte order mark if it has one.
public class LayoutDocumentBytesTests
{
    static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Theory]
    [InlineData("")]
    [InlineData("a: 1\n")]
    [InlineData("# ünï · °C 😀 日本語\r\ntext: \"CPU load\"\r\n")]
    public void Reads_and_writes_the_same_bytes(string text)
    {
        foreach (var bytes in new[] { Utf8(text), [.. Bom, .. Utf8(text)] })
            Assert.Equal(bytes, LayoutDocument.FromBytes(bytes).ToBytes());
    }

    [Fact]
    public void Keeps_the_byte_order_mark_through_an_edit()
    {
        var document = LayoutDocument.FromBytes([.. Bom, .. Utf8("a: 1\n")]);

        Assert.Equal('﻿', document.Text[0]);
        Assert.Equal([.. Bom, .. Utf8("a: 2\n")], document.SetScalar(YamlPath.Parse("a"), "2").ToBytes());
    }

    [Fact]
    public void Writes_no_byte_order_mark_the_file_had_not() =>
        Assert.Equal(Utf8("a: é\n"), LayoutDocument.FromBytes(Utf8("a: 1\n")).SetScalar(YamlPath.Parse("a"), "é").ToBytes());

    [Fact]
    public void Refuses_bytes_that_are_not_UTF_8_at_their_line()
    {
        var ex = Assert.Throws<LayoutEditException>(() => LayoutDocument.FromBytes([.. Utf8("a: 1\nb: x"), 0xC3, 0x28, .. Utf8("\n")]));

        Assert.Equal(2, ex.Line);
        Assert.Contains("UTF-8", ex.Reason);
    }

    public static TheoryData<byte[]> Utf16 => new()
    {
        new byte[] { 0xFF, 0xFE, (byte)'a', 0, (byte)':', 0 },
        new byte[] { 0xFE, 0xFF, 0, (byte)'a', 0, (byte)':' },
        new byte[] { 0xFF, 0xFE, 0, 0, (byte)'a', 0, 0, 0 },
        new byte[] { 0, 0, 0xFE, 0xFF, 0, 0, 0, (byte)'a' },
        new byte[] { (byte)'a', 0, (byte)':', 0, (byte)' ', 0, (byte)'1', 0 },
    };

    [Theory]
    [MemberData(nameof(Utf16))]
    public void Refuses_UTF_16_and_UTF_32(byte[] bytes)
    {
        var ex = Assert.Throws<LayoutEditException>(() => LayoutDocument.FromBytes(bytes));

        Assert.Contains("UTF-16", ex.Reason);
    }

    [Fact]
    public void Refuses_what_Open_refuses() =>
        Assert.Throws<LayoutEditException>(() => LayoutDocument.FromBytes(Utf8("a: 1\na: 2\n")));
}
