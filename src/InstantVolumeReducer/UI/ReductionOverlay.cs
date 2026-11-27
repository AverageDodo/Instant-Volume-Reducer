using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using InstantVolumeReducer.Settings;
using Microsoft.Win32;

namespace InstantVolumeReducer.UI;

/// <summary>
/// The on-screen indicator: a small orange speaker badge in a corner of the screen while the volume is
/// reduced. The window is always on top, click-through, never takes the focus, and doesn't appear in the
/// taskbar or Alt+Tab. Create, use and dispose it on the UI thread.
/// </summary>
/// <remarks>
/// It's a per-pixel-alpha layered window (<c>UpdateLayeredWindow</c>), so the badge has smooth, semi-
/// transparent edges. While shown, a timer re-asserts its topmost position (other topmost windows can
/// cover it) and, in <see cref="OverlayMonitor.ActiveWindow"/> mode, moves it to the active window's
/// monitor. Exclusive-fullscreen apps draw over every normal window, so the badge isn't visible there.
/// </remarks>
internal sealed class ReductionOverlay : IDisposable
{
    private const int RefreshIntervalMs = 2000;

    private static readonly Color BadgeColor = Color.FromArgb(225, 32, 32, 32);
    private static readonly Color BadgeBorderColor = Color.FromArgb(110, 255, 255, 255);

    private readonly OverlayWindow _window = new();
    private readonly System.Windows.Forms.Timer _timer;
    private OverlayCorner _corner;
    private OverlaySize _size;
    private OverlayMonitor _monitor;
    private int _opacity;
    private bool _visible;
    private Rectangle _renderedBounds;
    private byte _renderedAlpha;
    private bool _disposed;

    public ReductionOverlay()
    {
        _timer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
        _timer.Tick += (_, _) => Refresh(forceRender: false);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>
    /// Shows the badge if the overlay is enabled and the volume is reduced, otherwise hides it. Picks up
    /// changed settings right away.
    /// </summary>
    public void Update(OverlaySettings settings, bool reduced)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _corner = settings.Corner;
        _size = settings.Size;
        _monitor = settings.Monitor;
        _opacity = settings.Opacity;

        if (!(settings.Enabled && reduced))
        {
            Hide();
            return;
        }

        var wasVisible = _visible;
        _visible = true;
        Refresh(forceRender: true); // Draw before showing, so it never appears empty.
        if (!wasVisible)
        {
            _window.Show();
            _window.BringToTop();
        }

        _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _timer.Dispose();
        _window.DestroyHandle();
    }

