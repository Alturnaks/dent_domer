using Dental.Storage;
using Xunit;

namespace Dental.EntityFrameworkCore.Storage;
public class FileRulesTests
{
    [Theory]
    [InlineData("scan.PDF", "application/pdf", true)]
    [InlineData("scan.jpg", "image/jpeg", true)]
    [InlineData("scan.png", "image/png", true)]
    [InlineData("scan.pdf.exe", "application/pdf", false)]
    [InlineData("scan.pdf", "image/png", false)]
    [InlineData("scan.svg", "image/svg+xml", false)]
    public void ExtensionAndMimeMustMatch(string name, string mime, bool expected) => Assert.Equal(expected, FileRules.IsAllowed(name, mime));
    [Fact]
    public void RejectDisguisedContent()
    {
        Assert.True(FileRules.Matches("%PDF-1.7"u8.ToArray(), "application/pdf"));
        Assert.False(FileRules.Matches("<script>"u8.ToArray(), "application/pdf"));
        Assert.False(FileRules.Matches(new byte[] { 137,80,78 }, "image/png"));
        Assert.True(FileRules.Matches(new byte[] { 137,80,78,71,13,10,26,10 }, "image/png"));
        Assert.True(FileRules.Matches(new byte[] { 255,216,255 }, "image/jpeg"));
    }
}
