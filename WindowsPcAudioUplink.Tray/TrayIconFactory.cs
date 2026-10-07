using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WindowsPcAudioUplink.Tray;

internal enum TrayIconFrame
{
    NoWaves,
    OneWave,
    TwoWaves
}

internal static class TrayIconFactory
{
    const int CanvasSize = 128;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(nint handle);

    public static Icon Create(TrayIconFrame frame)
    {
        using var source = new Bitmap(CanvasSize, CanvasSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawDish(graphics, frame);
        }

        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, 32, 32));
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    static void DrawDish(Graphics graphics, TrayIconFrame frame)
    {
        using var bowlPen = CreatePen(9);
        using var structurePen = CreatePen(8);
        using var signalPen = CreatePen(9);

        using var bowl = new GraphicsPath();
        bowl.AddBezier(31, 31, 6, 59, 30, 99, 88, 88);
        graphics.DrawPath(bowlPen, bowl);
        graphics.DrawLine(bowlPen, 31, 31, 88, 88);

        graphics.DrawLine(structurePen, 36, 84, 25, 108);
        graphics.DrawLine(structurePen, 49, 91, 63, 108);
        graphics.DrawLine(structurePen, 21, 111, 68, 111);

        graphics.DrawLine(structurePen, 61, 62, 77, 45);
        graphics.FillEllipse(Brushes.White, 69, 36, 18, 18);
        var previousMode = graphics.CompositingMode;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using (var transparent = new SolidBrush(Color.Transparent))
        {
            graphics.FillEllipse(transparent, 75, 42, 6, 6);
        }
        graphics.CompositingMode = previousMode;

        if (frame >= TrayIconFrame.OneWave)
        {
            graphics.DrawArc(signalPen, 61, 25, 42, 42, -105, 112);
        }

        if (frame >= TrayIconFrame.TwoWaves)
        {
            graphics.DrawArc(signalPen, 48, 12, 68, 68, -105, 112);
        }
    }

    static Pen CreatePen(float width)
    {
        return new Pen(Color.White, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
    }
}
