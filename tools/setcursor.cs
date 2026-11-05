// Get or set the cursor in physical screen pixels (DPI-aware helper for QA).
//   setcursor.exe            -> prints "x y"
//   setcursor.exe <x> <y>    -> moves the cursor
using System;
using System.Runtime.InteropServices;

class CursorTool
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X; public int Y; }

    [STAThread]
    static void Main(string[] args)
    {
        try { SetProcessDPIAware(); } catch { }
        if (args.Length >= 2)
        {
            SetCursorPos(int.Parse(args[0]), int.Parse(args[1]));
        }
        POINT p;
        if (GetCursorPos(out p)) Console.WriteLine(p.X + " " + p.Y);
    }
}
