// Verify per-pixel click-through: send WM_NCHITTEST to the pet window with screen points.
//   csc /target:exe /out:nchittest.exe nchittest.cs
//   nchittest.exe <hwnd> <x1> <y1> [x2 y2 ...]   (physical screen pixels)
using System;
using System.Runtime.InteropServices;

class HitTest
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern int GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    [STAThread]
    static void Main(string[] args)
    {
        SetProcessDPIAware();
        IntPtr hwnd = new IntPtr(long.Parse(args[0]));
        RECT r; GetWindowRect(hwnd, out r);
        Console.WriteLine("window physical rect: " + r.L + "," + r.T + " - " + r.R + "," + r.B);
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            int x = int.Parse(args[i]);
            int y = int.Parse(args[i + 1]);
            long lp = ((long)(y & 0xFFFF) << 16) | (uint)(x & 0xFFFF);
            IntPtr result = SendMessage(hwnd, 0x0084, IntPtr.Zero, new IntPtr(lp));
            Console.WriteLine("point " + x + "," + y + " -> HT=" + result.ToInt64() + " (" + (result.ToInt64() == 1 ? "CLIENT" : result.ToInt64() == -1 ? "TRANSPARENT" : "other") + ")");
        }
    }
}
