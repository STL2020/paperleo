using PaperlessAiCore.Core;
using Xunit;

namespace PaperlessAiCore.Core.Tests;

public class LicenseCheckTests
{
    // Hinweis: Seit der Umstellung auf PayHip-Verifizierung gibt es keine lokal generierten
    // Lizenzschlüssel mehr. IsProMode() prüft hier nur das Format (Schnellcheck fürs UI-Rendering,
    // z.B. ob der "Pro"-Badge angezeigt wird), die eigentliche Prüfung läuft online über
    // VerifyWithPayHipAsync() beim Aktivieren des Keys.

    [Fact]
    public void EmptyKey_IsCommunityMode()
    {
        Assert.False(LicenseCheck.IsProMode(null));
        Assert.False(LicenseCheck.IsProMode(""));
        Assert.False(LicenseCheck.IsProMode("   "));
    }

    [Fact]
    public void ValidFormatKey_PassesQuickCheck()
    {
        Assert.True(LicenseCheck.IsProMode("PAPERLEO-PRO-ABCD-1234-EFGH"));
    }

    [Fact]
    public void TooShortKey_FailsQuickCheck()
    {
        Assert.False(LicenseCheck.IsKeyFormatValid("abc"));
    }
}

public class ToolsTests
{
    [Theory]
    [InlineData("Rechnung - Telekom - 45,90 €", 45.90)]
    [InlineData("Mahnung - Stadtwerke Musterstadt - 1.234,56 €", 1234.56)]
    [InlineData("Rechnung - Amazon - 0,00 €", 0.0)]
    public void ParseAmountFromTitle_ExtractsCorrectAmount(string title, double expected)
    {
        var amount = Tools.ParseAmountFromTitle(title);
        Assert.NotNull(amount);
        Assert.Equal(expected, amount!.Value, precision: 2);
    }

    [Fact]
    public void ParseAmountFromTitle_ReturnsNull_WhenNoAmountPresent()
    {
        Assert.Null(Tools.ParseAmountFromTitle("Irgendein Titel ohne Betrag"));
        Assert.Null(Tools.ParseAmountFromTitle(null));
    }
}
