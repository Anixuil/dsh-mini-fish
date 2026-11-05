// QA helper: drive a real left-button drag with the mouse (physical screen pixels).
//   mouse-drag.exe <x1> <y1> <x2> <y2> [steps] [holdMs]
//   mouse-drag.exe click <x> <y>
using System;
using System.Runtime.InteropServices;
using System.Threading;

class MouseDrag
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

    const uint LEFTDOWN = 0x0002;
    const uint LEFTUP = 0x0004;

    [STAThread]
    static void Main(string[] args)
    {
        try { SetProcessDPIAware(); } catch { }
        try
        {
            if (args.Length >= 3 && args[0] == "click")
            {
                SetCursorPos(int.Parse(args[1]), int.Parse(args[2]));
                Thread.Sleep(150);
                mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                Thread.Sleep(90);
                mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
                Console.WriteLine("clicked " + args[1] + " " + args[2]);
                return;
            }
            int x1 = int.Parse(args[0]), y1 = int.Parse(args[1]);
            int x2 = int.Parse(args[2]), y2 = int.Parse(args[3]);
            int steps = args.Length > 4 ? int.Parse(args[4]) : 24;
            int holdMs = args.Length > 5 ? int.Parse(args[5]) : 220;
            SetCursorPos(x1, y1);
            Thread.Sleep(180);
            mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            Thread.Sleep(120);
            for (int i = 1; i <= steps; i++)
            {
                int x = x1 + (x2 - x1) * i / steps;
                int y = y1 + (y2 - y1) * i / steps;
                SetCursorPos(x, y);
                Thread.Sleep(18);
            }
            Thread.Sleep(holdMs);
            mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
            Console.WriteLine("dragged " + x1 + "," + y1 + " -> " + x2 + "," + y2);
        }
        catch (Exception ex)
        {
            Console.WriteLine("error: " + ex.Message);
        }
    }
}
