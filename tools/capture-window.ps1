$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class Cap {
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 public static bool Shoot(IntPtr hwnd, int w, int h, string path) {
   using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb)) {
     using (var g = Graphics.FromImage(bmp)) {
       IntPtr hdc = g.GetHdc();
       bool ok = PrintWindow(hwnd, hdc, 2);
       g.ReleaseHdc(hdc);
       bmp.Save(path, ImageFormat.Png);
       return ok;
     }
   }
 }
}
'@ -ReferencedAssemblies System.Drawing
$hwnd = [IntPtr]::new([long]$args[0])
$w = [int]$args[1]
$h = [int]$args[2]
$path = $args[3]
[Cap]::Shoot($hwnd, $w, $h, $path) | Out-Null
"captured $path"
