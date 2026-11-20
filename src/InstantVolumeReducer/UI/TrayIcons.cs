using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace InstantVolumeReducer.UI;

/// <summary>
/// The two tray icons (normal and reduced), drawn at runtime with GDI+ at the system's small-icon size.
/// Drawing them avoids shipping binary <c>.ico</c> files and keeps them sharp at any DPI scaling.
/// </summary>
internal sealed class TrayIcons : IDisposable
{
    /// <summary>The icons are designed on a 16×16 grid and scaled to the actual icon size.</summary>
    private const float DesignSize = 16f;

    private static readonly Color NormalColor = Color.FromArgb(0x2D, 0x7D, 0xD2);
    private static readonly Color ReducedColor = Color.FromArgb(0xE8, 0x59, 0x0C);

    public TrayIcons()
        : this(SystemInformation.SmallIconSize)
    {
    }

    public TrayIcons(Size size)
    {
        Normal = CreateIcon(size, reduced: false);
        Reduced = CreateIcon(size, reduced: true);
    }

    /// <summary>Blue speaker with two sound waves: the volume is at its normal level.</summary>
    public Icon Normal { get; }

    /// <summary>Orange speaker with a downward arrow: the volume is reduced.</summary>
    public Icon Reduced { get; }

    public void Dispose()
    {
        Normal.Dispose();
        Reduced.Dispose();
    }

    private static Icon CreateIcon(Size size, bool reduced)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            graphics.ScaleTransform(size.Width / DesignSize, size.Height / DesignSize);

            var color = reduced ? ReducedColor : NormalColor;
            DrawSpeaker(graphics, color);
            if (reduced)
            {
                DrawDownArrow(graphics, color);
            }
            else
            {
                DrawSoundWaves(graphics, color);
            }
        }

        // GetHicon creates an unmanaged icon that Icon.FromHandle doesn't take ownership of. Clone it into
        // an Icon that owns its own copy, then free the original handle so nothing leaks.
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawSpeaker(Graphics graphics, Color color)
    {
        using var brush = new SolidBrush(color);
        PointF[] speaker =
        [
            new(1f, 5.5f),
            new(4.5f, 5.5f),
            new(8.5f, 2f),
            new(8.5f, 14f),
            new(4.5f, 10.5f),
            new(1f, 10.5f),
        ];
        graphics.FillPolygon(brush, speaker);
    }

    private static void DrawSoundWaves(Graphics graphics, Color color)
    {
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(pen, 6.5f, 5f, 6f, 6f, -50f, 100f);
        graphics.DrawArc(pen, 5f, 2f, 10f, 12f, -50f, 100f);
    }

    private static void DrawDownArrow(Graphics graphics, Color color)
    {
        using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.DrawLine(pen, 12.5f, 3f, 12.5f, 13f);
        graphics.DrawLines(pen, [new PointF(10f, 10.5f), new PointF(12.5f, 13f), new PointF(15f, 10.5f)]);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
