// SwanShell Image Builder
// Builds per-game images for the swanshell / SwanShellDX menu (nileswan flash cart):
//   <destination>\NILESWAN\IMAGES\<game name>\1.bmp ... 6.bmp   (224x144, 4 bpp, 16 colours)
//   <destination>\NILESWAN\IMAGES\<game name>\thumb.bmp         (112x72,  4 bpp, 16 colours)
//
// Builds with the csc.exe of the .NET Framework 4 that ships with Windows (C# 5): see build.bat.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("SwanShell Image Builder")]
[assembly: System.Reflection.AssemblyProduct("SwanShell Image Builder")]
[assembly: System.Reflection.AssemblyDescription("Builds the game images for the swanshell / SwanShellDX menu of the nileswan flash cart")]
[assembly: System.Reflection.AssemblyCompany("mariovirgili")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (C) 2026 mariovirgili - GPL-3.0")]
[assembly: System.Reflection.AssemblyVersion("0.1.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.1.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.1.1")]

namespace SwanShellImageBuilder
{
    // ------------------------------------------------------------------
    // Conversion to the 4-bit BMP format read by swanshell
    // ------------------------------------------------------------------
    static class WsBmp
    {
        public const int FullW = 224, FullH = 144, ThumbW = 112, ThumbH = 72;
        public const int MaxFileSize = 65535;

        // Loads any image (PNG, JPG, BMP, GIF, TIFF) into a 32-bit Bitmap.
        public static Bitmap Load(string path)
        {
            return LoadBytes(File.ReadAllBytes(path));
        }

        public static Bitmap LoadBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var img = Image.FromStream(ms, true, true))
                return FromImage(img);
        }

        // Copies any Image into a 32-bit Bitmap, applying EXIF orientation and compositing transparency over black.
        public static Bitmap FromImage(Image img)
        {
            var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
            }
            try
            {
                if (img.PropertyIdList.Contains(0x0112))
                {
                    int o = img.GetPropertyItem(0x0112).Value[0];
                    RotateFlipType rf = RotateFlipType.RotateNoneFlipNone;
                    switch (o)
                    {
                        case 2: rf = RotateFlipType.RotateNoneFlipX; break;
                        case 3: rf = RotateFlipType.Rotate180FlipNone; break;
                        case 4: rf = RotateFlipType.Rotate180FlipX; break;
                        case 5: rf = RotateFlipType.Rotate90FlipX; break;
                        case 6: rf = RotateFlipType.Rotate90FlipNone; break;
                        case 7: rf = RotateFlipType.Rotate270FlipX; break;
                        case 8: rf = RotateFlipType.Rotate270FlipNone; break;
                    }
                    if (rf != RotateFlipType.RotateNoneFlipNone) bmp.RotateFlip(rf);
                }
            }
            catch { }
            return bmp;
        }

        // Fits the image to w x h.
        //  contain = false: fill the area and crop the excess around the centre (like ws_bmp.py)
        //  contain = true : fit the whole image inside, with bars of colour bg; margin shrinks the usable area
        public static Bitmap Fit(Bitmap src, int w, int h, bool contain, int margin, Color bg)
        {
            var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            // Screenshot already at the right size, or scaled up by an integer factor: copy pixel by pixel.
            int k = src.Width / w;
            if (k >= 1 && src.Width == w * k && src.Height == h * k && (margin == 0 || !contain))
            {
                var s = GetPixels(src);
                var d = new int[w * h];
                int off = k / 2;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        d[y * w + x] = s[(y * k + off) * src.Width + x * k + off];
                SetPixels(dst, d);
                return dst;
            }

            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(bg);
                int aw = contain ? w - 2 * margin : w;
                int ah = contain ? h - 2 * margin : h;
                double sc = contain
                    ? Math.Min((double)aw / src.Width, (double)ah / src.Height)
                    : Math.Max((double)w / src.Width, (double)h / src.Height);
                int dw = (int)Math.Round(src.Width * sc);
                int dh = (int)Math.Round(src.Height * sc);
                if (contain) { dw = Math.Max(1, Math.Min(aw, dw)); dh = Math.Max(1, Math.Min(ah, dh)); }
                else { dw = Math.Max(w, dw); dh = Math.Max(h, dh); }
                int dx = (w - dw) / 2, dy = (h - dh) / 2;

                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle(dx, dy, dw, dh), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return dst;
        }

        public static int[] GetPixels(Bitmap b)
        {
            var r = new Rectangle(0, 0, b.Width, b.Height);
            var data = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[b.Width * b.Height];
            for (int y = 0; y < b.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), px, y * b.Width, b.Width);
            b.UnlockBits(data);
            return px;
        }

        static void SetPixels(Bitmap b, int[] px)
        {
            var r = new Rectangle(0, 0, b.Width, b.Height);
            var data = b.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < b.Height; y++)
                Marshal.Copy(px, y * b.Width, IntPtr.Add(data.Scan0, y * data.Stride), b.Width);
            b.UnlockBits(data);
        }

        // ---- Reduction to 16 colours: median cut (variance split) + k-means refinement ----
        class Box
        {
            public int[] Px; // RGB colours 0xRRGGBB
            public double Sse;
            public int Axis;
            public Box(int[] px) { Px = px; Measure(); }
            void Measure()
            {
                int n = Px.Length;
                double[] sum = new double[3], sq = new double[3];
                foreach (int c in Px)
                    for (int a = 0; a < 3; a++)
                    {
                        int v = (c >> (16 - 8 * a)) & 255;
                        sum[a] += v; sq[a] += (double)v * v;
                    }
                Sse = 0; double best = -1; Axis = 0;
                for (int a = 0; a < 3; a++)
                {
                    double var = sq[a] - sum[a] * sum[a] / n;
                    Sse += var;
                    if (var > best) { best = var; Axis = a; }
                }
            }
        }

        public static int[] BuildPalette(int[] rgb, int colors)
        {
            var uniq = new HashSet<int>(rgb);
            if (uniq.Count <= colors)
            {
                var p = uniq.ToList();
                p.Sort();
                return p.ToArray();
            }

            var boxes = new List<Box> { new Box((int[])rgb.Clone()) };
            while (boxes.Count < colors)
            {
                Box bb = null;
                foreach (var b in boxes)
                    if (b.Px.Length > 1 && b.Sse > 0 && (bb == null || b.Sse > bb.Sse)) bb = b;
                if (bb == null) break;
                int shift = 16 - 8 * bb.Axis;
                var sorted = bb.Px.OrderBy(c => (c >> shift) & 255).ToArray();
                int mid = sorted.Length / 2;
                // avoid splitting a run of equal values when possible
                int key = (sorted[mid] >> shift) & 255;
                int lo = mid, hi = mid;
                while (lo > 0 && ((sorted[lo - 1] >> shift) & 255) == key) lo--;
                while (hi < sorted.Length && ((sorted[hi] >> shift) & 255) == key) hi++;
                mid = (lo > 0 && (mid - lo <= hi - mid || hi >= sorted.Length)) ? lo : hi;
                if (mid <= 0 || mid >= sorted.Length) mid = sorted.Length / 2;
                boxes.Remove(bb);
                boxes.Add(new Box(sorted.Take(mid).ToArray()));
                boxes.Add(new Box(sorted.Skip(mid).ToArray()));
            }

            var pal = new double[boxes.Count, 3];
            for (int i = 0; i < boxes.Count; i++)
            {
                double r = 0, g = 0, b = 0;
                foreach (int c in boxes[i].Px) { r += (c >> 16) & 255; g += (c >> 8) & 255; b += c & 255; }
                int n = boxes[i].Px.Length;
                pal[i, 0] = r / n; pal[i, 1] = g / n; pal[i, 2] = b / n;
            }

            // a few k-means passes to refine the colours
            int np = boxes.Count;
            for (int it = 0; it < 4; it++)
            {
                var acc = new double[np, 4];
                foreach (int c in rgb)
                {
                    int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                    int bi = 0; double bd = double.MaxValue;
                    for (int i = 0; i < np; i++)
                    {
                        double dr = r - pal[i, 0], dg = g - pal[i, 1], db = b - pal[i, 2];
                        double d = dr * dr + dg * dg + db * db;
                        if (d < bd) { bd = d; bi = i; }
                    }
                    acc[bi, 0] += r; acc[bi, 1] += g; acc[bi, 2] += b; acc[bi, 3] += 1;
                }
                for (int i = 0; i < np; i++)
                    if (acc[i, 3] > 0)
                    { pal[i, 0] = acc[i, 0] / acc[i, 3]; pal[i, 1] = acc[i, 1] / acc[i, 3]; pal[i, 2] = acc[i, 2] / acc[i, 3]; }
            }

            var res = new int[np];
            for (int i = 0; i < np; i++)
                res[i] = (Clamp((int)Math.Round(pal[i, 0])) << 16) | (Clamp((int)Math.Round(pal[i, 1])) << 8) | Clamp((int)Math.Round(pal[i, 2]));
            return res;
        }

        static int Clamp(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

        static int Nearest(int[] pal, int r, int g, int b)
        {
            int bi = 0, bd = int.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                int dr = r - ((pal[i] >> 16) & 255), dg = g - ((pal[i] >> 8) & 255), db = b - (pal[i] & 255);
                int d = dr * dr + dg * dg + db * db;
                if (d < bd) { bd = d; bi = i; }
            }
            return bi;
        }

        // Returns the indices (one per pixel, rows top to bottom) and the palette (max 16 colours).
        public static byte[] Quantize(Bitmap img, bool dither, out int[] palette)
        {
            int w = img.Width, h = img.Height;
            var px = GetPixels(img);
            var rgb = new int[px.Length];
            for (int i = 0; i < px.Length; i++) rgb[i] = px[i] & 0xFFFFFF;
            palette = BuildPalette(rgb, 16);
            var pal = palette;
            var idx = new byte[w * h];

            bool exact = new HashSet<int>(rgb).Count <= 16;
            if (!dither || exact)
            {
                var cache = new Dictionary<int, byte>();
                for (int i = 0; i < rgb.Length; i++)
                {
                    byte v;
                    if (!cache.TryGetValue(rgb[i], out v))
                    {
                        v = (byte)Nearest(pal, (rgb[i] >> 16) & 255, (rgb[i] >> 8) & 255, rgb[i] & 255);
                        cache[rgb[i]] = v;
                    }
                    idx[i] = v;
                }
                return idx;
            }

            // Floyd-Steinberg
            var cur = new float[(w + 2) * 3];
            var nxt = new float[(w + 2) * 3];
            for (int y = 0; y < h; y++)
            {
                Array.Clear(nxt, 0, nxt.Length);
                for (int x = 0; x < w; x++)
                {
                    int c = rgb[y * w + x];
                    int o = (x + 1) * 3;
                    float r = ((c >> 16) & 255) + cur[o];
                    float g = ((c >> 8) & 255) + cur[o + 1];
                    float b = (c & 255) + cur[o + 2];
                    int ri = Clamp((int)Math.Round(r)), gi = Clamp((int)Math.Round(g)), bi = Clamp((int)Math.Round(b));
                    int p = Nearest(pal, ri, gi, bi);
                    idx[y * w + x] = (byte)p;
                    float er = r - ((pal[p] >> 16) & 255);
                    float eg = g - ((pal[p] >> 8) & 255);
                    float eb = b - (pal[p] & 255);
                    cur[o + 3] += er * 7 / 16f; cur[o + 4] += eg * 7 / 16f; cur[o + 5] += eb * 7 / 16f;
                    nxt[o - 3] += er * 3 / 16f; nxt[o - 2] += eg * 3 / 16f; nxt[o - 1] += eb * 3 / 16f;
                    nxt[o] += er * 5 / 16f; nxt[o + 1] += eg * 5 / 16f; nxt[o + 2] += eb * 5 / 16f;
                    nxt[o + 3] += er * 1 / 16f; nxt[o + 4] += eg * 1 / 16f; nxt[o + 5] += eb * 1 / 16f;
                }
                var t = cur; cur = nxt; nxt = t;
            }
            return idx;
        }

        // Writes an uncompressed 4-bit BMP: rows bottom-up, left pixel in the high nibble.
        public static byte[] Encode(int w, int h, byte[] idx, int[] pal)
        {
            int pitch = ((w * 4 + 31) / 32) * 4;
            int dataSize = pitch * h;
            int off = 14 + 40 + 16 * 4;
            int size = off + dataSize;
            if (size > MaxFileSize)
                throw new Exception("file too large: " + size + " bytes");

            var ms = new MemoryStream(size);
            var bw = new BinaryWriter(ms);
            // BITMAPFILEHEADER
            bw.Write((byte)'B'); bw.Write((byte)'M');
            bw.Write((uint)size); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((uint)off);
            // BITMAPINFOHEADER
            bw.Write((uint)40); bw.Write((int)w); bw.Write((int)h);
            bw.Write((ushort)1); bw.Write((ushort)4); bw.Write((uint)0); bw.Write((uint)dataSize);
            bw.Write((int)2835); bw.Write((int)2835); bw.Write((uint)16); bw.Write((uint)16);
            // palette B, G, R, 0
            for (int i = 0; i < 16; i++)
            {
                int c = i < pal.Length ? pal[i] : 0;
                bw.Write((byte)(c & 255)); bw.Write((byte)((c >> 8) & 255)); bw.Write((byte)((c >> 16) & 255)); bw.Write((byte)0);
            }
            var row = new byte[pitch];
            for (int y = h - 1; y >= 0; y--)
            {
                Array.Clear(row, 0, pitch);
                for (int x = 0; x < w; x += 2)
                {
                    int a = idx[y * w + x] & 15;
                    int b = (x + 1 < w) ? (idx[y * w + x + 1] & 15) : 0;
                    row[x / 2] = (byte)((a << 4) | b);
                }
                bw.Write(row);
            }
            bw.Flush();
            return ms.ToArray();
        }

        // Same checks the menu performs: returns null when the file is valid.
        public static string Validate(byte[] f, int w, int h)
        {
            if (f.Length > MaxFileSize) return "larger than 65535 bytes";
            if (f.Length < 54 || f[0] != 'B' || f[1] != 'M') return "missing BM header";
            int off = BitConverter.ToInt32(f, 10);
            int hs = BitConverter.ToInt32(f, 14);
            int bw = BitConverter.ToInt32(f, 18), bh = BitConverter.ToInt32(f, 22);
            int bpp = BitConverter.ToUInt16(f, 28), comp = BitConverter.ToInt32(f, 30);
            if (hs < 40) return "info header < 40 bytes";
            if (bw != w || bh != h) return "size " + bw + "x" + bh + " instead of " + w + "x" + h;
            if (bpp != 4) return "not 4 bits per pixel";
            if (comp != 0) return "compressed";
            if (14 + hs + 16 * 4 > off) return "palette overlaps the pixel data";
            int pitch = ((w * 4 + 31) / 32) * 4;
            if ((long)off + (long)pitch * h > f.Length) return "pixel data past the end of the file";
            return null;
        }

        static Bitmap IndexedToBitmap(int w, int h, byte[] idx, int[] pal)
        {
            var px = new int[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = unchecked((int)0xFF000000) | pal[idx[i]];
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            SetPixels(b, px);
            return b;
        }

        // Same pipeline as Convert, but returns the 16-colour result as a Bitmap (for the preview).
        public static Bitmap Render(Bitmap src, int w, int h, bool contain, int margin, bool dither)
        {
            using (var fit = Fit(src, w, h, contain, margin, Color.Black))
            {
                int[] pal;
                var idx = Quantize(fit, dither, out pal);
                return IndexedToBitmap(w, h, idx, pal);
            }
        }

        // Converts to the final BMP bytes (verified); optionally also returns the 16-colour result as a Bitmap.
        public static byte[] ConvertToBytes(Bitmap src, int w, int h, bool contain, int margin, bool dither, bool makeBitmap, out Bitmap bmp)
        {
            using (var fit = Fit(src, w, h, contain, margin, Color.Black))
            {
                int[] pal;
                var idx = Quantize(fit, dither, out pal);
                var bytes = Encode(w, h, idx, pal);
                string err = Validate(bytes, w, h);
                if (err != null) throw new Exception("verification failed: " + err);
                bmp = makeBitmap ? IndexedToBitmap(w, h, idx, pal) : null;
                return bytes;
            }
        }

        public static void Convert(Bitmap src, string outPath, int w, int h, bool contain, int margin, bool dither)
        {
            using (var fit = Fit(src, w, h, contain, margin, Color.Black))
            {
                int[] pal;
                var idx = Quantize(fit, dither, out pal);
                var bytes = Encode(w, h, idx, pal);
                string err = Validate(bytes, w, h);
                if (err != null) throw new Exception("verification failed: " + err);
                File.WriteAllBytes(outPath, bytes);
            }
        }
    }

    // ------------------------------------------------------------------
    // Matching ROMs <-> images
    // ------------------------------------------------------------------
    class ImgFile
    {
        public string Path, Rel, Key, Base, Tag, SubDir;
        public int Num = int.MaxValue;
        public bool IsThumb, IsCover;
    }

    class GameJob
    {
        public string RomPath, Folder;
        public List<ImgFile> Full = new List<ImgFile>();
        public ImgFile Thumb;
        public bool Fuzzy;
    }

    static class Matcher
    {
        public const string WebDir = "_web";   // images picked from the internet are saved here
        public static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
        static readonly Regex Suffix = new Regex(
            @"^(?<base>.+?)[ _\-.]+(?<tag>thumb|thumbnail|miniatura|cover|box|boxart|front|copertina|title|titolo|snap|screen|screenshot|ss|\d{1,2})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // hex hash at the end of the name (e.g. "Game (English) 9B23BCDCC67ED02A992853EA177560B6")
        static readonly Regex HashTail = new Regex(@"[ _\-.]+[0-9A-Fa-f]{16,}$", RegexOptions.CultureInvariant);
        static readonly Regex Tags = new Regex(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.CultureInvariant);

        public static string Norm(string s)
        {
            s = Tags.Replace(s, " ");
            var sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        public static List<ImgFile> IndexImages(string dir, bool recursive)
        {
            var list = new List<ImgFile>();
            var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(dir, "*", opt);
            string web = System.IO.Path.Combine(dir, WebDir);
            if (!recursive && Directory.Exists(web)) files = files.Concat(Directory.EnumerateFiles(web));
            foreach (var p in files)
            {
                string ext = System.IO.Path.GetExtension(p).ToLowerInvariant();
                if (Array.IndexOf(ImageExt, ext) < 0) continue;
                var f = new ImgFile();
                f.Path = p;
                f.Rel = p.Substring(dir.Length).TrimStart('\\', '/');
                f.Key = HashTail.Replace(System.IO.Path.GetFileNameWithoutExtension(p), "");
                int sl = f.Rel.IndexOfAny(new[] { '\\', '/' });
                f.SubDir = sl > 0 ? f.Rel.Substring(0, sl) : "";
                var m = Suffix.Match(f.Key);
                if (m.Success)
                {
                    f.Base = m.Groups["base"].Value;
                    f.Tag = m.Groups["tag"].Value.ToLowerInvariant();
                    int n;
                    if (int.TryParse(f.Tag, out n)) f.Num = n;
                }
                f.IsThumb = f.Tag == "thumb" || f.Tag == "thumbnail" || f.Tag == "miniatura";
                string relDir = (System.IO.Path.GetDirectoryName(f.Rel) ?? "").ToLowerInvariant();
                f.IsCover = f.Tag == "cover" || f.Tag == "box" || f.Tag == "boxart" || f.Tag == "front" || f.Tag == "copertina"
                            || relDir.Contains("box") || relDir.Contains("cover") || relDir.Contains("copertin");
                list.Add(f);
            }
            return list;
        }

        public static bool IsWeb(ImgFile f)
        {
            return string.Equals(f.SubDir, WebDir, StringComparison.OrdinalIgnoreCase);
        }

        // Names the menu looks up the folder by: "Game.wsc.bps" -> "Game.wsc", then "Game".
        public static List<string> RomNames(string romPath)
        {
            var names = new List<string>();
            string a = System.IO.Path.GetFileNameWithoutExtension(romPath);
            names.Add(a);
            string b = System.IO.Path.GetFileNameWithoutExtension(a);
            if (b.Length > 0 && b != a) names.Add(b);
            return names;
        }

        public static List<string> SubDirs(string dir)
        {
            try
            {
                return Directory.GetDirectories(dir).Select(d => System.IO.Path.GetFileName(d))
                    .Where(n => !string.Equals(n, WebDir, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }

        public static GameJob Match(string romPath, List<ImgFile> images, bool fuzzy, bool coversInFull, int maxFull,
                                    bool onePerSub, string thumbSub)
        {
            var job = new GameJob();
            job.RomPath = romPath;
            var names = RomNames(romPath);
            job.Folder = names[0];

            var found = new List<ImgFile>();
            foreach (var f in images)
                foreach (var n in names)
                    if (string.Equals(f.Key, n, StringComparison.OrdinalIgnoreCase) ||
                        (f.Tag != null && string.Equals(f.Base, n, StringComparison.OrdinalIgnoreCase)))
                    { found.Add(f); break; }

            // Loose matching is used for the folder images when none of them matches exactly.
            // Images downloaded into _web always match exactly and must not switch it off.
            if (fuzzy && !found.Any(f => !IsWeb(f)))
            {
                var nn = names.Select(Norm).Where(s => s.Length > 0).ToList();
                foreach (var f in images)
                {
                    if (IsWeb(f) || found.Contains(f)) continue;
                    string k = Norm(f.Key);
                    string b = f.Tag != null ? Norm(f.Base) : null;
                    if (nn.Contains(k) || (b != null && nn.Contains(b))) { found.Add(f); job.Fuzzy = true; }
                }
            }

            // order: numbered first (1, 2, ...), then titles, then screenshots, then covers; ties by path
            var ordered = found
                .OrderBy(f => IsWeb(f) ? 0 : 1)
                .ThenBy(f => f.Num)
                .ThenBy(f => f.IsCover ? 2 : (f.Tag == "title" || f.Tag == "titolo" || f.Rel.ToLowerInvariant().Contains("title") ? 0 : 1))
                .ThenBy(f => f.Rel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ImgFile fromSub = null;
            if (!string.IsNullOrEmpty(thumbSub))
                fromSub = ordered.FirstOrDefault(f => string.Equals(f.SubDir, thumbSub, StringComparison.OrdinalIgnoreCase));
            job.Thumb = ordered.FirstOrDefault(f => f.IsThumb)
                        ?? fromSub
                        ?? ordered.FirstOrDefault(f => f.IsCover)
                        ?? ordered.FirstOrDefault();

            var usedSub = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in ordered)
            {
                if (job.Full.Count >= maxFull) break;
                if (f.IsThumb) continue;
                if (f.IsCover && !coversInFull) continue;
                if (onePerSub && f.SubDir.Length > 0 && !IsWeb(f) && !usedSub.Add(f.SubDir)) continue;
                job.Full.Add(f);
            }
            return job;
        }

        public static List<string> FindRoms(string dir, string extList, bool recursive)
        {
            var exts = new HashSet<string>(extList
                .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => "." + e.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant()));
            var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(dir, "*", opt)
                .Where(p => exts.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant()))
                .OrderBy(p => System.IO.Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    // ------------------------------------------------------------------
    // Settings saved next to the executable
    // ------------------------------------------------------------------
    public static class Settings
    {
        static string filePath;

        // Next to the exe when that folder is writable (portable use), otherwise %APPDATA%\SwanShellImageBuilder.
        public static string FilePath
        {
            get
            {
                if (filePath != null) return filePath;
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SwanShellImageBuilder.ini");
                string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwanShellImageBuilder", "SwanShellImageBuilder.ini");
                if (File.Exists(local) && CanWrite(local, false)) return filePath = local;
                if (!File.Exists(app) && CanWrite(local, true)) return filePath = local;
                try { Directory.CreateDirectory(Path.GetDirectoryName(app)); } catch { }
                if (!File.Exists(app) && File.Exists(local))
                    try { File.Copy(local, app); } catch { }   // carry over settings from a read-only copy
                return filePath = app;
            }
        }

        static bool CanWrite(string path, bool probe)
        {
            try
            {
                if (!probe)
                {
                    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
                    return true;
                }
                string test = Path.Combine(Path.GetDirectoryName(path), ".ssib_write_test");
                File.WriteAllText(test, "x");
                File.Delete(test);
                return true;
            }
            catch { return false; }
        }
        public static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                    }
            }
            catch { }
            return d;
        }
        // Merges the given keys into the file (other keys are kept).
        public static void Save(Dictionary<string, string> d)
        {
            try
            {
                var all = Load();
                foreach (var kv in d) all[kv.Key] = kv.Value;
                File.WriteAllLines(FilePath, all.Select(kv => kv.Key + "=" + (kv.Value ?? "").Replace("\r", "").Replace("\n", " ")), Encoding.UTF8);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // What will be written for one game, given what is already in its folder on the card.
    // Existing files are never overwritten or deleted here: new images get free names,
    // identical ones are skipped, and nothing is added beyond the 6 images the menu shows.
    // ------------------------------------------------------------------
    class ExistingImage
    {
        public string Path, Name, Problem;
        public bool IsThumb;
        public int ShownIndex;   // 1..6 = position in the menu, 0 = not shown (beyond 6)
        public Bitmap Preview;
    }

    class PlannedImage
    {
        public ImgFile Source;
        public bool IsThumb;
        public string OutName, Skip, Error;
        public byte[] Bytes;
        public Bitmap Preview;
    }

    class GamePlan
    {
        public string Dir;
        public List<ExistingImage> Existing = new List<ExistingImage>();
        public List<PlannedImage> New = new List<PlannedImage>();
    }

    static class Planner
    {
        public const int MaxShown = 6;

        static bool IsThumbName(string n) { return string.Equals(n, "thumb.bmp", StringComparison.OrdinalIgnoreCase); }

        public static GamePlan Make(GameJob job, string gdir, RunOptions o, bool wantBitmaps)
        {
            var plan = new GamePlan { Dir = gdir };
            var existingBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (gdir != null && Directory.Exists(gdir))
            {
                var files = Directory.GetFiles(gdir)
                    .Where(f => f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => System.IO.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                int shown = 0;
                foreach (var f in files)
                {
                    var e = new ExistingImage { Path = f, Name = System.IO.Path.GetFileName(f) };
                    e.IsThumb = IsThumbName(e.Name);
                    names.Add(e.Name);
                    try
                    {
                        var b = File.ReadAllBytes(f);
                        existingBytes[e.Name] = b;
                        e.Problem = WsBmp.Validate(b, e.IsThumb ? WsBmp.ThumbW : WsBmp.FullW, e.IsThumb ? WsBmp.ThumbH : WsBmp.FullH);
                    }
                    catch (Exception ex) { e.Problem = "cannot read: " + ex.Message; }
                    if (e.Name.Length >= 48) e.Problem = (e.Problem == null ? "" : e.Problem + "; ") + "name of 48 characters or more";
                    if (!e.IsThumb) { shown++; e.ShownIndex = shown <= MaxShown ? shown : 0; }
                    plan.Existing.Add(e);
                }
            }

            int fullCount = plan.Existing.Count(e => !e.IsThumb);
            var newBytes = new List<Tuple<string, byte[]>>();

            if (job.Thumb != null)
            {
                var it = Convert(job.Thumb, true, o, wantBitmaps);
                if (it.Error == null)
                {
                    byte[] ex;
                    if (existingBytes.TryGetValue("thumb.bmp", out ex))
                        it.Skip = ex.SequenceEqual(it.Bytes)
                            ? "identical to the existing thumb.bmp, not written again"
                            : "not written: thumb.bmp already exists (delete it to replace it)";
                    else if (names.Contains("thumb.bmp"))
                        it.Skip = "not written: thumb.bmp already exists";
                    else it.OutName = "thumb.bmp";
                }
                plan.New.Add(it);
            }

            foreach (var src in job.Full)
            {
                var it = Convert(src, false, o, wantBitmaps);
                if (it.Error == null)
                {
                    string same = existingBytes.Where(kv => !IsThumbName(kv.Key) && kv.Value.SequenceEqual(it.Bytes)).Select(kv => kv.Key).FirstOrDefault();
                    string dup = newBytes.Where(t => t.Item2.SequenceEqual(it.Bytes)).Select(t => t.Item1).FirstOrDefault();
                    if (same != null) it.Skip = "identical to the existing " + same + ", not written again";
                    else if (dup != null) it.Skip = "identical to the new " + dup + ", not written twice";
                    else if (fullCount >= MaxShown) it.Skip = "not written: the folder already has " + MaxShown + " images (the menu shows only " + MaxShown + ")";
                    else
                    {
                        int n = 1;
                        while (names.Contains(n + ".bmp")) n++;
                        it.OutName = n + ".bmp";
                        names.Add(it.OutName);
                        newBytes.Add(Tuple.Create(it.OutName, it.Bytes));
                        fullCount++;
                    }
                }
                plan.New.Add(it);
            }
            return plan;
        }

        static PlannedImage Convert(ImgFile src, bool thumb, RunOptions o, bool wantBitmaps)
        {
            var it = new PlannedImage { Source = src, IsThumb = thumb };
            try
            {
                Bitmap bmp;
                using (var s = WsBmp.Load(src.Path))
                    it.Bytes = thumb
                        // in "fit" mode the subject stays inside the central 108x68 (the menu draws a 2 px frame)
                        ? WsBmp.ConvertToBytes(s, WsBmp.ThumbW, WsBmp.ThumbH, o.ThumbContain, 2, o.Dither, wantBitmaps, out bmp)
                        : WsBmp.ConvertToBytes(s, WsBmp.FullW, WsBmp.FullH, o.FullContain, 0, o.Dither, wantBitmaps, out bmp);
                it.Preview = bmp;
            }
            catch (Exception ex) { it.Error = ex.Message; }
            return it;
        }

        public static void Dispose(GamePlan p)
        {
            if (p == null) return;
            foreach (var e in p.Existing) if (e.Preview != null) { e.Preview.Dispose(); e.Preview = null; }
            foreach (var n in p.New) if (n.Preview != null) { n.Preview.Dispose(); n.Preview = null; }
        }
    }

    // ------------------------------------------------------------------
    // Online image search
    // ------------------------------------------------------------------
    class WebResult
    {
        public string Title, ImageUrl, ThumbUrl, PageUrl, Source;
    }

    static class Web
    {
        public const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
        public static readonly string[] Sources = { "Bing Images", "libretro thumbnails", "ScreenScraper" };
        public const int SrcBing = 0, SrcLibretro = 1, SrcScreenScraper = 2;

        public static byte[] Get(string url, int timeoutMs, string referer)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UA;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Accept = "text/html,application/xhtml+xml,image/avif,image/webp,image/apng,image/*,*/*;q=0.8";
            req.Headers["Accept-Language"] = "en-US,en;q=0.9";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (referer != null) req.Referer = referer;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var st = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[65536];
                int n;
                while ((n = st.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > 40L * 1024 * 1024) throw new Exception("file too large");
                }
                return ms.ToArray();
            }
        }

        // Bing image results page: every result carries an m="{...json...}" attribute
        // with murl (full image), turl (thumbnail), purl (page) and t (title).
        static readonly Regex MAttr = new Regex("\\bm=\"(\\{[^\"]*\\})\"", RegexOptions.CultureInvariant);

        public static List<WebResult> Bing(string query, int first)
        {
            string q = Uri.EscapeDataString(query);
            string url = "https://www.bing.com/images/async?q=" + q + "&first=" + first + "&count=35&mmasync=1&adlt=moderate";
            string html = Encoding.UTF8.GetString(Get(url, 20000, "https://www.bing.com/images/search?q=" + q));
            var list = new List<WebResult>();
            var seen = new HashSet<string>();
            foreach (Match m in MAttr.Matches(html))
            {
                string j = WebUtility.HtmlDecode(m.Groups[1].Value);
                var r = new WebResult
                {
                    ImageUrl = JStr(j, "murl"), ThumbUrl = JStr(j, "turl"),
                    PageUrl = JStr(j, "purl"), Title = JStr(j, "t"), Source = "Bing"
                };
                if (string.IsNullOrEmpty(r.ThumbUrl)) r.ThumbUrl = r.ImageUrl;
                if (string.IsNullOrEmpty(r.ThumbUrl)) continue;
                if (!seen.Add(r.ImageUrl ?? r.ThumbUrl)) continue;
                list.Add(r);
            }
            if (list.Count == 0 && first == 0 && html.IndexOf("iusc", StringComparison.Ordinal) < 0)
                throw new Exception("Bing returned a page without image results (it may be asking for consent or blocking automated requests).");
            return list;
        }

        static string JStr(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return null;
            try { return Regex.Unescape(m.Groups[1].Value); } catch { return m.Groups[1].Value; }
        }

        // libretro thumbnail server: exact No-Intro style names only.
        public static List<WebResult> Libretro(string name)
        {
            string fn = Regex.Replace(name, "[&*/:`<>?\\\\|\"]", "_");
            var list = new List<WebResult>();
            foreach (var sys in new[] { "Bandai - WonderSwan Color", "Bandai - WonderSwan" })
                foreach (var type in new[] { "Named_Titles", "Named_Snaps", "Named_Boxarts" })
                {
                    string url = "https://thumbnails.libretro.com/" + Uri.EscapeDataString(sys) + "/" + type + "/" + Uri.EscapeDataString(fn) + ".png";
                    list.Add(new WebResult { ImageUrl = url, ThumbUrl = url, Title = sys + " / " + type, Source = "libretro" });
                }
            return list;
        }

        public static string DefaultQuery(string game)
        {
            string q = Regex.Replace(game, @"\([^)]*\)|\[[^\]]*\]", " ");
            q = Regex.Replace(q, @"\.(wsc?|pc2|pcv2)$", "", RegexOptions.IgnoreCase);
            q = Regex.Replace(q, @"\s+", " ").Trim();
            return q + " WonderSwan";
        }

        public static string Host(string url)
        {
            try { return new Uri(url).Host; } catch { return ""; }
        }
    }

    // ------------------------------------------------------------------
    // Minimal JSON reader (objects -> Dictionary, arrays -> List, numbers -> double).
    // Tolerates trailing commas, which ScreenScraper sometimes sends.
    // ------------------------------------------------------------------
    static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("':' expected");
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new FormatException("',' or '}' expected");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw new FormatException("',' or ']' expected");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == st) throw new FormatException("unexpected character '" + c + "'");
            double dv;
            double.TryParse(s.Substring(st, i - st), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dv);
            return dv;
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("string expected");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)System.Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        public static object Get(object o, params string[] path)
        {
            foreach (var p in path)
            {
                var d = o as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(p, out o)) return null;
            }
            return o;
        }

        public static string Str(object o, string key)
        {
            var v = Get(o, key);
            return v == null ? null : System.Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // ------------------------------------------------------------------
    // ScreenScraper.fr (API v2). Needs developer credentials (devid/devpassword, requested on the
    // ScreenScraper forum); the user account (ssid/sspassword) is optional but gives more quota.
    // ------------------------------------------------------------------
    class SsCreds
    {
        public string DevId = "", DevPassword = "", SoftName = "SwanShellImageBuilder", User = "", Password = "";
        public bool HasDev { get { return DevId.Trim().Length > 0 && DevPassword.Length > 0; } }

        static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                var b = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return "dpapi:" + System.Convert.ToBase64String(b);
            }
            catch { return "plain:" + System.Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
        }

        static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                if (s.StartsWith("dpapi:"))
                    return Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                        System.Convert.FromBase64String(s.Substring(6)), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
                if (s.StartsWith("plain:")) return Encoding.UTF8.GetString(System.Convert.FromBase64String(s.Substring(6)));
            }
            catch { }
            return "";
        }

        public static SsCreds Load()
        {
            var d = Settings.Load();
            var c = new SsCreds();
            string v;
            if (d.TryGetValue("ss_devid", out v)) c.DevId = v;
            if (d.TryGetValue("ss_devpassword", out v)) c.DevPassword = Unprotect(v);
            if (d.TryGetValue("ss_softname", out v) && v.Trim().Length > 0) c.SoftName = v;
            if (d.TryGetValue("ss_user", out v)) c.User = v;
            if (d.TryGetValue("ss_password", out v)) c.Password = Unprotect(v);
            return c;
        }

        public void Save()
        {
            var d = new Dictionary<string, string>();
            d["ss_devid"] = DevId.Trim(); d["ss_devpassword"] = Protect(DevPassword);
            d["ss_softname"] = SoftName.Trim(); d["ss_user"] = User.Trim(); d["ss_password"] = Protect(Password);
            Settings.Save(d);
        }
    }

    static class ScreenScraper
    {
        static string Api = "https://api.screenscraper.fr/api2/";   // not readonly: tests point it elsewhere
        public const string DevAccountPage = "https://www.screenscraper.fr/forumsujets.php?frub=12&numpage=0";
        public const int SysWonderSwan = 45, SysWonderSwanColor = 46;

        static readonly Dictionary<string, string> TypeNames = new Dictionary<string, string>
        {
            { "ss", "Screenshot" }, { "sstitle", "Title screen" }, { "box-2D", "Box front" }, { "box-2D-back", "Box back" },
            { "box-2D-side", "Box side" }, { "box-3D", "Box 3D" }, { "box-texture", "Box texture" }, { "support-2D", "Cartridge" },
            { "support-texture", "Cartridge texture" }, { "wheel", "Logo" }, { "wheel-hd", "Logo HD" }, { "wheel-carbon", "Logo carbon" },
            { "wheel-steel", "Logo steel" }, { "screenmarquee", "Marquee" }, { "screenmarqueesmall", "Marquee small" },
            { "fanart", "Fan art" }, { "mixrbv1", "Mix v1" }, { "mixrbv2", "Mix v2" }, { "steamgrid", "Steam grid" }
        };

        static string Auth(SsCreds c)
        {
            string a = "devid=" + Uri.EscapeDataString(c.DevId.Trim()) + "&devpassword=" + Uri.EscapeDataString(c.DevPassword) +
                       "&softname=" + Uri.EscapeDataString(c.SoftName.Trim().Length > 0 ? c.SoftName.Trim() : "SwanShellImageBuilder") + "&output=json";
            if (c.User.Trim().Length > 0 && c.Password.Length > 0)
                a += "&ssid=" + Uri.EscapeDataString(c.User.Trim()) + "&sspassword=" + Uri.EscapeDataString(c.Password);
            return a;
        }

        // GET returning the body even for HTTP errors (ScreenScraper explains errors in the body).
        static string Call(string url, out int status)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Web.UA;
            req.Timeout = 30000; req.ReadWriteTimeout = 30000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            HttpWebResponse resp;
            try { resp = (HttpWebResponse)req.GetResponse(); }
            catch (WebException ex)
            {
                resp = ex.Response as HttpWebResponse;
                if (resp == null) throw;
            }
            using (resp)
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                status = (int)resp.StatusCode;
                return r.ReadToEnd();
            }
        }

        // Returns null when the game simply was not found; throws with a readable message on real errors.
        static object Request(string url)
        {
            int status;
            string body = Call(url, out status);
            string head = body.Length > 400 ? body.Substring(0, 400) : body;
            if (head.IndexOf("non trouv", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (head.TrimStart().StartsWith("{"))
            {
                try { return Json.Parse(body); }
                catch (Exception ex) { throw new Exception("unreadable answer from ScreenScraper (" + ex.Message + ")"); }
            }
            string msg = head.Trim();
            if (msg.IndexOf("Erreur de login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("identifiants", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("développeur", StringComparison.OrdinalIgnoreCase) >= 0 || status == 401 || status == 403)
                throw new Exception("login refused — check the developer ID/password and your user name/password (" + msg + ")");
            if (msg.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0 || status == 429 || status == 430 || status == 431)
                throw new Exception("ScreenScraper quota reached for today or too many requests — try again later (" + msg + ")");
            if (msg.IndexOf("ferm", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("closed", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new Exception("the ScreenScraper API is closed or too busy right now (" + msg + ")");
            if (msg.IndexOf("blacklist", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new Exception("this software name is blacklisted by ScreenScraper (" + msg + ")");
            throw new Exception("HTTP " + status + ": " + (msg.Length > 0 ? msg : "empty answer"));
        }

        static int[] Systems(string romPath)
        {
            string ext = Path.GetExtension(romPath).ToLowerInvariant();
            return ext == ".ws" ? new[] { SysWonderSwan, SysWonderSwanColor } : new[] { SysWonderSwanColor, SysWonderSwan };
        }

        static uint[] crcTable;
        static uint Crc32(byte[] data)
        {
            if (crcTable == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                crcTable = t;
            }
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        static string Hex(byte[] h) { return BitConverter.ToString(h).Replace("-", ""); }

        public static string CleanName(string game)
        {
            string q = Regex.Replace(game, @"\([^)]*\)|\[[^\]]*\]", " ");
            q = Regex.Replace(q, @"\.(wsc?|pc2|pcv2)$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(q, @"\s+", " ").Trim();
        }

        // Looks the game up by ROM checksum (plus name and size), then falls back to a name search.
        public static List<WebResult> Search(SsCreds c, string romPath, string game, string nameQuery, Action<string> status)
        {
            string ext = Path.GetExtension(romPath).ToLowerInvariant();
            bool isRom = ext == ".ws" || ext == ".wsc" || ext == ".pc2" || ext == ".pcv2";
            var results = new List<WebResult>();

            if (isRom && File.Exists(romPath) && new FileInfo(romPath).Length <= 64L * 1024 * 1024)
            {
                status("Computing the ROM checksums…");
                var data = File.ReadAllBytes(romPath);
                string crc = Crc32(data).ToString("X8");
                string md5, sha1;
                using (var h = System.Security.Cryptography.MD5.Create()) md5 = Hex(h.ComputeHash(data));
                using (var h = System.Security.Cryptography.SHA1.Create()) sha1 = Hex(h.ComputeHash(data));
                foreach (int sys in Systems(romPath))
                {
                    status("Looking up the ROM checksum on ScreenScraper (system " + sys + ")…");
                    string url = Api + "jeuInfos.php?" + Auth(c) + "&romtype=rom&systemeid=" + sys +
                                 "&crc=" + crc + "&md5=" + md5 + "&sha1=" + sha1 +
                                 "&romtaille=" + data.Length + "&romnom=" + Uri.EscapeDataString(Path.GetFileName(romPath));
                    var json = Request(url);
                    var jeu = Json.Get(json, "response", "jeu");
                    if (jeu != null)
                    {
                        AddMedia(results, jeu, "checksum match");
                        if (results.Count > 0) return results;
                    }
                }
            }

            string q = string.IsNullOrEmpty(nameQuery) ? CleanName(game) : nameQuery;
            foreach (int sys in Systems(romPath))
            {
                status("Searching ScreenScraper by name: \"" + q + "\" (system " + sys + ")…");
                string url = Api + "jeuRecherche.php?" + Auth(c) + "&systemeid=" + sys + "&recherche=" + Uri.EscapeDataString(q);
                var json = Request(url);
                var jeux = Json.Get(json, "response", "jeux") as List<object>;
                if (jeux == null) continue;
                foreach (var j in jeux.Take(6))
                    AddMedia(results, j, "name search");
                if (results.Count > 0) return results;
            }
            return results;
        }

        static string GameName(object jeu)
        {
            var noms = Json.Get(jeu, "noms") as List<object>;
            if (noms == null) return Json.Str(jeu, "nom") ?? "";
            foreach (var reg in new[] { "wor", "us", "eu", "ss", "jp" })
                foreach (var n in noms)
                    if (Json.Str(n, "region") == reg) return Json.Str(n, "text") ?? "";
            return noms.Count > 0 ? (Json.Str(noms[0], "text") ?? "") : "";
        }

        static void AddMedia(List<WebResult> results, object jeu, string how)
        {
            var medias = Json.Get(jeu, "medias") as List<object>;
            if (medias == null) return;
            string name = GameName(jeu);
            string id = Json.Str(jeu, "id");
            string page = id != null ? "https://www.screenscraper.fr/gameinfos.php?gameid=" + id : null;
            foreach (var m in medias)
            {
                string type = Json.Str(m, "type") ?? "";
                string format = (Json.Str(m, "format") ?? "").ToLowerInvariant();
                string url = Json.Str(m, "url");
                if (string.IsNullOrEmpty(url)) continue;
                if (format != "" && format != "png" && format != "jpg" && format != "jpeg") continue;   // skip videos, manuals...
                if (type.StartsWith("video") || type.StartsWith("manuel") || type.StartsWith("themehs") || type == "maps") continue;
                url = url.Replace(" ", "%20");
                string region = Json.Str(m, "region");
                string label;
                if (!TypeNames.TryGetValue(type, out label)) label = type;
                results.Add(new WebResult
                {
                    Source = "ScreenScraper",
                    Title = label + (string.IsNullOrEmpty(region) ? "" : " [" + region + "]") + " — " + name + " (" + how + ")",
                    ImageUrl = url,
                    ThumbUrl = url + (url.Contains("?") ? "&" : "?") + "maxwidth=320&maxheight=240",
                    PageUrl = page
                });
            }
        }
    }

    // Small dialog for the ScreenScraper credentials.
    class SsAccountForm : Form
    {
        TextBox txtDevId, txtDevPass, txtSoft, txtUser, txtPass;

        public SsAccountForm()
        {
            Text = "ScreenScraper account";
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var c = SsCreds.Load();
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(10), Dock = DockStyle.Fill };
            Controls.Add(t);
            var intro = new Label
            {
                AutoSize = true, MaximumSize = new Size(470, 0), Padding = new Padding(0, 0, 0, 8),
                Text = "The ScreenScraper API needs DEVELOPER credentials for this program (developer ID and password). " +
                       "They are not your user account: request them on the ScreenScraper forum, section for developers. " +
                       "Your user account is optional but raises the number of requests you can make per day."
            };
            t.Controls.Add(intro, 0, 0); t.SetColumnSpan(intro, 2);
            var link = new LinkLabel { Text = "Open the ScreenScraper developer forum", AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
            link.LinkClicked += delegate { try { System.Diagnostics.Process.Start(ScreenScraper.DevAccountPage); } catch { } };
            t.Controls.Add(link, 0, 1); t.SetColumnSpan(link, 2);

            txtDevId = Row(t, 2, "Developer ID:", c.DevId, false);
            txtDevPass = Row(t, 3, "Developer password:", c.DevPassword, true);
            txtSoft = Row(t, 4, "Software name:", c.SoftName, false);
            txtUser = Row(t, 5, "User name (optional):", c.User, false);
            txtPass = Row(t, 6, "User password (optional):", c.Password, true);
            var note = new Label
            {
                AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Color.DimGray, Padding = new Padding(0, 6, 0, 6),
                Text = "Saved in " + Settings.FilePath + " ; passwords are encrypted for your Windows user."
            };
            t.Controls.Add(note, 0, 7); t.SetColumnSpan(note, 2);

            var bp = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var ok = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            bp.Controls.Add(cancel); bp.Controls.Add(ok);
            t.Controls.Add(bp, 0, 8); t.SetColumnSpan(bp, 2);
            AcceptButton = ok; CancelButton = cancel;
            ok.Click += delegate
            {
                new SsCreds
                {
                    DevId = txtDevId.Text, DevPassword = txtDevPass.Text, SoftName = txtSoft.Text,
                    User = txtUser.Text, Password = txtPass.Text
                }.Save();
            };
        }

        static TextBox Row(TableLayoutPanel t, int row, string label, string value, bool password)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, row);
            var tb = new TextBox { Width = 260, Text = value ?? "", UseSystemPasswordChar = password };
            t.Controls.Add(tb, 1, row);
            return tb;
        }
    }

    // Window to search, pick and save images from the internet for one game.
    class WebPickerForm : Form
    {
        readonly string game, imgDir, romPath;
        readonly bool fullContain, thumbContain, dither;
        TextBox txtQuery;
        ComboBox cmbSource;
        Button btnSearch, btnMore, btnGoogle, btnAddFull, btnUseThumb;
        FlowLayoutPanel pnlResults;
        PictureBox picFull, picThumb;
        Label lblStatus, lblSel, lblSaved;
        Bitmap current;
        string currentSource;
        Control selectedTile;
        int searchGen, selGen, nextFirst;
        BlockingCollection<Tuple<int, WebResult, PictureBox, Control>> queue;
        public bool Changed;

        public WebPickerForm(string game, string romPath, string imgDir, bool fullContain, bool thumbContain, bool dither)
        {
            this.game = game; this.imgDir = imgDir; this.romPath = romPath;
            this.fullContain = fullContain; this.thumbContain = thumbContain; this.dither = dither;

            Text = "Search images online — " + game;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(1500, wa.Width - 40), Math.Min(900, wa.Height - 40));
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var bar = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true };
            bar.Controls.Add(new Label { Text = "Search:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
            txtQuery = new TextBox { Width = 380, Text = Web.DefaultQuery(game) };
            bar.Controls.Add(txtQuery);
            cmbSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            cmbSource.Items.AddRange(Web.Sources);
            int srcIdx = 0;
            string sv;
            if (Settings.Load().TryGetValue("websource", out sv)) int.TryParse(sv, out srcIdx);
            cmbSource.SelectedIndex = srcIdx >= 0 && srcIdx < Web.Sources.Length ? srcIdx : 0;
            bar.Controls.Add(cmbSource);
            btnSearch = new Button { Text = "Search", AutoSize = true };
            btnMore = new Button { Text = "More results", AutoSize = true, Enabled = false };
            btnGoogle = new Button { Text = "Open Google Images ↗", AutoSize = true };
            var btnPaste = new Button { Text = "Paste image (Ctrl+V)", AutoSize = true };
            btnPaste.Click += delegate { PasteImage(); };
            var btnSsAcc = new Button { Text = "ScreenScraper account…", AutoSize = true };
            btnSsAcc.Click += delegate
            {
                using (var f = new SsAccountForm())
                    if (f.ShowDialog(this) == DialogResult.OK && cmbSource.SelectedIndex == Web.SrcScreenScraper) DoSearch(false);
            };
            bar.Controls.Add(btnSearch); bar.Controls.Add(btnMore); bar.Controls.Add(btnGoogle); bar.Controls.Add(btnPaste); bar.Controls.Add(btnSsAcc);
            root.Controls.Add(bar, 0, 0);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            root.Controls.Add(split, 0, 1);
            pnlResults = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(40, 40, 40), Padding = new Padding(4) };
            split.Panel1.Controls.Add(pnlResults);

            var side = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Padding = new Padding(8), BackColor = Color.FromArgb(48, 48, 48)
            };
            split.Panel2.Controls.Add(side);
            lblSel = new Label { AutoSize = true, ForeColor = Color.White, MaximumSize = new Size(470, 0), Text = "Select an image on the left, or drop / paste one here." };
            side.Controls.Add(lblSel);
            side.Controls.Add(new Label { Text = "Full screen 224×144 (2×):", AutoSize = true, ForeColor = Color.Silver, Padding = new Padding(0, 8, 0, 0) });
            picFull = new PictureBox { Size = new Size(448, 288), BackColor = Color.Black };
            side.Controls.Add(picFull);
            side.Controls.Add(new Label { Text = "Thumbnail 112×72 (2×, orange = frame hidden by the menu):", AutoSize = true, ForeColor = Color.Silver, Padding = new Padding(0, 8, 0, 0) });
            picThumb = new PictureBox { Size = new Size(224, 144), BackColor = Color.Black };
            side.Controls.Add(picThumb);
            var btns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
            btnAddFull = new Button { Text = "Add as full-screen image", AutoSize = true, Enabled = false, BackColor = SystemColors.Control };
            btnUseThumb = new Button { Text = "Use as thumbnail", AutoSize = true, Enabled = false, BackColor = SystemColors.Control };
            btns.Controls.Add(btnAddFull); btns.Controls.Add(btnUseThumb);
            side.Controls.Add(btns);
            lblSaved = new Label { AutoSize = true, ForeColor = Color.LightGreen, MaximumSize = new Size(470, 0) };
            side.Controls.Add(lblSaved);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(640, 0), Padding = new Padding(0, 4, 0, 0),
                Text = "Google Images: click \"Open Google Images\", open the picture you like, then drag it into this window " +
                       "or right-click it → Copy image and press Ctrl+V here. Image files and links can be dropped too."
            }, 0, 0);
            lblStatus = new Label { AutoSize = true, Padding = new Padding(8, 4, 0, 0), MaximumSize = new Size(480, 0) };
            bottom.Controls.Add(lblStatus, 1, 0);
            root.Controls.Add(bottom, 0, 2);

            Load += delegate { try { split.SplitterDistance = Math.Max(300, split.Width - 500); } catch { } DoSearch(false); };
            btnSearch.Click += delegate { DoSearch(false); };
            btnMore.Click += delegate { DoSearch(true); };
            txtQuery.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(false); } };
            cmbSource.SelectedIndexChanged += delegate
            {
                var d = new Dictionary<string, string>();
                d["websource"] = cmbSource.SelectedIndex.ToString();
                Settings.Save(d);
                DoSearch(false);
            };
            btnGoogle.Click += delegate
            {
                try { System.Diagnostics.Process.Start("https://www.google.com/search?udm=2&q=" + Uri.EscapeDataString(txtQuery.Text)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            };
            btnAddFull.Click += delegate { Save(false); };
            btnUseThumb.Click += delegate { Save(true); };
            KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); return; }
                if (e.Control && e.KeyCode == Keys.V && (!txtQuery.Focused || ClipboardHasImage()))
                {
                    e.SuppressKeyPress = true;
                    PasteImage();
                }
            };
            AttachDrop(this);
            FormClosed += delegate
            {
                searchGen++; selGen++;
                if (queue != null) queue.CompleteAdding();
                if (current != null) current.Dispose();
            };
        }

        static bool ClipboardHasImage()
        {
            try
            {
                if (Clipboard.ContainsImage() || Clipboard.ContainsFileDropList()) return true;
                string t = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                return t.StartsWith("http", StringComparison.OrdinalIgnoreCase) || t.StartsWith("data:image", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        void PasteImage()
        {
            try { HandleData(Clipboard.GetDataObject(), "clipboard"); }
            catch (Exception ex) { SetStatus("Paste failed: " + ex.Message); }
        }

        void SetStatus(string t)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(SetStatus), t); } catch { } return; }
            lblStatus.Text = t;
        }

        // ---------------- searching ----------------
        void DoSearch(bool more)
        {
            int source = cmbSource.SelectedIndex;
            SsCreds creds = null;
            if (source == Web.SrcScreenScraper)
            {
                creds = SsCreds.Load();
                if (!creds.HasDev)
                {
                    using (var f = new SsAccountForm()) f.ShowDialog(this);
                    creds = SsCreds.Load();
                    if (!creds.HasDev) { SetStatus("ScreenScraper needs developer credentials: click \"ScreenScraper account…\"."); return; }
                }
            }
            int gen = ++searchGen;
            if (!more)
            {
                foreach (Control c in pnlResults.Controls.Cast<Control>().ToList()) DisposeTile(c);
                pnlResults.Controls.Clear();
                nextFirst = 0;
                if (queue != null) queue.CompleteAdding();
                queue = new BlockingCollection<Tuple<int, WebResult, PictureBox, Control>>();
                var q = queue;
                // ScreenScraper allows few parallel downloads per user: one at a time there
                int workers = source == Web.SrcScreenScraper ? 1 : 6;
                for (int i = 0; i < workers; i++)
                    new Thread(() => ThumbWorker(q)) { IsBackground = true }.Start();
            }
            string query = txtQuery.Text.Trim();
            bool libretro = source == Web.SrcLibretro;
            bool screenscraper = source == Web.SrcScreenScraper;
            string ssName = Regex.Replace(query, @"\s*wonder\s*swan(\s*color)?\s*$", "", RegexOptions.IgnoreCase).Trim();
            int first = nextFirst;
            btnSearch.Enabled = false; btnMore.Enabled = false;
            SetStatus("Searching…");
            new Thread(() =>
            {
                List<WebResult> res = null; string err = null;
                try
                {
                    res = screenscraper ? ScreenScraper.Search(creds, romPath, game, ssName, SetStatus)
                        : libretro ? Web.Libretro(game) : Web.Bing(query, first);
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (gen != searchGen || IsDisposed) return;
                        btnSearch.Enabled = true;
                        if (err != null) { SetStatus("Search failed: " + err); return; }
                        nextFirst = first + 35;
                        btnMore.Enabled = !libretro && !screenscraper && res.Count > 0;
                        foreach (var r in res) AddTile(r, libretro);
                        SetStatus(libretro ? "Checking libretro thumbnails (missing ones disappear)…"
                                : screenscraper ? (res.Count == 0 ? "ScreenScraper: game not found (checksum and name)." : res.Count + " images — data and media courtesy of ScreenScraper.fr")
                                : pnlResults.Controls.Count + " results");
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        void AddTile(WebResult r, bool removeIfMissing)
        {
            var tile = new Panel { Size = new Size(168, 160), Margin = new Padding(4), BackColor = Color.FromArgb(60, 60, 60), Cursor = Cursors.Hand };
            var pb = new PictureBox { Size = new Size(160, 120), Location = new Point(4, 4), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            var lb = new Label
            {
                Location = new Point(4, 126), Size = new Size(160, 32), ForeColor = Color.Gainsboro,
                Text = (r.Title ?? "") + "\n" + Web.Host(r.PageUrl ?? r.ImageUrl), AutoEllipsis = true
            };
            tile.Controls.Add(pb); tile.Controls.Add(lb);
            tile.Tag = removeIfMissing;
            var tip = (r.Title ?? "") + "\n" + (r.ImageUrl ?? "");
            new ToolTip().SetToolTip(pb, tip);
            EventHandler click = delegate { SelectResult(r, tile); };
            tile.Click += click; pb.Click += click; lb.Click += click;
            AttachDrop(tile);
            pnlResults.Controls.Add(tile);
            try { queue.Add(Tuple.Create(searchGen, r, pb, (Control)tile)); } catch { }
        }

        void ThumbWorker(BlockingCollection<Tuple<int, WebResult, PictureBox, Control>> q)
        {
            try
            {
                foreach (var it in q.GetConsumingEnumerable())
                {
                    if (it.Item1 != searchGen) continue;
                    Bitmap b = null;
                    try
                    {
                        using (var full = WsBmp.LoadBytes(Web.Get(it.Item2.ThumbUrl, 15000, null)))
                            b = Shrink(full, 320);
                    }
                    catch { }
                    var item = it;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (IsDisposed || item.Item3.IsDisposed) { if (b != null) b.Dispose(); return; }
                            if (b != null) item.Item3.Image = b;
                            else if ((bool)item.Item4.Tag) { pnlResults.Controls.Remove(item.Item4); DisposeTile(item.Item4); }
                            else item.Item3.BackColor = Color.FromArgb(90, 30, 30);
                            if (cmbSource.SelectedIndex == 1)
                                SetStatus(pnlResults.Controls.Count == 0 ? "No libretro thumbnails for this exact name." : pnlResults.Controls.Count + " libretro thumbnails");
                        }));
                    }
                    catch { if (b != null) b.Dispose(); }
                }
            }
            catch { }
        }

        static Bitmap Shrink(Bitmap b, int max)
        {
            double s = Math.Min(1.0, (double)max / Math.Max(b.Width, b.Height));
            int w = Math.Max(1, (int)(b.Width * s)), h = Math.Max(1, (int)(b.Height * s));
            var o = new Bitmap(w, h);
            using (var g = Graphics.FromImage(o))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(b, 0, 0, w, h);
            }
            return o;
        }

        static void DisposeTile(Control c)
        {
            foreach (var pb in c.Controls.OfType<PictureBox>())
                if (pb.Image != null) { pb.Image.Dispose(); pb.Image = null; }
            c.Dispose();
        }

        // ---------------- selecting ----------------
        void SelectResult(WebResult r, Control tile)
        {
            if (selectedTile != null && !selectedTile.IsDisposed) selectedTile.BackColor = Color.FromArgb(60, 60, 60);
            selectedTile = tile;
            tile.BackColor = Color.FromArgb(0, 120, 215);
            int gen = ++selGen;
            lblSel.Text = (r.Title ?? "") + "\nDownloading…";
            btnAddFull.Enabled = btnUseThumb.Enabled = false;
            new Thread(() =>
            {
                Bitmap b = null; string note = null;
                if (!string.IsNullOrEmpty(r.ImageUrl))
                {
                    try { b = WsBmp.LoadBytes(Web.Get(r.ImageUrl, 20000, r.PageUrl)); }
                    catch (Exception ex) { note = "full image unavailable (" + ex.Message + "), using the thumbnail"; }
                }
                if (b == null && !string.IsNullOrEmpty(r.ThumbUrl) && r.ThumbUrl != r.ImageUrl)
                {
                    try { b = WsBmp.LoadBytes(Web.Get(r.ThumbUrl, 15000, null)); }
                    catch (Exception ex) { note = "download failed: " + ex.Message; }
                }
                string info = (r.Title ?? "") + "\n" + Web.Host(r.PageUrl ?? r.ImageUrl);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (gen != selGen || IsDisposed) { if (b != null) b.Dispose(); return; }
                        SetCurrent(b, info, note);
                        currentSource = r.Source;
                    }));
                }
                catch { if (b != null) b.Dispose(); }
            }) { IsBackground = true }.Start();
        }

        void SetCurrent(Bitmap b, string info, string note)
        {
            if (current != null) current.Dispose();
            current = b;
            currentSource = null;
            SetPic(picFull, null); SetPic(picThumb, null);
            if (b == null)
            {
                lblSel.Text = info + "\n" + (note ?? "could not load the image");
                btnAddFull.Enabled = btnUseThumb.Enabled = false;
                return;
            }
            lblSel.Text = info + "\n" + b.Width + "×" + b.Height + (note != null ? "  — " + note : "");
            try
            {
                using (var f = WsBmp.Render(b, WsBmp.FullW, WsBmp.FullH, fullContain, 0, dither))
                    SetPic(picFull, MainForm.Zoom(f, 2, false));
                using (var t = WsBmp.Render(b, WsBmp.ThumbW, WsBmp.ThumbH, thumbContain, 2, dither))
                    SetPic(picThumb, MainForm.Zoom(t, 2, true));
            }
            catch (Exception ex) { lblSel.Text += "\nconversion failed: " + ex.Message; }
            btnAddFull.Enabled = btnUseThumb.Enabled = true;
            lblSaved.Text = "";
        }

        static void SetPic(PictureBox p, Image img)
        {
            if (p.Image != null) p.Image.Dispose();
            p.Image = img;
        }

        // ---------------- drag & drop / paste ----------------
        void AttachDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (o, e) => { e.Effect = DragDropEffects.Copy; };
            c.DragDrop += (o, e) => { try { HandleData(e.Data, "dropped"); } catch (Exception ex) { SetStatus("Drop failed: " + ex.Message); } };
            foreach (Control ch in c.Controls) AttachDrop(ch);
        }

        static readonly Regex ImgSrc = new Regex("<img[^>]*?\\ssrc\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);

        void HandleData(IDataObject d, string how)
        {
            if (d == null) return;
            if (d.GetDataPresent(DataFormats.FileDrop))
            {
                var files = d.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    LoadAsync(how + " file", () => WsBmp.Load(files[0]), Path.GetFileName(files[0]));
                    return;
                }
            }
            if (d.GetDataPresent(DataFormats.Bitmap))
            {
                var img = d.GetData(DataFormats.Bitmap) as Image;
                if (img != null)
                {
                    selGen++;
                    ClearSelectionTile();
                    SetCurrent(WsBmp.FromImage(img), "Image from " + how, null);
                    return;
                }
            }
            string url = null;
            if (d.GetDataPresent(DataFormats.Html))
            {
                var html = d.GetData(DataFormats.Html) as string;
                if (html != null)
                {
                    var m = ImgSrc.Match(html);
                    if (m.Success) url = WebUtility.HtmlDecode(m.Groups[1].Value);
                }
            }
            if (url == null)
            {
                string text = (d.GetData(DataFormats.UnicodeText) as string) ?? (d.GetData(DataFormats.Text) as string);
                if (text != null) url = text.Trim().Split('\n')[0].Trim();
            }
            if (string.IsNullOrEmpty(url)) { SetStatus("Nothing usable in the " + how + " data."); return; }

            if (url.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                int comma = url.IndexOf(',');
                var bytes = Convert.FromBase64String(url.Substring(comma + 1));
                LoadAsync(how + " image", () => WsBmp.LoadBytes(bytes), "embedded image");
                return;
            }
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { SetStatus("Not an image or a web link."); return; }
            string u = url;
            LoadAsync(how + " link", () => WsBmp.LoadBytes(Web.Get(u, 20000, null)), Web.Host(u));
        }

        void ClearSelectionTile()
        {
            if (selectedTile != null && !selectedTile.IsDisposed) selectedTile.BackColor = Color.FromArgb(60, 60, 60);
            selectedTile = null;
        }

        void LoadAsync(string what, Func<Bitmap> loader, string label)
        {
            ClearSelectionTile();
            int gen = ++selGen;
            lblSel.Text = "Loading " + what + "…";
            btnAddFull.Enabled = btnUseThumb.Enabled = false;
            new Thread(() =>
            {
                Bitmap b = null; string err = null;
                try { b = loader(); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (gen != selGen || IsDisposed) { if (b != null) b.Dispose(); return; }
                        SetCurrent(b, "From " + what + ": " + label, err);
                    }));
                }
                catch { if (b != null) b.Dispose(); }
            }) { IsBackground = true }.Start();
        }

        // ---------------- saving ----------------
        void Save(bool thumb)
        {
            if (current == null) return;
            try
            {
                string dir = Path.Combine(imgDir, Matcher.WebDir);
                Directory.CreateDirectory(dir);
                string path;
                if (thumb) path = Path.Combine(dir, game + "_thumb.png");
                else
                {
                    int n = 1;
                    while (n < 99 && File.Exists(Path.Combine(dir, game + "_" + n + ".png"))) n++;
                    path = Path.Combine(dir, game + "_" + n + ".png");
                }
                current.Save(path, ImageFormat.Png);
                Changed = true;
                lblSaved.Text = (currentSource == "ScreenScraper" ? "Image from ScreenScraper.fr. " : "") +
                                "Saved as " + Matcher.WebDir + "\\" + Path.GetFileName(path) +
                                (thumb ? " (thumbnail)" : " (full-screen image, shown before the folder images)");
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not save the image: " + ex.Message, Text); }
        }
    }

    // ------------------------------------------------------------------
    // Options snapshot used by the build and the preview
    // ------------------------------------------------------------------
    class RunOptions
    {
        public string RomDir, ImgDir, OutRoot, Ext, ThumbSub;
        public bool OnePerSub, RomSub, ImgSub, Dither, Fuzzy, Covers, MakeThumb, SkipEmpty, FullContain, ThumbContain, Preview;
        public int MaxFull;
    }

    // ------------------------------------------------------------------
    // User interface
    // ------------------------------------------------------------------
    public class MainForm : Form
    {
        TextBox txtRom, txtExt, txtImg, txtOut, txtLog;
        CheckBox chkRomSub, chkImgSub, chkOnePerSub, chkStruct, chkDither, chkFuzzy, chkCovers, chkThumb, chkSkipNoImg;
        ComboBox cmbFull, cmbThumb, cmbThumbSub, cmbZoom;
        NumericUpDown numMax;
        Button btnPreview, btnRun, btnOpen, btnCancel, btnWeb;
        ProgressBar progress;
        Label lblStatus, lblPreviewInfo;
        TabControl tabs;
        TabPage tabLog, tabPreview;
        ListBox lstGames;
        FlowLayoutPanel pnlImages;
        volatile bool cancel;
        Thread worker;
        int previewGen;
        bool loading;
        List<GameJob> jobs = new List<GameJob>();

        const string DefaultExt = "ws;wsc;pc2;pcv2;bps;ips;ups";
        static readonly Color FrameColor = Color.FromArgb(255, 160, 0);

        public MainForm()
        {
            Text = "SwanShell Image Builder " + Program.Version + " — game images for nileswan / SwanShellDX";
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            var wa = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(Math.Min(820, wa.Width), Math.Min(600, wa.Height));
            Size = new Size(Math.Min(1040, wa.Width - 20), Math.Min(860, wa.Height - 20));
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
            for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            // --- folders ---
            var gFolders = new GroupBox { Text = "Folders", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
            var tf = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            tf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            gFolders.Controls.Add(tf);

            txtRom = AddFolderRow(tf, 0, "ROM folder (names):");
            chkRomSub = new CheckBox { Text = "include subfolders", AutoSize = true };
            txtExt = new TextBox { Text = DefaultExt, Width = 260 };
            var extPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            extPanel.Controls.Add(new Label { Text = "ROM extensions:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            extPanel.Controls.Add(txtExt);
            extPanel.Controls.Add(chkRomSub);
            tf.Controls.Add(extPanel, 1, 1);

            txtImg = AddFolderRow(tf, 2, "Image folder:");
            chkImgSub = new CheckBox { Text = "include subfolders (e.g. images, screenshot, mix)", AutoSize = true, Checked = true };
            chkOnePerSub = new CheckBox { Text = "one image per subfolder", AutoSize = true, Checked = true };
            var subPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            subPanel.Controls.Add(chkImgSub); subPanel.Controls.Add(chkOnePerSub);
            tf.Controls.Add(subPanel, 1, 3);

            txtOut = AddFolderRow(tf, 4, "Destination:");
            chkStruct = new CheckBox { Text = "create the NILESWAN\\IMAGES\\<game> structure (pick the SD card root)", AutoSize = true, Checked = true };
            tf.Controls.Add(chkStruct, 1, 5);
            root.Controls.Add(gFolders, 0, 0);

            // --- options ---
            var gOpt = new GroupBox { Text = "Options", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
            var to = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            to.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            to.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            to.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            to.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            gOpt.Controls.Add(to);

            string[] modes = { "Crop to fill (centre)", "Fit (black bars)" };
            cmbFull = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
            cmbFull.Items.AddRange(modes); cmbFull.SelectedIndex = 0;
            cmbThumb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
            cmbThumb.Items.AddRange(modes); cmbThumb.SelectedIndex = 0;
            numMax = new NumericUpDown { Minimum = 0, Maximum = 6, Value = 6, Width = 50 };

            to.Controls.Add(Lbl("Full screen 224×144:"), 0, 0); to.Controls.Add(cmbFull, 1, 0);
            to.Controls.Add(Lbl("Images per game (max 6):"), 2, 0); to.Controls.Add(numMax, 3, 0);
            to.Controls.Add(Lbl("Thumbnail 112×72:"), 0, 1); to.Controls.Add(cmbThumb, 1, 1);

            var thumbPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            chkThumb = new CheckBox { Text = "create thumb.bmp", AutoSize = true, Checked = true, Padding = new Padding(0, 3, 0, 0) };
            thumbPanel.Controls.Add(chkThumb);
            thumbPanel.Controls.Add(new Label { Text = "from:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
            cmbThumbSub = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
            cmbThumbSub.Items.Add("(automatic)"); cmbThumbSub.SelectedIndex = 0;
            thumbPanel.Controls.Add(cmbThumbSub);
            to.Controls.Add(thumbPanel, 2, 1);
            to.SetColumnSpan(thumbPanel, 2);

            var flags = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            chkDither = new CheckBox { Text = "dithering (Floyd-Steinberg)", AutoSize = true, Checked = true };
            chkFuzzy = new CheckBox { Text = "loose matching (ignore (Japan), [!], case, punctuation)", AutoSize = true, Checked = true };
            chkCovers = new CheckBox { Text = "also use covers as full-screen images", AutoSize = true, Checked = true };
            chkSkipNoImg = new CheckBox { Text = "skip games without images", AutoSize = true, Checked = true };
            flags.Controls.Add(chkDither); flags.Controls.Add(chkFuzzy); flags.Controls.Add(chkCovers); flags.Controls.Add(chkSkipNoImg);
            to.Controls.Add(flags, 0, 2);
            to.SetColumnSpan(flags, 4);
            root.Controls.Add(gOpt, 0, 1);

            // --- buttons ---
            var bp = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 4, 0, 4) };
            btnPreview = new Button { Text = "Preview", AutoSize = true };
            btnRun = new Button { Text = "Build images", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            btnCancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
            btnOpen = new Button { Text = "Open destination", AutoSize = true };
            bp.Controls.Add(btnPreview); bp.Controls.Add(btnRun); bp.Controls.Add(btnCancel); bp.Controls.Add(btnOpen);
            bp.Controls.Add(new Label { Text = "Preview zoom:", AutoSize = true, Padding = new Padding(16, 7, 0, 0) });
            cmbZoom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 55 };
            cmbZoom.Items.AddRange(new object[] { "1×", "2×", "3×" }); cmbZoom.SelectedIndex = 1;
            bp.Controls.Add(cmbZoom);
            root.Controls.Add(bp, 0, 2);

            var sp = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
            sp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            progress = new ProgressBar { Dock = DockStyle.Fill, Height = 18 };
            lblStatus = new Label { AutoSize = true, Text = "Ready", Padding = new Padding(6, 2, 0, 0) };
            sp.Controls.Add(progress, 0, 0); sp.Controls.Add(lblStatus, 1, 0);
            root.Controls.Add(sp, 0, 3);

            // --- tabs: preview / log ---
            tabs = new TabControl { Dock = DockStyle.Fill };
            tabPreview = new TabPage("Preview");
            tabLog = new TabPage("Log");
            tabs.TabPages.Add(tabPreview);
            tabs.TabPages.Add(tabLog);
            root.Controls.Add(tabs, 0, 4);

            txtLog = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window
            };
            tabLog.Controls.Add(txtLog);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            tabPreview.Controls.Add(split);
            lstGames = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
            split.Panel1.Controls.Add(lstGames);
            var head = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnWeb = new Button { Text = "Search images online…", AutoSize = true, Enabled = false };
            lblPreviewInfo = new Label
            {
                AutoSize = true, Padding = new Padding(6, 6, 6, 0), MaximumSize = new Size(700, 0),
                Text = "Click \"Preview\" to list the games, then select one to see its images exactly as they will be written (16 colours)."
            };
            head.Controls.Add(btnWeb, 0, 0); head.Controls.Add(lblPreviewInfo, 1, 0);
            pnlImages = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6), BackColor = Color.FromArgb(48, 48, 48) };
            split.Panel2.Controls.Add(pnlImages);
            split.Panel2.Controls.Add(head);
            Load += delegate
            {
                int sd = 300;
                string v;
                var st = Settings.Load();
                if (st.TryGetValue("split", out v)) int.TryParse(v, out sd);
                try { split.SplitterDistance = Math.Max(120, Math.Min(sd, split.Width - 200)); } catch { }
            };
            FormClosing += delegate
            {
                var d = new Dictionary<string, string>();
                d["split"] = split.SplitterDistance.ToString();
                Settings.Save(d);
            };

            btnPreview.Click += delegate { Start(true); };
            btnRun.Click += delegate { Start(false); };
            btnCancel.Click += delegate { cancel = true; };
            btnOpen.Click += delegate
            {
                string d = OutputRoot();
                if (Directory.Exists(d)) System.Diagnostics.Process.Start("explorer.exe", "\"" + d + "\"");
                else if (Directory.Exists(txtOut.Text)) System.Diagnostics.Process.Start("explorer.exe", "\"" + txtOut.Text + "\"");
            };
            lstGames.SelectedIndexChanged += delegate { btnWeb.Enabled = lstGames.SelectedIndex >= 0; ShowPreview(); };
            lstGames.DoubleClick += delegate { OpenWebPicker(); };
            btnWeb.Click += delegate { OpenWebPicker(); };
            txtImg.TextChanged += delegate { RefreshThumbSubs(null); };

            // changing a conversion option refreshes the preview of the selected game
            EventHandler redraw = delegate { if (!loading) ShowPreview(); };
            cmbFull.SelectedIndexChanged += redraw; cmbThumb.SelectedIndexChanged += redraw;
            chkDither.CheckedChanged += redraw; chkThumb.CheckedChanged += redraw; cmbZoom.SelectedIndexChanged += redraw;

            // changing a matching option makes the list stale
            EventHandler stale = delegate { if (!loading && jobs.Count > 0) lblPreviewInfo.Text = "Matching options changed: click \"Preview\" again to update the list."; };
            numMax.ValueChanged += stale; cmbThumbSub.SelectedIndexChanged += stale; chkOnePerSub.CheckedChanged += stale;
            chkCovers.CheckedChanged += stale; chkFuzzy.CheckedChanged += stale; chkImgSub.CheckedChanged += stale;

            loading = true;
            LoadSettings();
            loading = false;
            FormClosing += delegate { cancel = true; previewGen++; SaveSettings(); };
            RestoreWindow();

            Log("Format: full screen 224×144 and thumbnail 112×72, 4-bit / 16-colour uncompressed BMP, under 65535 bytes.");
            Log("Matching: an image belongs to a game when its name equals the ROM name (without extension),");
            Log("  or the ROM name followed by _1, -2, \" 3\", _title, _snap, _cover, _box, _thumb...");
            Log("A hex hash at the end of image names (e.g. \" 9B23BC...\") is ignored.");
            Log("Thumbnail: from the subfolder chosen in \"from:\"; when automatic, from *_thumb, else the cover");
            Log("  (*_cover, *_box or a Boxart folder), else the first image.");
            Log("");
        }

        static Label Lbl(string t) { return new Label { Text = t, AutoSize = true, Padding = new Padding(0, 6, 4, 0) }; }

        TextBox AddFolderRow(TableLayoutPanel t, int row, string label)
        {
            t.Controls.Add(Lbl(label), 0, row);
            var tb = new TextBox { Dock = DockStyle.Fill };
            t.Controls.Add(tb, 1, row);
            var b = new Button { Text = "Browse…", AutoSize = true };
            b.Click += delegate
            {
                using (var d = new FolderBrowserDialog())
                {
                    d.Description = label;
                    d.ShowNewFolderButton = true;
                    if (Directory.Exists(tb.Text)) d.SelectedPath = tb.Text;
                    if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath;
                }
            };
            t.Controls.Add(b, 2, row);
            return tb;
        }

        void RefreshThumbSubs(string want)
        {
            string cur = want ?? (cmbThumbSub.SelectedIndex > 0 ? (string)cmbThumbSub.SelectedItem : null);
            cmbThumbSub.Items.Clear();
            cmbThumbSub.Items.Add("(automatic)");
            if (Directory.Exists(txtImg.Text.Trim()))
                foreach (var d in Matcher.SubDirs(txtImg.Text.Trim())) cmbThumbSub.Items.Add(d);
            int i = cur == null ? -1 : cmbThumbSub.FindStringExact(cur);
            cmbThumbSub.SelectedIndex = i > 0 ? i : 0;
        }

        // ---------------- settings ----------------
        void LoadSettings()
        {
            var s = Settings.Load();
            string v;
            if (s.TryGetValue("rom", out v)) txtRom.Text = v;
            if (s.TryGetValue("img", out v)) txtImg.Text = v;
            if (s.TryGetValue("out", out v)) txtOut.Text = v;
            if (s.TryGetValue("ext", out v) && v.Trim().Length > 0) txtExt.Text = v;
            if (s.TryGetValue("full", out v)) { int i; if (int.TryParse(v, out i) && i >= 0 && i < 2) cmbFull.SelectedIndex = i; }
            if (s.TryGetValue("thumb", out v)) { int i; if (int.TryParse(v, out i) && i >= 0 && i < 2) cmbThumb.SelectedIndex = i; }
            if (s.TryGetValue("zoom", out v)) { int i; if (int.TryParse(v, out i) && i >= 1 && i <= 3) cmbZoom.SelectedIndex = i - 1; }
            if (s.TryGetValue("max", out v)) { int i; if (int.TryParse(v, out i) && i >= 0 && i <= 6) numMax.Value = i; }
            Bool(s, "romsub", chkRomSub); Bool(s, "imgsub", chkImgSub); Bool(s, "struct", chkStruct);
            Bool(s, "dither", chkDither); Bool(s, "fuzzy", chkFuzzy); Bool(s, "covers", chkCovers);
            Bool(s, "mkthumb", chkThumb); Bool(s, "skip", chkSkipNoImg); Bool(s, "onepersub", chkOnePerSub);
            RefreshThumbSubs(s.TryGetValue("thumbsub", out v) ? v : null);
        }

        // Window size/position/state, restored only if it is still on a connected screen.
        void RestoreWindow()
        {
            var s = Settings.Load();
            string v;
            if (!s.TryGetValue("window", out v)) return;
            var p = v.Split(',');
            int x, y, w, h;
            if (p.Length < 4 || !int.TryParse(p[0], out x) || !int.TryParse(p[1], out y) ||
                !int.TryParse(p[2], out w) || !int.TryParse(p[3], out h)) return;
            var r = new Rectangle(x, y, Math.Max(MinimumSize.Width, w), Math.Max(MinimumSize.Height, h));
            if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
            if (p.Length > 4 && p[4] == "max") WindowState = FormWindowState.Maximized;
        }

        static void Bool(Dictionary<string, string> s, string k, CheckBox c)
        {
            string v; if (s.TryGetValue(k, out v)) c.Checked = v == "1";
        }

        void SaveSettings()
        {
            var s = new Dictionary<string, string>();
            s["rom"] = txtRom.Text; s["img"] = txtImg.Text; s["out"] = txtOut.Text; s["ext"] = txtExt.Text;
            s["full"] = cmbFull.SelectedIndex.ToString(); s["thumb"] = cmbThumb.SelectedIndex.ToString();
            s["max"] = ((int)numMax.Value).ToString();
            s["zoom"] = (cmbZoom.SelectedIndex + 1).ToString();
            s["romsub"] = chkRomSub.Checked ? "1" : "0"; s["imgsub"] = chkImgSub.Checked ? "1" : "0";
            s["struct"] = chkStruct.Checked ? "1" : "0"; s["dither"] = chkDither.Checked ? "1" : "0";
            s["fuzzy"] = chkFuzzy.Checked ? "1" : "0"; s["covers"] = chkCovers.Checked ? "1" : "0";
            s["mkthumb"] = chkThumb.Checked ? "1" : "0"; s["skip"] = chkSkipNoImg.Checked ? "1" : "0";
            s["onepersub"] = chkOnePerSub.Checked ? "1" : "0";
            s["thumbsub"] = cmbThumbSub.SelectedIndex > 0 ? (string)cmbThumbSub.SelectedItem : "";
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (WindowState != FormWindowState.Minimized || RestoreBounds.Width > 0)
                s["window"] = b.X + "," + b.Y + "," + b.Width + "," + b.Height + (WindowState == FormWindowState.Maximized ? ",max" : "");
            Settings.Save(s);
        }

        // ---------------- helpers ----------------
        string OutputRoot()
        {
            string o = txtOut.Text.Trim();
            return chkStruct.Checked ? Path.Combine(o, "NILESWAN", "IMAGES") : o;
        }

        RunOptions GetOptions(bool preview)
        {
            return new RunOptions
            {
                RomDir = txtRom.Text.Trim(), ImgDir = txtImg.Text.Trim(), OutRoot = OutputRoot(), Ext = txtExt.Text,
                RomSub = chkRomSub.Checked, ImgSub = chkImgSub.Checked, Dither = chkDither.Checked,
                Fuzzy = chkFuzzy.Checked, Covers = chkCovers.Checked, MakeThumb = chkThumb.Checked,
                SkipEmpty = chkSkipNoImg.Checked, MaxFull = (int)numMax.Value,
                OnePerSub = chkOnePerSub.Checked && chkImgSub.Checked,
                ThumbSub = cmbThumbSub.SelectedIndex > 0 ? (string)cmbThumbSub.SelectedItem : null,
                FullContain = cmbFull.SelectedIndex == 1, ThumbContain = cmbThumb.SelectedIndex == 1,
                Preview = preview
            };
        }

        void Log(string s)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(Log), s); } catch { } return; }
            txtLog.AppendText(s + Environment.NewLine);
        }

        void SetProgress(int v, int max, string status)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<int, int, string>(SetProgress), v, max, status); } catch { } return; }
            progress.Maximum = Math.Max(1, max);
            progress.Value = Math.Min(progress.Maximum, Math.Max(0, v));
            lblStatus.Text = status;
        }

        void SetBusy(bool busy)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<bool>(SetBusy), busy); } catch { } return; }
            btnPreview.Enabled = !busy; btnRun.Enabled = !busy; btnCancel.Enabled = busy;
        }

        void SetJobs(List<GameJob> list)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<List<GameJob>>(SetJobs), list); } catch { } return; }
            string prev = lstGames.SelectedIndex >= 0 && lstGames.SelectedIndex < jobs.Count ? jobs[lstGames.SelectedIndex].Folder : null;
            jobs = list;
            lstGames.BeginUpdate();
            lstGames.Items.Clear();
            foreach (var j in list) lstGames.Items.Add(ItemText(j));
            lstGames.EndUpdate();
            int with = list.Count(j => j.Full.Count > 0 || j.Thumb != null);
            lblPreviewInfo.Text = list.Count + " games, " + with + " with images, " + (list.Count - with) +
                                  " without.  ≈ = loose match,  – = no images.  Select a game to see its images.";
            int sel = prev == null ? -1 : list.FindIndex(j => j.Folder == prev);
            if (sel < 0) sel = list.FindIndex(j => j.Full.Count > 0 || j.Thumb != null);
            if (sel >= 0 && sel == lstGames.SelectedIndex) ShowPreview();
            else if (sel >= 0) lstGames.SelectedIndex = sel;
            else ClearImages();
        }

        static string ItemText(GameJob j)
        {
            int n = j.Full.Count + (j.Thumb != null ? 1 : 0);
            return (n == 0 ? "–  " : (j.Fuzzy ? "≈  " : "    ")) + j.Folder + (n == 0 ? "   (no images)" : "");
        }

        void OpenWebPicker()
        {
            int i = lstGames.SelectedIndex;
            if (i < 0 || i >= jobs.Count) return;
            string imgDir = txtImg.Text.Trim();
            if (!Directory.Exists(imgDir)) { MessageBox.Show(this, "Please select a valid image folder first: picked images are saved in its \"" + Matcher.WebDir + "\" subfolder.", Text); return; }
            bool changed;
            using (var dlg = new WebPickerForm(jobs[i].Folder, jobs[i].RomPath, imgDir, cmbFull.SelectedIndex == 1, cmbThumb.SelectedIndex == 1, chkDither.Checked))
            {
                dlg.ShowDialog(this);
                changed = dlg.Changed;
            }
            if (changed) RematchSelected();
        }

        // Re-matches only the selected game (after images were added or removed) and refreshes its preview.
        void RematchSelected()
        {
            int i = lstGames.SelectedIndex;
            if (i < 0 || i >= jobs.Count) return;
            var o = GetOptions(true);
            try
            {
                var images = Matcher.IndexImages(o.ImgDir, o.ImgSub);
                var nj = Matcher.Match(jobs[i].RomPath, images, o.Fuzzy, o.Covers, o.MaxFull, o.OnePerSub, o.ThumbSub);
                if (!o.MakeThumb) nj.Thumb = null;
                jobs[i] = nj;
                lstGames.Items[i] = ItemText(nj);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            ShowPreview();
        }

        static bool SafeFolder(string name)
        {
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        // ---------------- preview of the selected game ----------------
        void ClearImages()
        {
            var old = pnlImages.Controls.Cast<Control>().ToList();
            pnlImages.Controls.Clear();
            foreach (var c in old)
            {
                foreach (var pb in c.Controls.OfType<PictureBox>())
                    if (pb.Image != null) { pb.Image.Dispose(); pb.Image = null; }
                c.Dispose();
            }
            pnlImages.AutoScrollPosition = new Point(0, 0);
        }

        string GameDir(GameJob job)
        {
            return txtOut.Text.Trim().Length > 0 ? Path.Combine(OutputRoot(), job.Folder) : null;
        }

        void ShowPreview()
        {
            int gen = ++previewGen;
            ClearImages();
            int i = lstGames.SelectedIndex;
            if (i < 0 || i >= jobs.Count) return;
            var job = jobs[i];
            var o = GetOptions(true);
            int zoom = cmbZoom.SelectedIndex + 1;
            string gdir = GameDir(job);
            pnlImages.Controls.Add(new Label { Text = "Preparing preview…", AutoSize = true, ForeColor = Color.White, Padding = new Padding(8) });

            new Thread(() =>
            {
                GamePlan plan = null; string err = null;
                try
                {
                    plan = Planner.Make(job, gdir, o, true);
                    foreach (var e in plan.Existing)
                        try { using (var b = WsBmp.Load(e.Path)) e.Preview = Zoom(b, zoom, e.IsThumb); } catch { }
                    foreach (var n in plan.New)
                        if (n.Preview != null) { var z = Zoom(n.Preview, zoom, n.IsThumb); n.Preview.Dispose(); n.Preview = z; }
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (gen != previewGen || IsDisposed) { Planner.Dispose(plan); return; }
                        ClearImages();
                        if (err != null) { AddHeader("ERROR: " + err, Color.OrangeRed, null); return; }
                        RenderPlan(plan);
                    }));
                }
                catch { Planner.Dispose(plan); }
            }) { IsBackground = true }.Start();
        }

        void AddHeader(string text, Color color, Control extra)
        {
            if (pnlImages.Controls.Count > 0) pnlImages.SetFlowBreak(pnlImages.Controls[pnlImages.Controls.Count - 1], true);
            var hp = new FlowLayoutPanel
            {
                AutoSize = true, WrapContents = true, Margin = new Padding(6, 10, 6, 2),
                MaximumSize = new Size(Math.Max(300, pnlImages.ClientSize.Width - 40), 0)
            };
            hp.Controls.Add(new Label { Text = text, AutoSize = true, ForeColor = color, Font = new Font(Font, FontStyle.Bold), MaximumSize = new Size(560, 0) });
            if (extra != null) hp.Controls.Add(extra);
            pnlImages.Controls.Add(hp);
            pnlImages.SetFlowBreak(hp, true);
        }

        bool ConfirmDelete(string what)
        {
            return MessageBox.Show(this, "Permanently delete " + what + "?\n\nThis cannot be undone.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        void RenderPlan(GamePlan plan)
        {
            // ---- what is already on the card
            if (plan.Dir == null)
                AddHeader("Destination not set: images already on the card cannot be shown.", Color.Silver, null);
            else if (plan.Existing.Count == 0)
                AddHeader("Nothing on the card yet for this game  (" + plan.Dir + ")", Color.Silver, null);
            else
            {
                var existing = plan.Existing.ToList();
                var delAll = new LinkLabel { Text = "Delete all of them…", AutoSize = true, LinkColor = Color.Orange, Margin = new Padding(12, 3, 3, 3) };
                delAll.LinkClicked += delegate
                {
                    if (!ConfirmDelete("these " + existing.Count + " files in\n" + plan.Dir + "\n\n" + string.Join("\n", existing.Select(e => e.Name)))) return;
                    foreach (var e in existing)
                        try { File.Delete(e.Path); } catch (Exception ex) { MessageBox.Show(this, e.Name + ": " + ex.Message, Text); }
                    ShowPreview();
                };
                AddHeader("Already on the card (kept as they are)  —  " + plan.Dir, Color.LightSkyBlue, delAll);
                foreach (var e in plan.Existing)
                {
                    var ex = e;
                    string where = e.IsThumb ? "thumbnail"
                        : (e.ShownIndex > 0 ? "shown as image " + e.ShownIndex : "NOT shown: the menu shows only the first " + Planner.MaxShown);
                    var lines = new List<Control> { InfoLabel(where, Color.Silver) };
                    if (e.Problem != null) lines.Add(InfoLabel("⚠ the menu ignores this file: " + e.Problem, Color.Orange));
                    var del = new LinkLabel { Text = "Delete…", AutoSize = true, LinkColor = Color.Orange };
                    del.LinkClicked += delegate
                    {
                        if (!ConfirmDelete("\n" + ex.Path)) return;
                        try { File.Delete(ex.Path); } catch (Exception err) { MessageBox.Show(this, err.Message, Text); return; }
                        ShowPreview();
                    };
                    lines.Add(del);
                    AddBox(e.Name, Color.White, lines, e.Preview, "cannot display this file");
                }
            }

            // ---- what Build images will add
            if (plan.New.Count == 0)
            {
                AddHeader("No source images found for this game.", Color.Silver, null);
                return;
            }
            int writes = plan.New.Count(n => n.OutName != null);
            AddHeader("From your image folders: " + writes + " new file(s) will be written by \"Build images\" — nothing existing is overwritten", Color.LightGreen, null);
            foreach (var n in plan.New)
            {
                var it = n;
                string title = n.OutName != null ? "→ " + n.OutName + "  (new)" : (n.IsThumb ? "thumbnail — not written" : "not written");
                if (n.IsThumb && n.OutName != null) title += "   (orange = 2 px frame hidden by the menu)";
                var lines = new List<Control> { InfoLabel("← " + n.Source.Rel, Color.Silver) };
                if (n.Skip != null) lines.Add(InfoLabel(n.Skip, Color.Orange));
                if (n.Error != null) lines.Add(InfoLabel("ERROR: " + n.Error, Color.OrangeRed));
                if (Matcher.IsWeb(n.Source))
                {
                    int uses = plan.New.Count(x => x.Source.Path == it.Source.Path);
                    var rm = new LinkLabel
                    {
                        Text = "Delete the downloaded file " + Path.GetFileName(n.Source.Path) +
                               (uses > 1 ? " (it is the source of " + (n.IsThumb ? "this thumbnail and of an image too" : "this image and of the thumbnail too") + ")" : ""),
                        AutoSize = true, LinkColor = Color.Orange, MaximumSize = new Size(470, 0)
                    };
                    rm.LinkClicked += delegate
                    {
                        if (!ConfirmDelete("the downloaded image\n" + it.Source.Path)) return;
                        try { File.Delete(it.Source.Path); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); return; }
                        RematchSelected();
                    };
                    lines.Add(rm);
                }
                AddBox(title, n.OutName != null ? Color.White : Color.Gray, lines, n.Preview, null);
            }
        }

        static Label InfoLabel(string text, Color c)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = c, MaximumSize = new Size(470, 0) };
        }

        void AddBox(string title, Color titleColor, List<Control> lines, Bitmap img, string noImageText)
        {
            var box = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
                Margin = new Padding(6), BackColor = Color.FromArgb(48, 48, 48)
            };
            box.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = titleColor, Font = new Font(Font, FontStyle.Bold) });
            foreach (var l in lines) box.Controls.Add(l);
            if (img != null)
                box.Controls.Add(new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 4, 0, 0) });
            else if (noImageText != null)
                box.Controls.Add(InfoLabel(noImageText, Color.OrangeRed));
            pnlImages.Controls.Add(box);
        }

        // Nearest-neighbour zoom; for the thumbnail also draws the 2-pixel frame the menu puts over it.
        internal static Bitmap Zoom(Bitmap b, int z, bool frame)
        {
            var o = new Bitmap(b.Width * z, b.Height * z, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(o))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(b, new Rectangle(0, 0, o.Width, o.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel);
                if (frame)
                    using (var br = new SolidBrush(Color.FromArgb(200, FrameColor)))
                    {
                        int f = 2 * z;
                        g.FillRectangle(br, 0, 0, o.Width, f);
                        g.FillRectangle(br, 0, o.Height - f, o.Width, f);
                        g.FillRectangle(br, 0, f, f, o.Height - 2 * f);
                        g.FillRectangle(br, o.Width - f, f, f, o.Height - 2 * f);
                    }
            }
            return o;
        }

        // ---------------- preview / build run ----------------
        void Start(bool preview)
        {
            var o = GetOptions(preview);
            if (!Directory.Exists(o.RomDir)) { MessageBox.Show(this, "Please select a valid ROM folder.", Text); return; }
            if (!Directory.Exists(o.ImgDir)) { MessageBox.Show(this, "Please select a valid image folder.", Text); return; }
            if (!preview && txtOut.Text.Trim().Length == 0) { MessageBox.Show(this, "Please select the destination folder.", Text); return; }
            SaveSettings();

            txtLog.Clear();
            tabs.SelectedTab = preview ? tabPreview : tabLog;
            progress.Value = 0;
            lblStatus.Text = preview ? "Preview…" : "Building…";
            cancel = false;
            SetBusy(true);
            worker = new Thread(() => Run(o)) { IsBackground = true };
            worker.Start();
        }

        void Run(RunOptions o)
        {
            int made = 0, games = 0, noImg = 0, errors = 0, skipped = 0;
            var list = new List<GameJob>();
            try
            {
                var roms = Matcher.FindRoms(o.RomDir, o.Ext, o.RomSub);
                Log("ROMs found: " + roms.Count);
                var images = Matcher.IndexImages(o.ImgDir, o.ImgSub);
                Log("Images found: " + images.Count);
                if (!o.Preview) Log("Destination: " + o.OutRoot);
                Log("");

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < roms.Count && !cancel; i++)
                {
                    var job = Matcher.Match(roms[i], images, o.Fuzzy, o.Covers, o.MaxFull, o.OnePerSub, o.ThumbSub);
                    if (!o.MakeThumb) job.Thumb = null;
                    SetProgress(i, roms.Count, (i + 1) + " / " + roms.Count);
                    if (!seen.Add(job.Folder)) { Log("= " + job.Folder + "  (already processed, duplicate ROM)"); continue; }
                    list.Add(job);

                    bool hasAny = job.Full.Count > 0 || job.Thumb != null;
                    if (!hasAny) { noImg++; Log("- " + job.Folder + "  : no images"); if (o.SkipEmpty) continue; }
                    if (!SafeFolder(job.Folder)) { errors++; Log("! " + job.Folder + "  : not a valid folder name"); continue; }

                    Log("+ " + job.Folder + (job.Fuzzy ? "   [loose match]" : ""));
                    if (hasAny) games++;
                    if (o.Preview)
                    {
                        if (job.Thumb != null) Log("     thumbnail <- " + job.Thumb.Rel);
                        foreach (var f in job.Full) Log("     image     <- " + f.Rel);
                        continue;
                    }

                    string gdir = Path.Combine(o.OutRoot, job.Folder);
                    GamePlan plan;
                    try { plan = Planner.Make(job, gdir, o, false); }
                    catch (Exception ex) { errors++; Log("     ERROR: " + ex.Message); continue; }
                    if (plan.Existing.Count > 0)
                        Log("     kept as they are: " + string.Join(", ", plan.Existing.Select(e => e.Name)));
                    if (plan.New.Any(n => n.OutName != null)) Directory.CreateDirectory(gdir);
                    foreach (var it in plan.New)
                    {
                        if (cancel) break;
                        if (it.Error != null) { errors++; Log("     ERROR " + it.Source.Rel + ": " + it.Error); continue; }
                        if (it.OutName == null) { skipped++; Log("     =  " + it.Source.Rel + ": " + it.Skip); continue; }
                        try
                        {
                            // CreateNew: never overwrite a file, even one that appeared in the meantime
                            using (var fs = new FileStream(Path.Combine(gdir, it.OutName), FileMode.CreateNew, FileAccess.Write))
                                fs.Write(it.Bytes, 0, it.Bytes.Length);
                            made++;
                            Log("     " + it.OutName.PadRight(10) + "<- " + it.Source.Rel);
                        }
                        catch (Exception ex) { errors++; Log("     ERROR writing " + it.OutName + ": " + ex.Message); }
                    }
                }

                Log("");
                if (cancel) Log("Cancelled.");
                if (o.Preview)
                    Log("Preview: " + games + " games with images, " + noImg + " without. No files written.");
                else
                    Log("Done: " + made + " BMP files written for " + games + " games, " + skipped + " not written (already present or no room), " +
                        noImg + " games without images, " + errors + " errors. No existing file was overwritten or deleted.");
                SetProgress(roms.Count, roms.Count, cancel ? "Cancelled" : "Done");
                SetJobs(list);
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                SetProgress(0, 1, "Error");
            }
            finally { SetBusy(false); }
        }
    }

    static class Program
    {
        public const string Version = "0.1.1";

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // Command line, like ws_bmp.py:  SwanShellImageBuilder.exe full|thumb input output.bmp
            if (args.Length == 3 && (args[0] == "full" || args[0] == "thumb"))
            {
                try
                {
                    bool full = args[0] == "full";
                    using (var src = WsBmp.Load(args[1]))
                        WsBmp.Convert(src, args[2], full ? WsBmp.FullW : WsBmp.ThumbW, full ? WsBmp.FullH : WsBmp.ThumbH, false, 0, true);
                    return 0;
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(args[2] + ".error.txt", ex.Message); } catch { }
                    return 1;
                }
            }

            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
            try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