    private void Hide()
    {
        _timer.Stop();
        if (_visible)
        {
            _visible = false;
            _window.Hide();
        }

        _renderedBounds = Rectangle.Empty;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            Refresh(forceRender: true);
        }
    }

    /// <summary>
    /// Re-places the badge (monitor, working area or scaling may have changed), redraws it if needed, and
    /// puts it back on top of other topmost windows.
    /// </summary>
    private void Refresh(bool forceRender)
    {
        if (!_visible || ChooseScreen() is not { } screen)
        {
            return;
        }

        var bounds = OverlayLayout.Place(screen.WorkingArea, _corner, _size, GetDpi(screen));
        var alpha = OverlayLayout.Alpha(_opacity);
        if (forceRender || bounds != _renderedBounds || alpha != _renderedAlpha)
        {
            _window.Render(bounds, alpha);
            _renderedBounds = bounds;
            _renderedAlpha = alpha;
        }

        _window.BringToTop();
    }

    private Screen? ChooseScreen()
    {
        if (_monitor == OverlayMonitor.ActiveWindow)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground != _window.Handle)
            {
                return Screen.FromHandle(foreground);
            }
        }

        return Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
    }

    /// <summary>The monitor's effective DPI (its scaling setting), or 96 if it can't be read.</summary>
    private static int GetDpi(Screen screen)
    {
        var center = new NativeMethods.POINT
        {
            X = screen.Bounds.Left + (screen.Bounds.Width / 2),
            Y = screen.Bounds.Top + (screen.Bounds.Height / 2),
        };
        var monitor = NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return monitor != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0
            && dpiX > 0
                ? (int)dpiX
                : OverlayLayout.BaseDpi;
    }

    /// <summary>Draws the badge: a dark rounded square with the orange "reduced" speaker.</summary>
    private static Bitmap DrawBadge(int edge)
    {
        var bitmap = new Bitmap(edge, edge, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var border = Math.Max(1f, edge / 48f);
        var badge = new RectangleF(border / 2, border / 2, edge - border, edge - border);
        var corner = new SizeF(edge * 0.44f, edge * 0.44f); // The ellipse size of each rounded corner.
        using (var fill = new SolidBrush(BadgeColor))
        {
            graphics.FillRoundedRectangle(fill, badge, corner);
        }

        using (var pen = new Pen(BadgeBorderColor, border))
        {
            graphics.DrawRoundedRectangle(pen, badge, corner);
        }

        var inset = edge * 0.17f;
        TrayIcons.DrawGlyph(graphics, new RectangleF(inset, inset, edge - (2 * inset), edge - (2 * inset)), reduced: true);
        return bitmap;
    }

    /// <summary>The bare layered window; it only knows how to show, hide, stay on top and draw itself.</summary>
    private sealed class OverlayWindow : NativeWindow
    {
        public OverlayWindow()
        {
            CreateHandle(new CreateParams
            {
                Caption = $"{AppInfo.Name} overlay",
                Style = unchecked((int)NativeMethods.WS_POPUP),
                ExStyle = NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW
                    | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST,
                Width = 1,
                Height = 1,
            });
        }

        public void Show() => NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);

        public void Hide() => NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);

        public void BringToTop() => NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);

        /// <summary>Draws the badge at <paramref name="bounds"/> (physical pixels) with the given overall alpha.</summary>
        public void Render(Rectangle bounds, byte alpha)
        {
            using var bitmap = DrawBadge(bounds.Width);

            var screenDc = NativeMethods.GetDC(IntPtr.Zero);
            var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0)); // A premultiplied 32-bit DIB, as ULW_ALPHA needs.
            var previous = NativeMethods.SelectObject(memoryDc, hBitmap);
            try
            {
                var destination = new NativeMethods.POINT { X = bounds.X, Y = bounds.Y };
                var size = new NativeMethods.SIZE { CX = bounds.Width, CY = bounds.Height };
                var source = new NativeMethods.POINT();
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    SourceConstantAlpha = alpha,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA,
                };
                NativeMethods.UpdateLayeredWindow(
                    Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                NativeMethods.SelectObject(memoryDc, previous);
                NativeMethods.DeleteObject(hBitmap);
                NativeMethods.DeleteDC(memoryDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case NativeMethods.WM_NCHITTEST:
                    m.Result = NativeMethods.HTTRANSPARENT; // Clicks go to whatever is underneath.
                    return;
                case NativeMethods.WM_MOUSEACTIVATE:
                    m.Result = NativeMethods.MA_NOACTIVATE;
                    return;
                default:
                    base.WndProc(ref m);
                    return;
            }
        }
    }

    private static class NativeMethods
    {
        public const uint WS_POPUP = 0x80000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public static readonly IntPtr HWND_TOPMOST = new(-1);

        public const int WM_NCHITTEST = 0x0084;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public static readonly IntPtr HTTRANSPARENT = new(-1);
        public static readonly IntPtr MA_NOACTIVATE = new(3);

        public const int ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;

        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int CX;
            public int CY;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            ref POINT pptDst,
            ref SIZE psize,
            IntPtr hdcSrc,
            ref POINT pptSrc,
            int crKey,
            ref BLENDFUNCTION pblend,
            int dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr ho);
    }
}
