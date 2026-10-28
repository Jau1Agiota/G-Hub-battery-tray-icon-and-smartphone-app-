using System.Drawing;
using PeripheralBatteryCompanion.Core.Presentation;
using Xunit;
namespace PeripheralBatteryCompanion.Tests;
public sealed class DashboardLayoutTests
{
    [Fact]
    public void NominalVisibleBudgetIsExactly520By570()
    {
        Assert.Equal(new Size(520,570),DashboardLayout.NominalSize(96));
        Assert.Equal(new Rectangle(16,16,488,64),DashboardLayout.Header(96,520));
        for(var i=0;i<3;i++) Assert.Equal(new Rectangle(16,90+i*142,488,132),DashboardLayout.Card(i,96,520));
        Assert.Equal(new Rectangle(16,516,488,44),DashboardLayout.Footer(96,520));
        Assert.Equal(new Rectangle(12,10,180,112),DashboardLayout.ImageViewport(96,488));
        Assert.Equal(new Rectangle(204,10,272,112),DashboardLayout.TextRegion(96,488));
    }
    [Theory]
    [InlineData(96)] [InlineData(120)] [InlineData(144)] [InlineData(192)]
    public void DpiScaledGeometryHasNoOverlapAndIncludesFooter(int dpi)
    {
        var size=DashboardLayout.NominalSize(dpi);var outer=new Rectangle(Point.Empty,size);
        var previous=DashboardLayout.Header(dpi,size.Width);
        for(var i=0;i<3;i++)
        {
            var card=DashboardLayout.Card(i,dpi,size.Width);
            Assert.True(outer.Contains(card));Assert.True(card.Top>=previous.Bottom);
            var viewport=DashboardLayout.ImageViewport(dpi,card.Width);var text=DashboardLayout.TextRegion(dpi,card.Width);
            Assert.True(viewport.Right<text.Left);Assert.True(new Rectangle(Point.Empty,card.Size).Contains(viewport));
            previous=card;
        }
        var footer=DashboardLayout.Footer(dpi,size.Width);
        Assert.True(outer.Contains(footer));Assert.True(footer.Top>=previous.Bottom);
    }
    [Theory]
    [InlineData(1672,910)] [InlineData(441,410)] [InlineData(429,410)]
    public void AlphaBoundContainPreservesAspectRatioAndEntireSilhouette(int width,int height)
    {
        var viewport=new RectangleF(12,10,180,112);
        var result=DashboardLayout.Contain(new(100,20,width,height),viewport);
        Assert.True(viewport.Contains(result));
        Assert.Equal(width/(double)height,result.Width/result.Height,5);
        Assert.True(Math.Abs(result.Width-viewport.Width)<.01 || Math.Abs(result.Height-viewport.Height)<.01);
    }
}
