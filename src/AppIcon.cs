using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DangAn
{
    /// <summary>软件图标：青蓝渐变圆角底 + 档案盒 + 露出的文件。程序内 Logo 与 .ico 用同一份绘制代码。</summary>
    public static class AppIcon
    {
        public static readonly Color C1 = Color.FromArgb(38, 198, 170);   // 左上
        public static readonly Color C2 = Color.FromArgb(14, 110, 160);   // 右下
        public static readonly Color Deep = Color.FromArgb(14, 120, 140);

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Draw(Graphics g, RectangleF r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = Math.Min(r.Width, r.Height);
            float x0 = r.X, y0 = r.Y;
            Func<float, float> X = v => x0 + v * s;
            Func<float, float> Y = v => y0 + v * s;
            bool tiny = s < 28;

            // 底板
            var bg = new RectangleF(X(0.03f), Y(0.03f), s * 0.94f, s * 0.94f);
            using (var path = RoundRect(bg, s * 0.23f))
            {
                using (var b = new LinearGradientBrush(new PointF(bg.Left, bg.Top), new PointF(bg.Right, bg.Bottom), C1, C2)) g.FillPath(b, path);
                if (!tiny)
                {
                    // 顶部高光
                    var hl = new RectangleF(bg.X, bg.Y, bg.Width, bg.Height * 0.55f);
                    using (var hb = new LinearGradientBrush(hl, Color.FromArgb(55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        hb.WrapMode = WrapMode.TileFlipXY;
                        var old = g.Clip; g.SetClip(path); g.FillRectangle(hb, new RectangleF(hl.X, hl.Y + 1, hl.Width, hl.Height - 2)); g.Clip = old;
                    }
                }
            }

            // 露出的文件（两张，略错开）
            if (!tiny)
            {
                using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                using (var p = RoundRect(new RectangleF(X(0.36f), Y(0.17f), s * 0.36f, s * 0.36f), s * 0.035f)) g.FillPath(b, p);
            }
            using (var b = new SolidBrush(Color.White))
            using (var p = RoundRect(new RectangleF(X(0.28f), Y(tiny ? 0.18f : 0.22f), s * 0.38f, s * 0.36f), s * 0.035f)) g.FillPath(b, p);
            if (!tiny)
                using (var pen = new Pen(Color.FromArgb(120, Deep), Math.Max(1f, s * 0.028f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, X(0.34f), Y(0.30f), X(0.58f), Y(0.30f));
                    g.DrawLine(pen, X(0.34f), Y(0.37f), X(0.52f), Y(0.37f));
                }

            // 档案盒：盖子 + 盒身
            var lid = new RectangleF(X(0.17f), Y(0.45f), s * 0.66f, s * 0.12f);
            var body = new RectangleF(X(0.21f), Y(0.53f), s * 0.58f, s * 0.30f);
            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 40, 60)))
            using (var p = RoundRect(new RectangleF(body.X, body.Y + s * 0.02f, body.Width, body.Height), s * 0.05f)) if (!tiny) g.FillPath(shadow, p);
            using (var b = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (var p = RoundRect(body, s * 0.05f)) g.FillPath(b, p);
            using (var b = new SolidBrush(Color.White))
            using (var p = RoundRect(lid, s * 0.04f)) g.FillPath(b, p);
            if (!tiny)
                using (var pen = new Pen(Color.FromArgb(70, Deep), Math.Max(1f, s * 0.015f)))
                    g.DrawLine(pen, lid.Left + s * 0.03f, lid.Bottom, lid.Right - s * 0.03f, lid.Bottom);
            // 标签槽
            var slot = new RectangleF(X(0.40f), Y(tiny ? 0.62f : 0.63f), s * 0.20f, s * (tiny ? 0.09f : 0.07f));
            using (var b = new LinearGradientBrush(slot, C1, C2, LinearGradientMode.Horizontal))
            using (var p = RoundRect(slot, s * 0.03f)) g.FillPath(b, p);
        }

        public static Bitmap Render(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); Draw(g, new RectangleF(0, 0, size, size)); }
            return bmp;
        }

        /// <summary>传统 32 位 DIB 图标帧（兼容 .NET Icon 类和老版资源管理器）。</summary>
        static void WriteDib(Bitmap bmp, Stream s)
        {
            int w = bmp.Width, h = bmp.Height;
            var bw = new BinaryWriter(s);
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }
            int maskRow = ((w + 31) / 32) * 4;
            bw.Write(new byte[maskRow * h]);
            bw.Flush();
        }

        public static void SaveIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var pngs = new List<byte[]>();
            foreach (var s in sizes)
                using (var bmp = Render(s))
                using (var ms = new MemoryStream())
                {
                    if (s >= 256) bmp.Save(ms, ImageFormat.Png);
                    else WriteDib(bmp, ms);
                    pngs.Add(ms.ToArray());
                }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(b); w.Write(b); w.Write((byte)0); w.Write((byte)0);
                    w.Write((ushort)1); w.Write((ushort)32); w.Write((uint)pngs[i].Length); w.Write((uint)offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }
    }
}
