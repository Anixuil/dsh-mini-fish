// DPI-aware full-desktop screenshot helper used to verify the desktop pet window.
//   csc /target:winexe /out:screenshot.exe screenshot.cs /r:System.Drawing.dll
//   screenshot.exe <out.png> [x y w h]
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

class Shot
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int n);
    [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);

    [STAThread]
    static void Main(string[] args)
    {
        // -4 = per-monitor v2, -2 = per-monitor; fall back to system aware
        try { SetProcessDpiAwareness(2); } catch { }
        try { SetProcessDPIAware(); } catch { }

        string path = args.Length > 0 ? args[0] : "shot.png";
        int x = 0, y = 0;
        int w = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
        int h = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
        int sx = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
        int sy = GetSystemMetrics(77); // SM_YVIRTUALSCREEN
        if (args.Length >= 5)
        {
            x = int.Parse(args[1]); y = int.Parse(args[2]);
            w = int.Parse(args[3]); h = int.Parse(args[4]);
        }
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(sx + x, sy + y, 0, 0, new Size(w, h));
            bmp.Save(path, ImageFormat.Png);
        }
        Console.WriteLine("screen=" + GetSystemMetrics(0) + "x" + GetSystemMetrics(1) + " virtual=" + w + "x" + h + " saved=" + path);
    }
}
