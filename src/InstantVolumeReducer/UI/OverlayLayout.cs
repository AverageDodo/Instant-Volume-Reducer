using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.UI;

/// <summary>
/// Where the overlay icon goes and how big it is, in physical pixels. Kept apart from the window code so
/// it can be unit-tested.
/// </summary>
public static class OverlayLayout
{
    /// <summary>Windows' reference DPI (100 % scaling).</summary>
    public const int BaseDpi = 96;

    /// <summary>The gap between the icon and the edges of the working area, at 100 % scaling.</summary>
    public const int LogicalMargin = 16;

    /// <summary>The icon's edge length at 100 % scaling.</summary>
    public static int LogicalSize(OverlaySize size) => size switch
    {
        OverlaySize.Small => 32,
        OverlaySize.Large => 64,
        _ => 48,
    };

    /// <summary>Scales a length from 100 % to the monitor's DPI, rounding to the nearest pixel.</summary>
    public static int Scale(int logical, int dpi)
    {
        var effectiveDpi = dpi > 0 ? dpi : BaseDpi;
        return (int)Math.Round(logical * effectiveDpi / (double)BaseDpi, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The icon's bounds in a corner of <paramref name="workingArea"/> (the monitor without the taskbar), so
    /// it never covers the taskbar. If the working area is too small for the margin, the icon moves closer
    /// to the edge; it always stays inside the working area's top-left corner.
    /// </summary>
    public static Rectangle Place(Rectangle workingArea, OverlayCorner corner, OverlaySize size, int dpi)
    {
        var edge = Scale(LogicalSize(size), dpi);
        var margin = Scale(LogicalMargin, dpi);
        var marginX = Math.Clamp((workingArea.Width - edge) / 2, 0, margin);
        var marginY = Math.Clamp((workingArea.Height - edge) / 2, 0, margin);

        var left = corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft;
        var top = corner is OverlayCorner.TopLeft or OverlayCorner.TopRight;
        var x = left ? workingArea.Left + marginX : workingArea.Right - marginX - edge;
        var y = top ? workingArea.Top + marginY : workingArea.Bottom - marginY - edge;

        return new Rectangle(Math.Max(x, workingArea.Left), Math.Max(y, workingArea.Top), edge, edge);
    }

    /// <summary>
    /// The window alpha while the overlay is kept ready but the volume isn't reduced. 1 of 255 is
    /// invisible to the eye, but unlike 0 the window still counts as visible content, so Windows keeps
    /// composing it with whatever is underneath and nothing changes when it becomes visible.
    /// </summary>
    public const byte IdleAlpha = 1;

    /// <summary>The window alpha for the current state: the opacity setting while reduced, otherwise <see cref="IdleAlpha"/>.</summary>
    public static byte WindowAlpha(bool reduced, int opacityPercent) => reduced ? Alpha(opacityPercent) : IdleAlpha;

    /// <summary>Converts the opacity setting (percent) to a window alpha value (0–255).</summary>
    public static byte Alpha(int opacityPercent)
    {
        var percent = Math.Clamp(opacityPercent, OverlaySettings.MinOpacity, OverlaySettings.MaxOpacity);
        return (byte)Math.Round(percent * 255 / 100.0, MidpointRounding.AwayFromZero);
    }
}
