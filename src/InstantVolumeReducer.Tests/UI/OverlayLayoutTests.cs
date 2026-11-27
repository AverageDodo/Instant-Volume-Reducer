using System.Drawing;
using InstantVolumeReducer.Settings;
using InstantVolumeReducer.UI;

namespace InstantVolumeReducer.Tests.UI;

public class OverlayLayoutTests
{
    // A 1920×1080 monitor with a 48 px taskbar at the bottom.
    private static readonly Rectangle WorkingArea = new(0, 0, 1920, 1032);

    [Theory]
    [InlineData(OverlayCorner.TopLeft, 16, 16)]
    [InlineData(OverlayCorner.TopRight, 1920 - 16 - 48, 16)]
    [InlineData(OverlayCorner.BottomLeft, 16, 1032 - 16 - 48)]
    [InlineData(OverlayCorner.BottomRight, 1920 - 16 - 48, 1032 - 16 - 48)]
    public void Place_PutsIconInCornerOfWorkingArea(OverlayCorner corner, int x, int y)
    {
        var bounds = OverlayLayout.Place(WorkingArea, corner, OverlaySize.Medium, dpi: 96);

        Assert.Equal(new Rectangle(x, y, 48, 48), bounds);
    }

    [Theory]
    [InlineData(OverlaySize.Small, 96, 32)]
    [InlineData(OverlaySize.Medium, 96, 48)]
    [InlineData(OverlaySize.Large, 96, 64)]
    [InlineData(OverlaySize.Medium, 144, 72)] // 150 %
    [InlineData(OverlaySize.Small, 120, 40)]  // 125 %
    [InlineData(OverlaySize.Large, 192, 128)] // 200 %
    public void Place_ScalesSizeWithDpi(OverlaySize size, int dpi, int edge)
    {
        var bounds = OverlayLayout.Place(WorkingArea, OverlayCorner.TopLeft, size, dpi);

        Assert.Equal((edge, edge), (bounds.Width, bounds.Height));
    }

    [Fact]
    public void Place_ScalesMarginWithDpi()
    {
        var bounds = OverlayLayout.Place(WorkingArea, OverlayCorner.BottomRight, OverlaySize.Medium, dpi: 144);

        // 72 px icon, 24 px margin at 150 %.
        Assert.Equal(new Rectangle(1920 - 24 - 72, 1032 - 24 - 72, 72, 72), bounds);
    }

    [Fact]
    public void Place_SecondaryMonitorWithNegativeCoordinates()
    {
        // A monitor to the left of and above the primary one, taskbar on its left edge.
        var workingArea = new Rectangle(-2500, -300, 2500, 1440);

        var bounds = OverlayLayout.Place(workingArea, OverlayCorner.TopRight, OverlaySize.Small, dpi: 96);

        Assert.Equal(new Rectangle(-16 - 32, -300 + 16, 32, 32), bounds);
    }

    [Fact]
    public void Place_TinyWorkingArea_ShrinksMarginAndStaysInside()
    {
        var workingArea = new Rectangle(100, 200, 60, 40);

        var bounds = OverlayLayout.Place(workingArea, OverlayCorner.BottomRight, OverlaySize.Medium, dpi: 96);

        Assert.Equal(48, bounds.Width);
        Assert.Equal(new Point(100 + 6, 200), bounds.Location);
    }

    [Theory]
    [InlineData(0, 96)]
    [InlineData(-5, 96)]
    public void Scale_InvalidDpi_TreatedAs100Percent(int dpi, int expectedDpi)
    {
        Assert.Equal(OverlayLayout.Scale(48, expectedDpi), OverlayLayout.Scale(48, dpi));
    }

    [Theory]
    [InlineData(true, 85, 217)]
    [InlineData(true, 20, 51)]
    [InlineData(false, 85, OverlayLayout.IdleAlpha)]
    [InlineData(false, 100, OverlayLayout.IdleAlpha)]
    public void WindowAlpha_VisibleOnlyWhileReduced(bool reduced, int percent, byte alpha)
    {
        Assert.Equal(alpha, OverlayLayout.WindowAlpha(reduced, percent));
    }

    [Fact]
    public void IdleAlpha_IsNotZero_SoTheWindowStillCountsAsVisible()
    {
        Assert.True(OverlayLayout.IdleAlpha > 0);
    }

    [Theory]
    [InlineData(100, 255)]
    [InlineData(85, 217)]
    [InlineData(20, 51)]
    [InlineData(5, 51)]    // Clamped to the minimum.
    [InlineData(150, 255)] // Clamped to the maximum.
    public void Alpha_ConvertsPercent(int percent, byte alpha)
    {
        Assert.Equal(alpha, OverlayLayout.Alpha(percent));
    }
}
