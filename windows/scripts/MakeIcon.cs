using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class MakeIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: MakeIcon.exe <source.png> <out.ico> <out-hires.png>");
            return 1;
        }

        var source = args[0];
        var outIco = args[1];
        var outPng = args[2];

        using (var src = Image.FromFile(source))
        using (var hires = Render(src, 512))
        {
            hires.Save(outPng, ImageFormat.Png);
            using (var iconBmp = Render(src, 256))
            {
                var hIcon = iconBmp.GetHicon();
                try
                {
                    using (var icon = (Icon)Icon.FromHandle(hIcon).Clone())
                    using (var fs = File.Create(outIco))
                        icon.Save(fs);
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        Console.WriteLine("Wrote " + outPng + " and " + outIco);
        return 0;
    }

    private static Bitmap Render(Image src, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            var pad = (int)Math.Round(size * 0.1);
            var dest = new Rectangle(pad, pad, size - pad * 2, size - pad * 2);
            g.DrawImage(src, dest);
        }
        ApplyCircularMask(bmp);
        return bmp;
    }

    private static void ApplyCircularMask(Bitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var cx = (w - 1) / 2.0;
        var cy = (h - 1) / 2.0;
        var r = Math.Min(cx, cy) - 1.0;
        var r2 = r * r;
        var bg = Color.FromArgb(10, 10, 12);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * h];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < h; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < w; x++)
                {
                    var i = row + x * 4;
                    var b = bytes[i];
                    var g = bytes[i + 1];
                    var rch = bytes[i + 2];
                    var dx = x - cx;
                    var dy = y - cy;
                    if (dx * dx + dy * dy > r2)
                    {
                        bytes[i + 3] = 0;
                        continue;
                    }
                    if (IsWhite(rch, g, b))
                    {
                        bytes[i] = 255; bytes[i + 1] = 255; bytes[i + 2] = 255; bytes[i + 3] = 255;
                    }
                    else if (IsBlack(rch, g, b) && HasWhiteNeighbor(bytes, data.Stride, w, h, x, y, 5))
                    {
                        bytes[i] = 0; bytes[i + 1] = 0; bytes[i + 2] = 0; bytes[i + 3] = 255;
                    }
                    else
                    {
                        bytes[i] = bg.B; bytes[i + 1] = bg.G; bytes[i + 2] = bg.R; bytes[i + 3] = 255;
                    }
                }
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static bool IsWhite(int r, int g, int b) { return r > 190 && g > 190 && b > 190; }
    private static bool IsBlack(int r, int g, int b) { return r < 55 && g < 55 && b < 55; }

    private static bool HasWhiteNeighbor(byte[] bytes, int stride, int w, int h, int cx, int cy, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var x = cx + dx;
            var y = cy + dy;
            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            var i = y * stride + x * 4;
            if (IsWhite(bytes[i + 2], bytes[i + 1], bytes[i])) return true;
        }
        return false;
    }
}
