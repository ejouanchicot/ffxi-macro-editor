using FfxiMacros.Core.Text;
using Xunit;

namespace FfxiMacros.Tests;

public class FfxiTextTests
{
    [Fact]
    public void Decode_StripsTrailingPadding()
    {
        byte[] field = new byte[61];
        "/ma \"Cure IV\" <t>"u8.CopyTo(field);

        Assert.Equal("/ma \"Cure IV\" <t>", FfxiText.Decode(field));
    }

    [Fact]
    public void Decode_OfAnEmptyFieldIsAnEmptyString()
    {
        Assert.Equal("", FfxiText.Decode(new byte[61]));
    }

    [Fact]
    public void Decode_TurnsAnAutoTranslatePhraseIntoAnEscape()
    {
        byte[] field = [0xFD, 0x02, 0x02, 0x1F, 0x97, 0xFD, 0, 0];

        Assert.Equal("«02021F97»", FfxiText.Decode(field));
    }

    [Fact]
    public void Decode_KeepsInteriorNulsAsData()
    {
        byte[] field = new byte[9];
        field[0] = 0x00;
        "abc"u8.CopyTo(field.AsSpan(1));

        Assert.Equal("{00}abc", FfxiText.Decode(field));
    }

    [Fact]
    public void Decode_EscapesALiteralBrace()
    {
        Assert.Equal("a{{b", FfxiText.Decode("a{b"u8.ToArray()));
    }

    [Fact]
    public void Decode_EscapesAnUnpairedAutoTranslateMarker()
    {
        Assert.Equal("{FD}ab", FfxiText.Decode(new byte[] { 0xFD, (byte)'a', (byte)'b' }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/ja \"Provoke\" <t>")]
    [InlineData("«02021F97»")]
    [InlineData("/ja \"«02021F97»\" <t>")]
    [InlineData("{00}con send Sylvane Erase <laststid>")]
    [InlineData("a{{b}c")]
    [InlineData("{FD}{FE}{FF}{01}")]
    [InlineData("/item \"«070209BA»\" <t>")]
    public void EncodeThenDecode_IsIdentity(string text)
    {
        byte[] field = FfxiText.Encode(text, 61);

        Assert.Equal(61, field.Length);
        Assert.Equal(text, FfxiText.Decode(field));
    }

    [Fact]
    public void Encode_PadsWithNuls()
    {
        byte[] field = FfxiText.Encode("ab", 9);

        Assert.Equal(new byte[] { (byte)'a', (byte)'b', 0, 0, 0, 0, 0, 0, 0 }, field);
    }

    [Fact]
    public void Encode_AcceptsExactlyFieldSizeMinusOneBytes()
    {
        byte[] field = FfxiText.Encode(new string('a', 60), 61);

        Assert.Equal(0, field[60]);
        Assert.Equal(new string('a', 60), FfxiText.Decode(field));
    }

    [Fact]
    public void Encode_RejectsOneByteTooMany()
    {
        Assert.Throws<FfxiTextException>(() => FfxiText.Encode(new string('a', 61), 61));
    }

    [Fact]
    public void Encode_RejectsNonAsciiCharacters()
    {
        var ex = Assert.Throws<FfxiTextException>(() => FfxiText.Encode("café", 61));
        Assert.Contains("not supported", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_RejectsAnUnterminatedEscape()
    {
        Assert.Throws<FfxiTextException>(() => FfxiText.Encode("abc{02", 61));
    }

    [Fact]
    public void Encode_RejectsAMalformedAutoTranslateEscape()
    {
        Assert.Throws<FfxiTextException>(() => FfxiText.Encode("«0202»", 61));
    }

    [Fact]
    public void Encode_RejectsAnUnknownEscape()
    {
        Assert.Throws<FfxiTextException>(() => FfxiText.Encode("{HELLO}", 61));
    }

    [Fact]
    public void Encode_AcceptsDashesInsideAnAutoTranslateEscape()
    {
        Assert.Equal(
            FfxiText.Encode("«02021F97»", 61),
            FfxiText.Encode("{AT:02-02-1F-97}", 61));
    }

    [Fact]
    public void MeasureBytes_CountsAPhraseAsSixBytes()
    {
        Assert.Equal(6, FfxiText.MeasureBytes("«02021F97»"));
        Assert.Equal(1, FfxiText.MeasureBytes("{00}"));
        Assert.Equal(1, FfxiText.MeasureBytes("{{"));
        Assert.Equal(3, FfxiText.MeasureBytes("abc"));
    }

    [Fact]
    public void TryEncode_ReportsTheErrorInsteadOfThrowing()
    {
        Assert.False(FfxiText.TryEncode(new string('a', 99), 61, out _, out string? error));
        Assert.NotNull(error);

        Assert.True(FfxiText.TryEncode("/echo hi", 61, out _, out error));
        Assert.Null(error);
    }

    [Fact]
    public void Fits_ChecksAgainstTheUsableFieldSize()
    {
        Assert.True(FfxiText.Fits("12345678", 9));
        Assert.False(FfxiText.Fits("123456789", 9));
        Assert.False(FfxiText.Fits("{bad}", 61));
    }
}
