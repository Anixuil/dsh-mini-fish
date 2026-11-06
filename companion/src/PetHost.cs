// DSH Pet Host — transparent, frameless, always-on-top desktop companion.
// M2 runtime: layered rig groups, spring-driven secondary motion, blink/mouth variants,
// signal-driven state machine (DSH agent events), simple canvas effects.
//
// Build: pwsh tools/build-companion.ps1
// Run:   PetHost.exe --pet <pack> --state <state.json> [--signals <signals.jsonl>] [--parent-pid N]

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Web.Script.Serialization;

namespace DshPetHost
{
    // ---------------------------------------------------------------- options

    internal sealed class Options
    {
        public string PetDir;
        public string StateFile;
        public string SignalsFile;
        public string Diag;
        public int ParentPid;
        public double Scale = 1.0;
        public bool ClickThrough;
        public bool NoSleep;
        public bool WindowAware = true;
        public double WalkIdleSeconds = 60;
        public string BalanceFile;
        public string DumpFrame;
        public string HideLayers;   // QA: comma separated layer ids to collapse
        public int ReloadDecode;    // QA: re-decode every layer at this width after load
        public string QaMode;       // QA: force a state before an offscreen dump (walk/drag/poke)
        public bool QaInstance;     // QA: allow a second host instance (perf measurement)
        public string QaTrace;      // QA: append a foreground/perch trace line every 0.5 s
        public string QaFgFile;     // QA: simulate the foreground window from a rect file
        public string QaCursor;     // QA: pin the cursor position "x,y" (physical pixels)
        public int DumpDelayMs = 400;   // QA: how long to let the scene settle before dumping

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string value = null;
                switch (a)
                {
                    case "--pet":
                    case "--state":
                    case "--scale":
                    case "--diag":
                    case "--signals":
                    case "--parent-pid":
                    case "--balance":
                    case "--walk-idle":
                    case "--hide-layers":
                    case "--reload-decode":
                    case "--qa-mode":
                    case "--dump-delay":
                    case "--qa-trace":
                    case "--qa-fg-file":
                    case "--qa-cursor":
                    case "--dump-frame":
                        if (i + 1 >= args.Length) throw new ArgumentException("missing value for " + a);
                        value = args[++i];
                        break;
                }
                switch (a)
                {
                    case "--pet": o.PetDir = value; break;
                    case "--state": o.StateFile = value; break;
                    case "--scale": o.Scale = double.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--diag": o.Diag = value; break;
                    case "--signals": o.SignalsFile = value; break;
                    case "--parent-pid": o.ParentPid = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--balance": o.BalanceFile = value; break;
                    case "--walk-idle": o.WalkIdleSeconds = double.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--dump-frame": o.DumpFrame = value; break;
                    case "--hide-layers": o.HideLayers = value; break;
                    case "--reload-decode": o.ReloadDecode = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--qa-mode": o.QaMode = value; break;
                    case "--qa-trace": o.QaTrace = value; break;
                    case "--qa-fg-file": o.QaFgFile = value; break;
                    case "--qa-cursor": o.QaCursor = value; break;
                    case "--dump-delay": o.DumpDelayMs = int.Parse(value, CultureInfo.InvariantCulture); break;                    case "--click-through": o.ClickThrough = true; break;
                    case "--qa-instance": o.QaInstance = true; break;
                    case "--no-sleep": o.NoSleep = true; break;
                    case "--window-aware": o.WindowAware = true; break;
                    case "--no-window-aware": o.WindowAware = false; break;
                    default: throw new ArgumentException("unknown argument: " + a);
                }
            }
            if (string.IsNullOrEmpty(o.PetDir)) throw new ArgumentException("--pet is required");
            if (string.IsNullOrEmpty(o.StateFile))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                o.StateFile = Path.Combine(appData, "dsh-pet", "state.json");
            }
            return o;
        }
    }

    // ---------------------------------------------------------------- json

    internal static class Json
    {
        public static Dictionary<string, object> Load(string path, int maxBytes)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = maxBytes };
            object parsed = ser.DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
            var dict = parsed as Dictionary<string, object>;
            if (dict == null) throw new InvalidDataException("expected a JSON object: " + path);
            return dict;
        }

        public static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }

        public static double Num(Dictionary<string, object> d, string k, double fallback)
        {
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return fallback;
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        public static bool Bool(Dictionary<string, object> d, string k, bool fallback)
        {
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return fallback;
            return Convert.ToBoolean(v, CultureInfo.InvariantCulture);
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }

        public static double[] Arr(Dictionary<string, object> d, string k, double[] fallback)
        {
            object v;
            if (!d.TryGetValue(k, out v) || !(v is object[])) return fallback;
            var arr = (object[])v;
            if (arr.Length != 2) return fallback;
            return new[] { Convert.ToDouble(arr[0], CultureInfo.InvariantCulture), Convert.ToDouble(arr[1], CultureInfo.InvariantCulture) };
        }
    }

    // ---------------------------------------------------------------- manifest

    internal sealed class CanvasSpec
    {
        public double Width = 300;
        public double Height = 400;
        public double BaseScale = 1.0;
        public double[] Anchor = { 0.5, 1.0 };
    }

    internal sealed class IdleSpec
    {
        public double Breath = 0.012;
        public double Sway = 0.8;
        public double Bob = 2.5;
        public double Speed = 1.0;
    }

    internal sealed class GazeSpec
    {
        // iris centres in canvas-normalized coordinates (pair per eye)
        public double[][] Centers = new double[][] { new[] { 0.385, 0.364 }, new[] { 0.580, 0.365 } };
        public double Radius = 0.055;   // falloff radius, canvas width fraction
        public double Shift = 0.016;    // max iris displacement, canvas width fraction
    }

    internal sealed class LayerSpec
    {
        public string Id = "";
        public string Image = "";
        public double Z;
        public double[] Pivot = { 0.5, 1.0 };
        public string Spring = "";
        public string Parent = "";
        public bool Hidden;
        public bool Retired;
        public Dictionary<string, string> Variants = new Dictionary<string, string>();
    }

    internal sealed class EyeStateSpec
    {
        public string Static = "";
        public string Mask = "";
        public string Lid = "";
    }

    internal sealed class EyeMovingSpec
    {
        public string Image = "";
    }

    internal sealed class EyeRigSpec
    {
        public double LimitX = 12;
        public double LimitY = 6;
        public Dictionary<string, EyeStateSpec> States = new Dictionary<string, EyeStateSpec>();
        public EyeMovingSpec Moving = new EyeMovingSpec();
        public Dictionary<string, string> Pupils = new Dictionary<string, string>();
    }

    internal sealed class PoseSpec
    {
        public string Id = "";
        public string Image = "";
        public double LedgeY;   // canvas y of the ledge the pose sits on / peeks over
    }

    internal sealed class PetManifest
    {
        public string Spec = "";
        public string Id = "";
        public Dictionary<string, object> Name = new Dictionary<string, object>();
        public CanvasSpec Canvas = new CanvasSpec();
        public IdleSpec Idle = new IdleSpec();
        public GazeSpec Gaze = new GazeSpec();
        public List<LayerSpec> Layers = new List<LayerSpec>();
        public EyeRigSpec EyesRig;
        public Dictionary<string, PoseSpec> Poses = new Dictionary<string, PoseSpec>();

        public static PetManifest Load(string packDir)
        {
            string path = Path.Combine(packDir, "pet.json");
            if (!File.Exists(path)) throw new FileNotFoundException("pet.json not found: " + path);
            var root = Json.Load(path, 8 * 1024 * 1024);
            if (root == null) throw new InvalidDataException("pet.json is not an object");

            var m = new PetManifest();
            m.Spec = Json.Str(root, "spec");
            m.Id = Json.Str(root, "id");
            if (!string.IsNullOrEmpty(m.Spec) && m.Spec != "dsh-pet/1")
                throw new InvalidDataException("unsupported spec: " + m.Spec);

            object nameObj;
            if (root.TryGetValue("name", out nameObj))
            {
                var nameDict = nameObj as Dictionary<string, object>;
                if (nameDict != null) m.Name = nameDict;
                else if (nameObj is string) m.Name["default"] = (string)nameObj;
            }

            var canvas = Json.Obj(root, "canvas");
            if (canvas != null)
            {
                m.Canvas.Width = Json.Num(canvas, "width", m.Canvas.Width);
                m.Canvas.Height = Json.Num(canvas, "height", m.Canvas.Height);
                m.Canvas.BaseScale = Json.Num(canvas, "baseScale", 1.0);
                m.Canvas.Anchor = Json.Arr(canvas, "anchor", new[] { 0.5, 1.0 });
            }
            var idle = Json.Obj(root, "idle");
            if (idle != null)
            {
                m.Idle.Breath = Json.Num(idle, "breath", m.Idle.Breath);
                m.Idle.Sway = Json.Num(idle, "sway", m.Idle.Sway);
                m.Idle.Bob = Json.Num(idle, "bob", m.Idle.Bob);
                m.Idle.Speed = Json.Num(idle, "speed", m.Idle.Speed);
            }

            var gaze = Json.Obj(root, "gaze");
            if (gaze != null)
            {
                m.Gaze.Radius = Json.Num(gaze, "radius", m.Gaze.Radius);
                m.Gaze.Shift = Json.Num(gaze, "shift", m.Gaze.Shift);
                object centersObj;
                if (gaze.TryGetValue("centers", out centersObj) && centersObj is object[])
                {
                    var list = new List<double[]>();
                    foreach (var item in (object[])centersObj)
                    {
                        var pair = item as object[];
                        if (pair == null || pair.Length != 2) continue;
                        list.Add(new[] { Convert.ToDouble(pair[0], CultureInfo.InvariantCulture), Convert.ToDouble(pair[1], CultureInfo.InvariantCulture) });
                    }
                    if (list.Count > 0) m.Gaze.Centers = list.ToArray();
                }
            }

            var rig = Json.Obj(root, "eyesRig");
            if (rig != null)
            {
                var spec = new EyeRigSpec();
                object limitObj;
                if (rig.TryGetValue("limit", out limitObj) && limitObj is object[])
                {
                    var lim = (object[])limitObj;
                    if (lim.Length == 2)
                    {
                        spec.LimitX = Convert.ToDouble(lim[0], CultureInfo.InvariantCulture);
                        spec.LimitY = Convert.ToDouble(lim[1], CultureInfo.InvariantCulture);
                    }
                }
                var states = Json.Obj(rig, "states");
                if (states != null)
                {
                    foreach (var kv in states)
                    {
                        var sd = kv.Value as Dictionary<string, object>;
                        if (sd == null) continue;
                        spec.States[kv.Key] = new EyeStateSpec
                        {
                            Static = Json.Str(sd, "static"),
                            Mask = Json.Str(sd, "mask"),
                            Lid = Json.Str(sd, "lid"),
                        };
                    }
                }
                var moving = Json.Obj(rig, "moving");
                if (moving != null)
                {
                    spec.Moving = new EyeMovingSpec
                    {
                        Image = Json.Str(moving, "image"),
                    };
                }
                var pupils = Json.Obj(rig, "pupils");
                if (pupils != null)
                {
                    foreach (var kv in pupils)
                        spec.Pupils[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? "";
                }
                if (spec.States.Count > 0) m.EyesRig = spec;
            }

            object layersObj;
            if (root.TryGetValue("layers", out layersObj) && layersObj is object[])
            {
                foreach (var item in (object[])layersObj)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    var spec = new LayerSpec
                    {
                        Id = Json.Str(d, "id"),
                        Image = Json.Str(d, "image"),
                        Z = Json.Num(d, "z", 0),
                        Pivot = Json.Arr(d, "pivot", new[] { 0.5, 1.0 }),
                        Spring = Json.Str(d, "spring"),
                        Hidden = Json.Bool(d, "hidden", false),
                        Retired = Json.Bool(d, "retired", false),
                        Parent = Json.Str(d, "parent"),
                    };
                    var variants = Json.Obj(d, "variants");
                    if (variants != null)
                    {
                        foreach (var kv in variants)
                        {
                            spec.Variants[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                        }
                    }
                    m.Layers.Add(spec);
                }
            }
            if (m.Layers.Count == 0) m.Layers.Add(new LayerSpec { Id = "body", Image = "parts/body.png" });
            m.Layers.Sort((a, b) => a.Z.CompareTo(b.Z));

            // full-body poses (sit on a window ledge / peek over the screen edge)
            var poses = Json.Obj(root, "poses");
            if (poses != null)
            {
                foreach (var kv in poses)
                {
                    var pd = kv.Value as Dictionary<string, object>;
                    if (pd == null) continue;
                    var pose = new PoseSpec
                    {
                        Id = kv.Key,
                        Image = Json.Str(pd, "image"),
                        LedgeY = Json.Num(pd, "ledgeY", 0),
                    };
                    if (!string.IsNullOrEmpty(pose.Image)) m.Poses[kv.Key] = pose;
                }
            }

            string rootFull = Path.GetFullPath(packDir) + Path.DirectorySeparatorChar;
            foreach (var l in m.Layers)
            {
                if (string.IsNullOrEmpty(l.Image)) throw new InvalidDataException("layer without image");
                l.Image = Resolve(packDir, rootFull, l.Image);
                var resolved = new Dictionary<string, string>();
                foreach (var kv in l.Variants)
                {
                    resolved[kv.Key] = Resolve(packDir, rootFull, kv.Value);
                }
                l.Variants = resolved;
            }
            foreach (var pose in m.Poses.Values)
                pose.Image = Resolve(packDir, rootFull, pose.Image);
            return m;
        }

        static string Resolve(string packDir, string rootFull, string rel)
        {
            string full = Path.GetFullPath(Path.Combine(packDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("layer escapes the pack: " + rel);
            return full;
        }
    }

    // ---------------------------------------------------------------- layers

    internal sealed class PetLayer
    {
        public LayerSpec Spec;
        public Image Visual;
        public BitmapSource Bitmap;
        public Dictionary<string, BitmapSource> Variants = new Dictionary<string, BitmapSource>();
        readonly Dictionary<string, string> _variantPaths = new Dictionary<string, string>();
        readonly Dictionary<string, byte[]> _bgraCache = new Dictionary<string, byte[]>();
        public byte[] Bgra;
        public int Stride;
        public Matrix Matrix = Matrix.Identity;
        public MatrixTransform Transform;
        public Point Offset;
        public string CurrentVariant = "";
        public bool ManagedSource;
        bool _loaded;

        // Every layer is a full 1024x1328 canvas. Decoding all of them at the source
        // resolution costs ~4x the memory of a half-resolution decode, so the host
        // decodes at the size the pet is actually drawn at. Decoding *below* the
        // on-screen size is what produced the pale blocky patches in the hair: thin
        // highlight strokes get resampled to half resolution and then drawn back up as
        // hard-edged rectangles.
        public static int DecodeWidth = 512;

        public static PetLayer Load(LayerSpec spec, string packDir)
        {
            var layer = new PetLayer { Spec = spec };
            layer.Transform = new MatrixTransform(Matrix.Identity);
            layer.Visual = new Image
            {
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = false,
                RenderTransformOrigin = new Point(0, 0),
                RenderTransform = layer.Transform,
            };
            RenderOptions.SetBitmapScalingMode(layer.Visual, BitmapScalingMode.LowQuality);
            layer.CurrentVariant = spec.Variants.ContainsKey("open") ? "open" : "";
            if (spec.Retired)
            {
                // Kept only as a hit-test placeholder (the old eyes layer): never decode.
                layer.Visual.Visibility = Visibility.Collapsed;
                return layer;
            }
            foreach (var kv in spec.Variants) layer._variantPaths[kv.Key] = kv.Value;
            if (spec.Hidden)
            {
                // Alternative/action layers (gait frames, raised arms, ...) are decoded
                // the first time the runtime actually shows them.
                layer.Visual.Visibility = Visibility.Collapsed;
                return layer;
            }
            layer.EnsureLoaded();
            return layer;
        }

        /// <summary>Decode the layer image if it is not decoded yet (hidden layers are lazy).</summary>
        public void EnsureLoaded()
        {
            if (_loaded || Spec.Retired) return;
            _loaded = true;
            Bitmap = LoadBitmap(Spec.Image);
            if (!ManagedSource) Visual.Source = Bitmap;
            Bgra = ToBgra(Bitmap, out Stride);
            _bgraCache[""] = Bgra;
            if (!string.IsNullOrEmpty(CurrentVariant)) SetVariant(CurrentVariant, true);
        }

        public static BitmapSource LoadBitmap(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("missing layer image: " + path);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = DecodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        static byte[] ToBgra(BitmapSource bmp, out int stride)
        {
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        /// <summary>Re-decode at the current DecodeWidth (used when the pet is scaled up).</summary>
        public void Reload()
        {
            if (!_loaded) return;
            Bitmap = LoadBitmap(Spec.Image);
            if (!ManagedSource) Visual.Source = Bitmap;
            Bgra = ToBgra(Bitmap, out Stride);
            _bgraCache.Clear();
            _bgraCache[""] = Bgra;
            Variants.Clear();
            string current = CurrentVariant;
            CurrentVariant = "";
            if (!string.IsNullOrEmpty(current) && _variantPaths.ContainsKey(current)) SetVariant(current);
        }

        public void SetVariant(string name)
        {
            SetVariant(name, false);
        }

        public void SetVariant(string name, bool force)
        {
            if (Spec.Retired) return;
            if (string.IsNullOrEmpty(name)) return;
            EnsureLoaded();
            if (!force && name == CurrentVariant) return;
            BitmapSource bmp;
            if (!Variants.TryGetValue(name, out bmp))
            {
                string path;
                if (!_variantPaths.TryGetValue(name, out path)) return;
                // variants are decoded on first use: a layer can have a dozen of them and
                // most sessions only ever show two or three
                bmp = LoadBitmap(path);
                Variants[name] = bmp;
            }
            CurrentVariant = name;
            if (!ManagedSource) Visual.Source = bmp;
            Bitmap = bmp;
            byte[] cached;
            if (_bgraCache.TryGetValue(name, out cached))
            {
                Bgra = cached;
            }
            else
            {
                int stride;
                Bgra = ToBgra(bmp, out stride);
                _bgraCache[name] = Bgra;
            }
            Stride = bmp.PixelWidth * 4;
        }

        public bool HitTest(Point windowPoint, double threshold)
        {
            if (Bitmap == null || Bgra == null) return false;
            Point localPoint = new Point(windowPoint.X - Offset.X, windowPoint.Y - Offset.Y);
            Matrix inv = Matrix;
            if (!inv.HasInverse) return false;
            inv.Invert();
            Point p = inv.Transform(localPoint);
            double px = p.X * Bitmap.PixelWidth / Math.Max(1.0, Visual.Width);
            double py = p.Y * Bitmap.PixelHeight / Math.Max(1.0, Visual.Height);
            int ix = (int)Math.Floor(px);
            int iy = (int)Math.Floor(py);
            if (ix < 0 || iy < 0 || ix >= Bitmap.PixelWidth || iy >= Bitmap.PixelHeight) return false;
            return Bgra[iy * Stride + ix * 4 + 3] > threshold;
        }
    }

    // ---------------------------------------------------------------- eye rig

    internal sealed class EyeRig
    {
        sealed class Side
        {
            public string Name;
            public Image Static;
            public Image Moving;
            public EllipseGeometry Clip = new EllipseGeometry();
            public BitmapSource Mask;
            public TranslateTransform Translate;
        }

        readonly string _packDir;
        readonly EyeRigSpec _spec;
        readonly Dictionary<string, BitmapSource> _cache = new Dictionary<string, BitmapSource>();
        readonly List<Side> _sides = new List<Side>();
        public readonly Canvas Root = new Canvas();
        double _artW = 300, _artH = 400;
        string _state = "";
        string _pupil = "";
        double _gazeX, _gazeY, _facing = 1;

        public EyeRig(string packDir, EyeRigSpec spec)
        {
            _packDir = packDir;
            _spec = spec;
            Root.IsHitTestVisible = false;
            foreach (var name in new[] { "l", "r" })
            {
                var side = new Side { Name = name };
                side.Static = MakeImage();
                side.Moving = MakeImage();
                side.Translate = new TranslateTransform();
                side.Moving.RenderTransform = side.Translate;
                side.Moving.Clip = side.Clip;
                side.Moving.Visibility = Visibility.Collapsed;
                Root.Children.Add(side.Static);
                Root.Children.Add(side.Moving);
                _sides.Add(side);
            }
        }

        static Image MakeImage()
        {
            return new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        }

        string Resolve(string pattern, string side, string suffix)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            string rel = pattern;
            int i = rel.IndexOf("%s");
            if (i >= 0) rel = rel.Substring(0, i) + side + rel.Substring(i + 2);
            i = rel.IndexOf("%s");
            if (i >= 0) rel = rel.Substring(0, i) + suffix + rel.Substring(i + 2);
            return Path.Combine(_packDir, rel);
        }

        BitmapSource Load(string pattern, string side, string suffix = "")
        {
            string full = Resolve(pattern, side, suffix);
            if (full == null) return null;
            BitmapSource bmp;
            if (_cache.TryGetValue(full, out bmp)) return bmp;
            if (!File.Exists(full)) return null;
            bmp = PetLayer.LoadBitmap(full);
            _cache[full] = bmp;
            return bmp;
        }

        public void Layout(double artW, double artH)
        {
            _artW = artW;
            _artH = artH;
            Root.Width = artW;
            Root.Height = artH;
            foreach (var side in _sides)
            {
                side.Static.Width = artW;
                side.Static.Height = artH;
                side.Moving.Width = artW;
                side.Moving.Height = artH;
                UpdateClip(side);
            }
        }

        public void UpdateMatrix(Matrix matrix)
        {
            Root.RenderTransform = new MatrixTransform(matrix);
        }

        void UpdateClip(Side side)
        {
            var mask = side.Mask;
            if (mask == null) return;
            int w = mask.PixelWidth, h = mask.PixelHeight;
            var bgra = new byte[w * h * 4];
            var conv = new FormatConvertedBitmap(mask, PixelFormats.Bgra32, null, 0);
            conv.Freeze();
            conv.CopyPixels(bgra, w * 4, 0);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    if (bgra[row + x * 4 + 3] > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < minX) return;
            double sx = _artW / Math.Max(1, w), sy = _artH / Math.Max(1, h);
            side.Clip.Center = new Point((minX + maxX + 1) * 0.5 * sx, (minY + maxY + 1) * 0.5 * sy);
            side.Clip.RadiusX = (maxX - minX + 1) * 0.5 * sx;
            side.Clip.RadiusY = (maxY - minY + 1) * 0.5 * sy;
        }

        public void SetState(string state, bool force = false)
        {
            if (string.IsNullOrEmpty(state)) return;
            if (!force && state == _state) return;
            EyeStateSpec st;
            if (!_spec.States.TryGetValue(state, out st)) return;
            _state = state;
            foreach (var side in _sides)
            {
                side.Static.Source = Load(st.Static, side.Name);
                side.Static.Visibility = Visibility.Visible;
                bool hasMoving = !string.IsNullOrEmpty(st.Mask);
                if (hasMoving)
                {
                    side.Mask = Load(st.Mask, side.Name);
                    UpdateClip(side);
                    side.Moving.Visibility = Visibility.Visible;
                }
                else
                {
                    side.Moving.Visibility = Visibility.Collapsed;
                }
            }
        }

        public void SetPupil(string variant, bool force = false)
        {
            if (string.IsNullOrEmpty(variant)) return;
            if (!force && variant == _pupil) return;
            string suffix;
            if (!_spec.Pupils.TryGetValue(variant, out suffix)) return;
            _pupil = variant;
            foreach (var side in _sides)
                side.Moving.Source = Load(_spec.Moving.Image, side.Name, suffix);
        }

        /// <summary>Re-decode every cached eye image (used after a decode-width change).</summary>
        public void Reload()
        {
            _cache.Clear();
            string state = _state;
            string pupil = _pupil;
            _state = "";
            _pupil = "";
            if (!string.IsNullOrEmpty(state)) SetState(state, true);
            if (!string.IsNullOrEmpty(pupil)) SetPupil(pupil, true);
        }

        public void SetGaze(double nx, double ny, double facing)
        {
            _gazeX = nx;
            _gazeY = ny;
            _facing = facing;
            double dx = _gazeX * _spec.LimitX * (_artW / 1024.0) * _facing;
            double dy = _gazeY * _spec.LimitY * (_artH / 1328.0);
            foreach (var side in _sides)
            {
                side.Translate.X = dx;
                side.Translate.Y = dy;
            }
        }
    }

    // ---------------------------------------------------------------- spring

    internal sealed class SpringState
    {
        public double Angle;
        public double Velocity;
        public double Stiffness = 90;
        public double Damping = 11;
        public double Min = -30;
        public double Max = 30;

        public void Update(double dt, double target)
        {
            target = Math.Max(Min, Math.Min(Max, target));
            double accel = Stiffness * (target - Angle) - Damping * Velocity;
            Velocity += accel * dt;
            Angle += Velocity * dt;
        }
    }

    // ---------------------------------------------------------------- signals

    internal sealed class Signal
    {
        public string Type = "";
        public string Status = "";
        public string Text = "";
        public bool Ok = true;
        public double At;
        public int Count;
        public int DurationMs;

        public static Signal Parse(string line)
        {
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 };
                var d = ser.DeserializeObject(line) as Dictionary<string, object>;
                if (d == null) return null;
                var s = new Signal
                {
                    Type = Json.Str(d, "type"),
                    Status = Json.Str(d, "status"),
                    Text = Json.Str(d, "text"),
                    Ok = Json.Bool(d, "ok", true),
                    At = Json.Num(d, "at", 0),
                    Count = (int)Json.Num(d, "tools", 0),
                    DurationMs = (int)Json.Num(d, "durationMs", 0),
                };
                return string.IsNullOrEmpty(s.Type) ? null : s;
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class SignalReader
    {
        readonly string _path;
        long _offset;

        public SignalReader(string path)
        {
            _path = path;
            try { if (!string.IsNullOrEmpty(_path) && File.Exists(_path)) _offset = new FileInfo(_path).Length; } catch { }
        }

        public void Poll(Action<Signal> handler)
        {
            if (string.IsNullOrEmpty(_path)) return;
            var lines = new List<string>();
            try
            {
                if (!File.Exists(_path)) return;
                var info = new FileInfo(_path);
                if (info.Length < _offset) _offset = 0;
                if (info.Length == _offset) return;
                using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(_offset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.Trim().Length > 0) lines.Add(line);
                        }
                        _offset = fs.Position;
                    }
                }
            }
            catch { return; }

            foreach (var line in lines)
            {
                var signal = Signal.Parse(line);
                if (signal == null) continue;
                try
                {
                    handler(signal);
                }
                catch (Exception ex)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(_path);
                        File.AppendAllText(Path.Combine(dir, "signal-errors.log"),
                            DateTime.UtcNow.ToString("o") + " " + line + "\n" + ex + "\n\n", Encoding.UTF8);
                    }
                    catch { }
                }
            }
        }
    }

    // ---------------------------------------------------------------- state machine

    internal enum PetState
    {
        Idle,
        Think,
        Work,
        Celebrate,
        Sad,
        Sleep,
        Drag,
        Poke,
        Alert,
        Walk
    }

    // ---------------------------------------------------------------- state persistence

    internal sealed class PersistedState
    {
        public double Left = double.NaN;
        public double Top = double.NaN;
        public double Scale = 1.0;
        public bool ClickThrough;
        public bool Topmost = true;
        public string PetId = "";
        public bool BalanceVisible = true;
        public bool WindowAware = true;

        public static PersistedState Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new PersistedState();
                var d = Json.Load(path, 256 * 1024);
                var s = new PersistedState();
                s.Left = d.ContainsKey("left") ? Convert.ToDouble(d["left"], CultureInfo.InvariantCulture) : double.NaN;
                s.Top = d.ContainsKey("top") ? Convert.ToDouble(d["top"], CultureInfo.InvariantCulture) : double.NaN;
                s.Scale = d.ContainsKey("scale") ? Convert.ToDouble(d["scale"], CultureInfo.InvariantCulture) : 1.0;
                s.ClickThrough = d.ContainsKey("clickThrough") && Convert.ToBoolean(d["clickThrough"], CultureInfo.InvariantCulture);
                s.Topmost = !d.ContainsKey("topmost") || Convert.ToBoolean(d["topmost"], CultureInfo.InvariantCulture);
                s.PetId = d.ContainsKey("petId") ? Convert.ToString(d["petId"], CultureInfo.InvariantCulture) : "";
                s.BalanceVisible = !d.ContainsKey("balanceVisible") || Convert.ToBoolean(d["balanceVisible"], CultureInfo.InvariantCulture);
                s.WindowAware = !d.ContainsKey("windowAware") || Convert.ToBoolean(d["windowAware"], CultureInfo.InvariantCulture);
                return s;
            }
            catch
            {
                return new PersistedState();
            }
        }

        public void Save(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var d = new Dictionary<string, object>
                {
                    { "scale", Scale }, { "clickThrough", ClickThrough },
                    { "topmost", Topmost }, { "petId", PetId }, { "balanceVisible", BalanceVisible },
                    { "windowAware", WindowAware },
                };
                if (!double.IsNaN(Left) && !double.IsInfinity(Left)) d["left"] = Left;
                if (!double.IsNaN(Top) && !double.IsInfinity(Top)) d["top"] = Top;
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(d), Encoding.UTF8);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- particles

    internal sealed class Particle
    {
        public FrameworkElement Element;
        public double X, Y, Vx, Vy, Age, Life;
    }

    // ---------------------------------------------------------------- window

    internal sealed class PetWindow : Window
    {
        const int WM_NCHITTEST = 0x0084;
        const int WM_HOTKEY = 0x0312;
        const int HTTRANSPARENT = -1;
        const int HTCLIENT = 1;
        const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
        const uint VK_P = 0x50, VK_Q = 0x51;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        // z-order inspection: IsWindowVisible + GetWindow(GW_HWNDPREV) let us prove whether
        // any visible window really covers her, instead of trusting the WS_EX_TOPMOST bit
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        const uint GW_HWNDPREV = 3;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        // NOSIZE | NOMOVE keep her exactly where she is; NOACTIVATE keeps the pet from
        // stealing focus when we re-assert the z-order.
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        const uint SWP_REASSERT = SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE;
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        readonly Options _options;
        readonly PetManifest _manifest;
        readonly List<PetLayer> _layers = new List<PetLayer>();
        EyeRig _eyeRig;
        string _rigEyesState = "open";
        string _rigPupil = "normal";
        readonly Dictionary<string, PetLayer> _byId = new Dictionary<string, PetLayer>();
        readonly Dictionary<string, SpringState> _springs = new Dictionary<string, SpringState>();
        readonly Canvas _layerCanvas = new Canvas();
        readonly Canvas _fxCanvas = new Canvas();
        readonly Grid _root = new Grid();
        readonly PersistedState _state;
        readonly DispatcherTimer _saveTimer;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly SignalReader _signals;
        readonly List<Particle> _particles = new List<Particle>();
        readonly Random _random = new Random();

        HwndSource _source;
        bool _clickThrough;
        bool _topmost = true;
        bool _dragging;
        bool _moved;
        bool _userClosed;
        double _dragStartCursorX, _dragStartCursorY;
        double _dragFromLeft, _dragFromTop;
        double _lastClickTime = -1;
        double _lastClickX, _lastClickY;
        double _dpiScale = 1.0;
        double _drawnScale = 0.24;
        double _t;
        double _lastFrameTime;

        PetState _mode = PetState.Idle;
        double _stateUntil = -1;
        double _lastActivity;
        bool _talking;
        double _talkingUntil;
        double _nextBlinkAt = 3;
        double _blinkPhase = -1;
        double _blinkTimer;
        double _headTilt;
        double _tiltDrag;
        bool _spinning;
        double _spinStart;
        double _twirlX = 1.0;
        double _twirlSquash = 1.0;
        double _jumpOffset;
        double _blushOpacity = 0.55;
        string _facePreset = "neutral";
        string _eyesPreset = "open";
        readonly Dictionary<string, BitmapSource> _fxCache = new Dictionary<string, BitmapSource>();
        double _mouthPhase;
        double _lookX, _lookY;
        double _lastSignalPoll;
        bool _windowAware = true;
        bool _targetValid;
        Rect _targetRect;
        string _targetTitle = "";
        IntPtr _targetHwnd;
        IntPtr _lastTargetHwnd;
        bool _perchActive;
        double _perchCooldown;
        double _perchSettle;
        double _lastWindowPoll;
        double _lastInteraction;
        double _systemIdleSeconds;
        double _lastIdlePoll;
        double _lastForegroundChange;
        IntPtr _lastForegroundSeen;
        // Foreground state snapshot: a window only becomes a perch target after it has
        // held still for a while (see PollForegroundWindow), so she never chases a
        // window that is being moved, resized or just flashed in front.
        IntPtr _fgStateHwnd = IntPtr.Zero;
        Rect _fgStateRect;
        bool _fgStateHaveRect;
        bool _fgStateValid;
        bool _fgFullscreen;
        double _fgStateSince;
        // bottom edge of the fullscreen / maximized window's monitor (DIP): the peek pose
        // rests on the real visible edge, even when the taskbar is hidden behind it
        Rect _fgRestArea;
        bool _fgRestValid;
        // bottom-right corner rest used while a fullscreen / maximized window is in front
        bool _cornerRest;
        double _cornerSettle;
        bool _qaPinForeground;
        double _lastQaTrace = -100;
        // Topmost watchdog. A window can silently fall out of the topmost band without
        // WS_EX_TOPMOST changing: another app calling SetWindowPos(HWND_NOTOPMOST), a
        // shell / DWM transition, or a third-party window manager reshuffling the band.
        // Once that happens WPF never re-asserts Topmost on its own, so she stays behind
        // ordinary windows until the user restarts the host. We poll the real z-order and
        // put her back on top.
        double _lastZOrderCheck = -100;
        // Where the user dropped her by hand / what the foreground looked like back then
        double _dragReleasedAt = -1000;
        IntPtr _dragFgHwnd;
        Rect _dragFgRect;
        bool _dragFgHaveRect;
        bool _dragFgFullscreen;
        bool _agentBusy;
        double _sleepAfterSeconds = 300;
        double _walkIdleSeconds = 60;
        double _eyeX, _eyeY;
        double _dragVelX;
        double _prevDragLeft;
        double _walkTargetLeft;
        double _walkDir;
        double _walkPhase;
        double _nextWanderAt = 18;
        double _facing = 1.0;
        double _walkBob;
        double _walkTilt;
        double _nextBubbleAt;
        int _taskTools;
        PetLayer _eyeLayer;
        WriteableBitmap _eyeWarp;
        byte[] _eyeWarpPixels;
        float[] _eyeWarpWeights;
        int _warpX, _warpY, _warpW, _warpH, _warpStride;
        double _gazeNx, _gazeNy;
        int _cursorX = -1;
        int _cursorY = -1;
        double _lastCursorMoveAt = -100;
        string _warpVariant = "";
        double _warpGx, _warpGy;
        double _lastWarpUpdate;

        Border _bubble;
        TextBlock _bubbleText;
        double _bubbleUntil = -1;
        bool _bubbleFadingOut;
        Border _balanceBadge;
        TextBlock _balanceText;
        string _balanceDetails = "";
        double _lastBalancePoll = -100;
        double _balanceUntil = -1;
        bool _badgeFadingOut;
        double _lastHoverPeek = -100;

        // Text overlays are transient by design: the pet shows nothing while it just
        // idles. A bubble only appears when there is something to say, keeps the
        // newest line only (new speech replaces it and restarts the window) and is
        // gone about a minute after the latest line. The balance badge never stays
        // on screen either; it peeks in for a few seconds when you interact.
        //
        // There is also only ever ONE text box on screen at a time: a bubble hides
        // the balance badge while it is up, and hover peeks are skipped while she is
        // talking. Autonomous small talk (walking, step chatter) never refreshes an
        // existing bubble and keeps quiet for a while after any line, so the screen
        // falls silent again instead of showing a permanent box.
        const double BubbleHoldSeconds = 60.0;
        const double ReactionBubbleSeconds = 8.0;
        const double GreetingBubbleSeconds = 10.0;
        const double ChatterBubbleSeconds = 20.0;
        const double ChatterGapSeconds = 60.0;
        const double BalancePeekSeconds = 10.0;

        // The window keeps a transparent margin around the artwork. Swinging the tail,
        // tilting the body while dragging or hopping during a celebration would
        // otherwise be sliced off by the window edge (the "cut" seen in screenshots).
        const double PadXRatio = 0.16;
        const double PadTopRatio = 0.13;
        const double PadBottomRatio = 0.08;

        // Perch timing. A foreground window must hold still for PerchStableSeconds before
        // she walks over and sits on it: a window that is being dragged, resized or
        // alt-tabbed through is not furniture yet. A fullscreen (or maximized) window has
        // no ledge to sit on, so she rests low in the bottom-right corner instead of
        // climbing to the top of the screen.
        const double PerchStableSeconds = 5.0;
        const double CornerRestDelaySeconds = 1.2;
        const double CornerRestSettleSeconds = 0.9;
        // She only keeps an eye on the cursor while it is actually moving; after this many
        // seconds without a cursor movement the gaze drifts back to the neutral pose.
        const double GazeFollowSeconds = 1.5;
        // Carrying her somewhere by hand means "stay there": the automatic perch / corner
        // rules leave her alone for this long, unless the foreground window changes.
        const double DragHoldSeconds = 60.0;
        double _artW, _artH;
        double _padX, _padTop, _padBottom;

        public PetWindow(Options options)
        {
            _options = options;
            _manifest = PetManifest.Load(options.PetDir);
            _state = PersistedState.Load(options.StateFile);
            _state.PetId = _manifest.Id;
            _clickThrough = options.ClickThrough || _state.ClickThrough;
            _topmost = _state.Topmost;
            _windowAware = options.WindowAware && _state.WindowAware;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = _topmost;
            Title = "DSH Pet — " + DisplayName();
            Content = _root;

            _root.Children.Add(_layerCanvas);
            _root.Children.Add(_fxCanvas);
            _fxCanvas.IsHitTestVisible = false;

            double scale = _manifest.Canvas.BaseScale * (_state.Scale > 0 ? _state.Scale : 1.0) * options.Scale;
            // Decode the layer bitmaps at the size they will be drawn at: bigger wastes
            // memory, smaller re-samples thin hair highlights into blocky pale patches.
            PetLayer.DecodeWidth = WantedDecodeWidth(scale, DetectDpiScale());

            foreach (var spec in _manifest.Layers)
            {
                var layer = PetLayer.Load(spec, options.PetDir);
                _layerCanvas.Children.Add(layer.Visual);
                _layers.Add(layer);
                _byId[spec.Id] = layer;
                if (!string.IsNullOrEmpty(spec.Spring))
                {
                    var spring = new SpringState();
                    ConfigureSpring(spec.Spring, spring);
                    _springs[spec.Id] = spring;
                }
            }
            if (_manifest.EyesRig != null)
            {
                _eyeRig = new EyeRig(options.PetDir, _manifest.EyesRig);
                int insertAt = _layers.Count;
                for (int i = 0; i < _layers.Count; i++)
                {
                    if (_layers[i].Spec.Z > 110) { insertAt = i; break; }
                }
                _layerCanvas.Children.Insert(insertAt, _eyeRig.Root);
            }
            if (!string.IsNullOrEmpty(options.HideLayers))
            {
                foreach (var raw in options.HideLayers.Split(','))
                {
                    string key = raw.Trim();
                    if (key == "eyesrig")
                    {
                        if (_eyeRig != null) _eyeRig.Root.Visibility = Visibility.Collapsed;
                        continue;
                    }
                    PetLayer hiddenLayer;
                    if (_byId.TryGetValue(key, out hiddenLayer)) hiddenLayer.Visual.Visibility = Visibility.Collapsed;
                }
            }
            ApplyGeometry(scale);

            BuildBubble();
            BuildBalanceBadge();
            ApplyGeometry(scale); // now the overlays exist, give them the padded size too
            InitEyeWarp();
            _signals = new SignalReader(options.SignalsFile);

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); _state.Save(_options.StateFile); };

            PreviewMouseLeftButtonDown += OnLeftDown;
            PreviewMouseLeftButtonUp += OnLeftUp;
            MouseMove += OnMouseMove;
            PreviewMouseRightButtonUp += OnRightUp;
            MouseWheel += OnWheel;
            MouseEnter += (s, e) => ShowBalancePeek();
            CompositionTarget.Rendering += OnFrame;
            Closed += (s, e) =>
            {
                CompositionTarget.Rendering -= OnFrame;
                _state.Save(_options.StateFile);
                if (_source != null) _source.RemoveHook(WndProc);
                if (_userClosed) WriteClosedMarker();
            };
            Loaded += (s, e) =>
            {
                _lastActivity = 0;
                WriteDiag();
                if (_options.ParentPid > 0)
                {
                    var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    watchdog.Tick += (s2, e2) =>
                    {
                        try { Process.GetProcessById(_options.ParentPid); }
                        catch { watchdog.Stop(); Close(); }
                    };
                    watchdog.Start();
                }
            };

            _sleepAfterSeconds = options.NoSleep ? double.PositiveInfinity : 300;
            _walkIdleSeconds = options.WalkIdleSeconds;
            RestorePosition();
            ApplyClickThrough();
            if (!string.IsNullOrEmpty(options.BalanceFile)) RefreshBalance();
        }

        string DisplayName()
        {
            object zh;
            if (_manifest.Name.TryGetValue("zh", out zh) && zh != null) return Convert.ToString(zh);
            object en;
            if (_manifest.Name.TryGetValue("en", out en) && en != null) return Convert.ToString(en);
            return _manifest.Id;
        }

        void ConfigureSpring(string name, SpringState s)
        {
            switch (name)
            {
                case "tail": s.Stiffness = 55; s.Damping = 7.5; s.Min = -40; s.Max = 40; break;
                case "ear": s.Stiffness = 120; s.Damping = 9; s.Min = -25; s.Max = 25; break;
                case "hair": s.Stiffness = 70; s.Damping = 9; s.Min = -20; s.Max = 20; break;
                case "ahoge": s.Stiffness = 160; s.Damping = 8; s.Min = -30; s.Max = 30; break;
                case "skirt": s.Stiffness = 90; s.Damping = 11; s.Min = -12; s.Max = 12; break;
                case "arm": s.Stiffness = 110; s.Damping = 10; s.Min = -60; s.Max = 60; break;
                default: s.Stiffness = 90; s.Damping = 10; break;
            }
        }

        // ------------------------------------------------------------ frame

        void OnFrame(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = _lastFrameTime <= 0 ? 0.016 : Math.Min(0.05, now - _lastFrameTime);
            _lastFrameTime = now;
            _t += dt;

            if (_signals != null && _t - _lastSignalPoll > 0.15)
            {
                _lastSignalPoll = _t;
                _signals.Poll(HandleSignal);
            }
            if (_t - _lastIdlePoll > 0.5)
            {
                _lastIdlePoll = _t;
                _systemIdleSeconds = GetSystemIdleSeconds();
            }
            if (_t - _lastWindowPoll > 0.2)
            {
                _lastWindowPoll = _t;
                PollForegroundWindow();
            }
            // Cheap, throttled self-heal: something may have knocked her out of the
            // topmost band (see EnsureTopmost). 2 Hz is fast enough to look instant and
            // cheap enough to leave in the 60 fps loop.
            if (_t - _lastZOrderCheck > 0.5)
            {
                _lastZOrderCheck = _t;
                EnsureTopmost();
            }
            UpdateState(dt);
            UpdateBlink(dt);
            UpdateEyeWarp(false);
            UpdatePerch(dt);
            QaTrace();
            if (!string.IsNullOrEmpty(_options.BalanceFile) && _t - _lastBalancePoll > 15)
            {
                _lastBalancePoll = _t;
                RefreshBalance();
            }

            _walkBob = 0;
            _walkTilt = 0;
            if (_mode == PetState.Walk) UpdateWalk(dt);
            else if (!_perchActive) _facing = 1.0;
            UpdatePose();
            UpdateGait();

            double breathRate = 3.6;
            double breath = _manifest.Idle.Breath * Math.Sin(_t * 2 * Math.PI / breathRate);
            double sway = _manifest.Idle.Sway * Math.Sin(_t * 2 * Math.PI / 5.3 + 0.6);
            double bob = _manifest.Idle.Bob * Math.Sin(_t * 2 * Math.PI / 2.9 + 1.2);
            double swayScale = 1.0;
            double breathScale = 1.0;

            switch (_mode)
            {
                case PetState.Think: breathRate = 4.6; breathScale = 0.8; swayScale = 0.6; break;
                case PetState.Work: breathRate = 2.6; breathScale = 1.5; swayScale = 1.4; break;
                case PetState.Sad: breathRate = 5.2; breathScale = 0.7; swayScale = 0.5; break;
                case PetState.Sleep: breathRate = 6.5; breathScale = 0.6; swayScale = 0.2; break;
                case PetState.Drag: breathScale = 0.4; swayScale = 0.4; break;
                case PetState.Celebrate: breathScale = 1.3; swayScale = 1.2; break;
                case PetState.Poke: breathScale = 1.2; swayScale = 0.8; break;
                case PetState.Alert: breathScale = 0.9; swayScale = 0.7; break;
            }
            breath *= breathScale;
            sway *= swayScale;

            // "twirl" reaction: one clean 0 -> flip -> 0 turn instead of a decaying angle
            double twirlHop = 0;
            _twirlX = 1.0;
            _twirlSquash = 1.0;
            if (_spinning)
            {
                double progress = (_t - _spinStart) / 0.85;
                if (progress >= 1 || progress < 0)
                {
                    _spinning = false;
                }
                else
                {
                    _twirlX = Math.Cos(2 * Math.PI * progress);
                    if (Math.Abs(_twirlX) < 0.03) _twirlX = _twirlX < 0 ? -0.03 : 0.03;
                    _twirlSquash = 1 + 0.07 * Math.Sin(2 * Math.PI * progress);
                    twirlHop = -14 * Math.Sin(Math.PI * progress);
                }
            }
            if (_mode == PetState.Celebrate) bob -= 26 * Math.Abs(Math.Sin(_t * 9.0));
            bob += _jumpOffset + twirlHop + _walkBob;
            _jumpOffset *= 0.88;

            _headTilt += (HeadTiltTarget() - _headTilt) * Math.Min(1, dt * 8);

            double headMotion = sway + _headTilt;
            foreach (var kv in _springs)
            {
                string id = kv.Key;
                var spring = kv.Value;
                double target = 0;
                // segmented chains: each child lags a little behind its parent, so the
                // motion travels down the tail / hair / arm instead of moving rigidly.
                if (id == "tail_base") target = -sway * 1.10 + Math.Sin(_t * 1.35) * 2.6;
                else if (id == "tail_mid") target = -sway * 1.15 + Math.Sin(_t * 1.35 - 0.5) * 2.2;
                else if (id == "tail_tip") target = -sway * 1.20 + Math.Sin(_t * 1.35 - 1.0) * 1.8;
                else if (id == "ear_l") target = sway * 0.9 + Math.Sin(_t * 0.85) * 1.2;
                else if (id == "ear_r") target = sway * 0.9 + Math.Sin(_t * 0.85 + 1.7) * 1.2;
                else if (id == "hair_side_l_upper") target = -headMotion * 0.85 + Math.Sin(_t * 0.95) * 1.1;
                else if (id == "hair_side_l_lower") target = -headMotion * 0.95 + Math.Sin(_t * 0.95 - 0.6) * 0.9;
                else if (id == "hair_side_r_upper") target = -headMotion * 0.85 + Math.Sin(_t * 0.95 + 1.4) * 1.1;
                else if (id == "hair_side_r_lower") target = -headMotion * 0.95 + Math.Sin(_t * 0.95 + 0.8) * 0.9;
                else if (id == "hair_front") target = -headMotion * 0.5 + Math.Sin(_t * 1.15) * 0.8;
                else if (id == "hair_back_upper") target = -headMotion * 0.35 + Math.Sin(_t * 1.05) * 0.7;
                else if (id == "hair_back_lower") target = -headMotion * 0.45 + Math.Sin(_t * 1.05 - 0.6) * 0.6;
                else if (id == "hair_back_action") target = -headMotion * 0.35 + Math.Sin(_t * 1.05) * 0.7;
                else if (id == "ahoge") target = -headMotion * 1.6 + Math.Sin(_t * 2.3) * 2.6;
                else if (id == "skirt") target = sway * 0.55 + Math.Sin(_t * 1.45) * 0.9;
                else if (id == "apron") target = sway * 0.45 + Math.Sin(_t * 1.45 + 0.8) * 0.7;
                else if (id == "upper_arm_l") target = sway * 0.25;
                else if (id == "forearm_l") target = sway * 0.32;
                else if (id == "hand_l") target = sway * 0.38;
                else if (id == "upper_arm_r") target = sway * 0.25;
                else if (id == "forearm_r") target = sway * 0.32;
                else if (id == "hand_r") target = sway * 0.38;

                if (_mode == PetState.Celebrate && id == "upper_arm_r") target += Math.Sin(_t * 10) * 14 - 12;
                if (_mode == PetState.Celebrate && id == "forearm_r") target += Math.Sin(_t * 10 - 0.4) * 8 - 6;
                if (_mode == PetState.Sleep && (id == "ear_l" || id == "ear_r")) target += 8;
                if (_mode == PetState.Sad && (id == "ear_l" || id == "ear_r")) target += 14;
                if (_mode == PetState.Drag)
                {
                    double dragLean = Clamp(_dragVelX * 0.3, -32, 32);
                    if (id == "upper_arm_l") target = -34 - dragLean;
                    else if (id == "upper_arm_r") target = 34 - dragLean;
                    else if (id == "forearm_l") target = 10 - dragLean * 0.3;
                    else if (id == "forearm_r") target = -10 - dragLean * 0.3;
                    else if (id == "hand_l") target = 5;
                    else if (id == "hand_r") target = -5;
                    else if (id == "tail_base") target += Clamp(-_dragVelX * 0.8, -38, 38);
                    else if (id == "tail_mid") target += Clamp(-_dragVelX * 0.6, -26, 26);
                    else if (id == "tail_tip") target += Clamp(-_dragVelX * 0.45, -18, 18);
                    else if (id == "skirt") target += Clamp(-_dragVelX * 0.4, -14, 14);
                    else if (id == "ahoge") target += Clamp(-_dragVelX * 0.35, -16, 16);
                }
                if (_mode == PetState.Walk)
                {
                    double step = Math.Sin(_walkPhase * 8.0);
                    if (id == "upper_arm_l") target += step * 10;
                    else if (id == "upper_arm_r") target -= step * 10;
                    else if (id == "forearm_l") target += step * 6;
                    else if (id == "forearm_r") target -= step * 6;
                    else if (id == "tail_base") target += step * 5;
                    else if (id == "tail_mid") target += step * 4;
                    else if (id == "tail_tip") target += step * 3;
                    else if (id == "skirt") target += Math.Abs(step) * 2;
                }
                spring.Update(dt, target);
            }

            ApplyTransforms(breath, sway, bob);
            UpdateParticles(dt);
            UpdateBubble();
            UpdateBalanceOverlay();
        }

        double HeadTiltTarget()
        {
            double tilt;
            switch (_mode)
            {
                case PetState.Think: tilt = -2.6 + Math.Sin(_t * 0.9) * 0.6; break;
                case PetState.Work: tilt = -1.2; break;
                case PetState.Celebrate: tilt = -5.0; break;
                case PetState.Sad: tilt = 5.5; break;
                case PetState.Sleep: tilt = 4.5; break;
                case PetState.Poke: tilt = -5.5; break;
                case PetState.Alert: tilt = -4.0; break;
                case PetState.Drag: tilt = _tiltDrag + Clamp(_dragVelX * 0.03, -8, 8); break;
                case PetState.Walk: tilt = Math.Sin(_walkPhase * 8.0) * 1.2; break;
                default: tilt = Math.Sin(_t * 0.55) * 1.1; break;
            }
            if (_perchActive) tilt += 2.2;
            // gaze: the eyes lead, the head follows a little
            tilt += Clamp(-_eyeX * (_facing < 0 ? -1.0 : 1.0) * 0.35, -3.2, 3.2);
            return tilt;
        }

        void ApplyTransforms(double breath, double sway, double bob)
        {
            double w = _artW;
            double h = _artH;
            double ax = _manifest.Canvas.Anchor[0] * w;
            double ay = _manifest.Canvas.Anchor[1] * h;
            double scaleX = 1 + breath;
            double scaleY = 1 - breath;

            var body = Matrix.Identity;
            body.Translate(-ax, -ay);
            double dragTilt = _mode == PetState.Drag ? Clamp(-_dragVelX * 0.06, -9, 9) : 0;
            body.Scale(scaleX * _twirlX * _facing, scaleY * _twirlSquash);
            body.Rotate(sway + _walkTilt + dragTilt);
            body.Translate(ax, ay + bob);
            body.Translate(_lookX * 0.6, _lookY * 0.6);

            var head = Matrix.Identity;
            head.RotateAt(_headTilt, ax, ay - h * 0.62);
            head.Append(body);

            // Parent-child rig: a child rotates about its own pivot first, then follows
            // its parent's accumulated transform (tail_base -> tail_mid -> tail_tip, ...),
            // so a wave travels down the chain without any extra animation data.
            var solved = new Dictionary<string, Matrix>();
            for (int i = 0; i < _layers.Count; i++)
                SolveLayerMatrix(_layers[i], body, head, w, h, solved);

            PetLayer blush;
            if (_byId.TryGetValue("blush", out blush)) blush.Visual.Opacity = _blushOpacity;

            if (_poseVisual != null && !string.IsNullOrEmpty(_poseMode))
                _poseVisual.RenderTransform = new MatrixTransform(body);

            if (_eyeRig != null)
            {
                _eyeRig.UpdateMatrix(head);
                _eyeRig.SetGaze(_gazeNx, _gazeNy, _facing < 0 ? -1.0 : 1.0);
            }
        }

        Matrix SolveLayerMatrix(PetLayer layer, Matrix body, Matrix head, double w, double h, Dictionary<string, Matrix> solved)
        {
            string id = layer.Spec.Id;
            Matrix cached;
            if (solved.TryGetValue(id, out cached)) return cached;

            Matrix baseMatrix;
            PetLayer parentLayer;
            if (!string.IsNullOrEmpty(layer.Spec.Parent) && _byId.TryGetValue(layer.Spec.Parent, out parentLayer))
                baseMatrix = SolveLayerMatrix(parentLayer, body, head, w, h, solved);
            else
                baseMatrix = IsHeadLayer(id) ? head : body;

            Matrix local = Matrix.Identity;
            SpringState spring;
            if (_springs.TryGetValue(id, out spring))
            {
                double px = layer.Spec.Pivot[0] * w;
                double py = layer.Spec.Pivot[1] * h;
                local.RotateAt(spring.Angle, px, py);
            }
            local.Append(baseMatrix);

            layer.Matrix = local;
            layer.Transform.Matrix = local;
            solved[id] = local;
            return local;
        }

        static bool IsHeadLayer(string id)
        {
            return id == "head" || id == "brow" || id == "blush" || id == "mouth" || id == "mouth_smile" ||
                   id == "eyes" || id == "eyes_open" || id == "iris" || id == "ahoge" ||
                   id == "headdress" || id == "bow_r" ||
                   id.StartsWith("hair_") || id.StartsWith("ear_");
        }

        static bool IsArm(string id) { return id == "arm_l" || id == "arm_r"; }
        static bool IsTail(string id) { return id == "tail"; }
        static bool IsBodySpring(string id) { return id == "skirt" || id == "apron"; }

        // ------------------------------------------------------------ state machine

        void HandleSignal(Signal signal)
        {
            _lastActivity = _t;
            _lastInteraction = _t;
            switch (signal.Type)
            {
                case "task.start":
                case "task.progress":
                case "assistant.stream":
                case "tool.result":
                    _agentBusy = true;
                    break;
                case "task.done":
                case "task.failed":
                case "turn.end":
                    _agentBusy = false;
                    break;
                case "agent.status":
                    _agentBusy = signal.Status == "running";
                    break;
            }
            switch (signal.Type)
            {
                case "agent.status":
                    if (signal.Status == "running") SetState(_mode == PetState.Work ? PetState.Work : PetState.Think, 0);
                    else if (_mode != PetState.Celebrate && _mode != PetState.Poke) SetState(PetState.Idle, 0);
                    break;
                case "assistant.stream":
                    _talking = true;
                    _talkingUntil = _t + 1.4;
                    if (_mode == PetState.Idle) SetState(PetState.Think, 0);
                    break;
                case "tool.result":
                    if (signal.Ok) SetState(PetState.Work, _t + 1.6);
                    else SetState(PetState.Sad, _t + 2.4);
                    break;
                case "turn.end":
                    SetState(PetState.Celebrate, _t + 2.2);
                    SpawnBurst("✦", 10, 1.0);
                    // the task summary may already be on screen; do not replace it
                    if (!BubbleVisible) ShowBubble("搞定啦！");
                    break;
                case "agent.error":
                    SetState(PetState.Sad, _t + 3.0);
                    ShowBubble("呜…出错了");
                    break;
                case "user.question":
                    SetState(PetState.Alert, _t + 4.0);
                    ShowBubble("在等你回答～");
                    break;
                case "goal.changed":
                    SetState(PetState.Celebrate, _t + 1.8);
                    SpawnBurst("✦", 8, 0.9);
                    break;
                case "pet.twirl":
                    SetState(PetState.Celebrate, _t + 1.3);
                    StartTwirl();
                    ShowBubble("好耶！");
                    break;
                case "pet.poke":
                    SetState(PetState.Poke, _t + 1.1);
                    SpawnBurst("♥", 4, 0.6);
                    break;
                case "pet.sleep":
                    SetState(PetState.Sleep, 0);
                    // keep the test hook asleep until the next real event wakes it
                    _lastActivity = _t - _sleepAfterSeconds;
                    break;
                case "pet.blink":
                    _blinkPhase = 0;
                    _blinkTimer = 0;
                    break;
                case "pet.balance":
                    ShowBalancePeek();
                    break;
                case "pet.walk":
                    StartWalk();
                    break;
                case "pet.drag":
                    SetState(PetState.Drag, _t + 1.6);
                    _dragVelX = signal.Count;
                    break;
                case "pet.say":
                    // test hook: <type>pet.say</type> with {"text": "…", "durationMs": N}
                    ShowBubble(string.IsNullOrEmpty(signal.Text) ? "…" : signal.Text,
                        signal.DurationMs > 0 ? signal.DurationMs / 1000.0 : ReactionBubbleSeconds);
                    break;
                case "task.start":
                    SetState(PetState.Work, 0);
                    // a short hello; the 1-minute window is kept for her real summaries
                    SayNow(Pick(new[] { "开工咯～", "今天也一起加油！", "包在我身上…大概！" }), GreetingBubbleSeconds);
                    SpawnBurst("✦", 4, 0.6);
                    break;
                case "task.progress":
                    _taskTools = signal.Count;
                    if (_random.NextDouble() < 0.22) SayRandom(new[] { "第 " + _taskTools + " 步了…", "还在忙，别催～", "快好了快好了" });
                    SetState(PetState.Work, _t + 1.2);
                    break;
                case "task.done":
                    SetState(PetState.Celebrate, _t + 2.4);
                    SpawnBurst("✦", 10, 1.0);
                    ShowBubble("搞定！" + signal.Count + " 步 · " + Math.Max(1, signal.DurationMs / 1000) + " 秒");
                    break;
                case "task.failed":
                    SetState(PetState.Sad, _t + 3.0);
                    SayNow(Pick(new[] { "呜…出错了", "没关系的，再来一次" }));
                    break;
            }
        }

        void StartTwirl()
        {
            _spinning = true;
            _spinStart = _t;
            _jumpOffset = 0;
        }

        void SetState(PetState next, double until)
        {
            if (_mode == next && until <= _stateUntil) return;
            _mode = next;
            _stateUntil = until;
            if (next == PetState.Celebrate)
            {
                _blushOpacity = 0.8;
                SpawnBurst("♥", 6, 0.8);
            }
            if (next == PetState.Poke) _blushOpacity = 1.0;
            if (next == PetState.Sad) _blushOpacity = 0.35;
            if (next != PetState.Walk) _facing = 1.0;
        }

        void UpdateState(double dt)
        {
            if (_talking && _t > _talkingUntil) _talking = false;
            if (_stateUntil > 0 && _t > _stateUntil)
            {
                _stateUntil = -1;
                if (_mode != PetState.Drag) SetState(PetState.Idle, 0);
            }

            string preset = "neutral";
            switch (_mode)
            {
                case PetState.Sleep: preset = "sleepy"; break;
                case PetState.Celebrate: preset = "celebrate"; break;
                case PetState.Sad: preset = "sad"; break;
                case PetState.Poke: preset = "shy"; break;
                case PetState.Alert: preset = "alert"; break;
                case PetState.Drag: preset = "alert"; break;
                case PetState.Think: preset = "focused"; break;
                case PetState.Work: preset = "focused"; break;
                default: preset = "neutral"; break;
            }
            if (!InPose)
            {
                ApplyFacePreset(preset);

                if (_mode == PetState.Sleep)
                {
                    SetEyes("closed");
                    if (_eyeRig != null) _eyeRig.SetState("closed");
                    SetMouth("sleep");
                }
                else if (_talking && _mode != PetState.Sad && _mode != PetState.Celebrate && _mode != PetState.Poke)
                {
                    _mouthPhase += dt;
                    int frame = ((int)(_mouthPhase / 0.13)) % 4 + 1;
                    SetMouth("talk_" + frame);
                }
            }

            if (_mode == PetState.Idle && _t - _lastActivity > _sleepAfterSeconds)
            {
                SetState(PetState.Sleep, 0);
                ShowBubble("Zzz…");
            }
            if (_mode == PetState.Sleep && _t - _lastActivity < 2) SetState(PetState.Idle, 0);
            if (!InPose && _mode == PetState.Idle && !_targetValid && !_cornerRest && !_fgFullscreen
                && !DragHoldActive()
                && _t > _nextWanderAt
                && _t - _lastInteraction > _walkIdleSeconds
                && _systemIdleSeconds > _walkIdleSeconds
                && _t - _lastForegroundChange > _walkIdleSeconds
                && !_agentBusy)
            {
                StartWalk();
            }

            // Peek easter egg: with no window to perch on and nobody around for a while,
            // she hides behind the taskbar and peeks up over the edge for a few seconds.
            if (!InPose && _mode == PetState.Idle && !_perchActive && !_fgFullscreen && !_cornerRest
                && !DragHoldActive()
                && _t > _nextPeekAt
                && !_agentBusy && _t - _lastInteraction > 45 && _systemIdleSeconds > 45
                && _manifest.Poses.ContainsKey("peek"))
            {
                StartPeek();
            }
            // a corner rest owns the peek pose; normal peek timing must not end it
            if (_poseMode == "peek" && !_cornerRest &&
                (_agentBusy || _mode != PetState.Idle || (_peekUntil > 0 && _t > _peekUntil))) EndPeek();

            if (_mode != PetState.Poke && _mode != PetState.Celebrate)
                _blushOpacity += (0.55 - _blushOpacity) * Math.Min(1, dt * 2);

            POINT cp;
            bool gotCursor = GetCursorPos(out cp);
            if (!string.IsNullOrEmpty(_options.QaCursor))
            {
                // QA: pin the cursor so the gaze logic can be checked deterministically
                string[] parts = _options.QaCursor.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    cp.X = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    cp.Y = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    gotCursor = true;
                }
            }
            if (gotCursor)
            {
                if (cp.X != _cursorX || cp.Y != _cursorY)
                {
                    _cursorX = cp.X;
                    _cursorY = cp.Y;
                    _lastCursorMoveAt = _t;
                }
                // Looking at the cursor is a reaction to it *moving*; if it just sits
                // there the gaze eases back to normal instead of staring at it forever.
                bool follow = _t - _lastCursorMoveAt < GazeFollowSeconds;
                double nx = 0, ny = 0;
                if (follow)
                {
                    Point cur = ScreenPixelsToLocal(cp.X, cp.Y);
                    // gaze is relative to the artwork, not to the padded window
                    nx = Clamp((cur.X - (_padX + _artW / 2)) / Math.Max(1, _artW), -1, 1);
                    ny = Clamp((cur.Y - (_padTop + _artH * 0.42)) / Math.Max(1, _artH), -1, 1);
                }
                double bodyRate = follow ? 5.0 : 2.5;
                _lookX += (nx * 3 - _lookX) * Math.Min(1, dt * bodyRate);
                _lookY += (ny * 2 - _lookY) * Math.Min(1, dt * bodyRate);
                double gazeX = Clamp(nx * _artW * 0.040, -9, 9);
                double gazeY = Clamp(ny * _artH * 0.022, -5, 5);
                _eyeX += (gazeX - _eyeX) * Math.Min(1, dt * 8);
                _eyeY += (gazeY - _eyeY) * Math.Min(1, dt * 8);
                // the iris follows quickly while tracking, and returns a bit more slowly
                double eyeRate = follow ? 10.0 : 3.5;
                _gazeNx += (nx - _gazeNx) * Math.Min(1, dt * eyeRate);
                _gazeNy += (ny - _gazeNy) * Math.Min(1, dt * eyeRate);
            }
        }

        PetLayer EyesLayer()
        {
            PetLayer layer;
            if (_byId.TryGetValue("eyes", out layer)) return layer;
            if (_byId.TryGetValue("eyes_open", out layer)) return layer;
            return null;
        }

        PetLayer MouthLayer()
        {
            PetLayer layer;
            if (_byId.TryGetValue("mouth", out layer)) return layer;
            if (_byId.TryGetValue("mouth_smile", out layer)) return layer;
            return null;
        }

        void SetEyes(string variant)
        {
            if (_blinkPhase >= 0) return;
            var layer = EyesLayer();
            if (layer != null) layer.SetVariant(variant);
        }

        void SetBrow(string variant)
        {
            PetLayer layer;
            if (!_byId.TryGetValue("brow", out layer)) return;
            bool none = variant == "none";
            layer.Visual.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            if (!none) layer.SetVariant(variant);
        }

        void SetBlush(string variant)
        {
            PetLayer layer;
            if (_byId.TryGetValue("blush", out layer)) layer.SetVariant(variant);
        }

        void SetHeadFace(string variant)
        {
            PetLayer layer;
            if (_byId.TryGetValue("head", out layer)) layer.SetVariant(variant);
        }

        // ------------------------------------------------------------ wearables / pose groups

        string _poseKey = "";
        int _gaitFrame = -1;

        // ---- full-body poses (v2 sit_pose / peek_pose) ----------------------
        readonly Dictionary<string, Image> _poseVisuals = new Dictionary<string, Image>();
        Image _poseVisual;
        string _poseMode = "";
        double _peekUntil;
        double _nextPeekAt = 240;

        bool InPose { get { return !string.IsNullOrEmpty(_poseMode); } }

        void SetLayerVisible(string id, bool visible)
        {
            PetLayer layer;
            if (!_byId.TryGetValue(id, out layer)) return;
            if (visible) layer.EnsureLoaded();
            layer.Visual.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        Image GetPoseVisual(string poseId)
        {
            Image visual;
            if (_poseVisuals.TryGetValue(poseId, out visual)) return visual;
            PoseSpec spec;
            if (!_manifest.Poses.TryGetValue(poseId, out spec)) return null;
            visual = new Image
            {
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = false,
                RenderTransformOrigin = new Point(0, 0),
                RenderTransform = new MatrixTransform(Matrix.Identity),
                Width = _artW,
                Height = _artH,
                Visibility = Visibility.Collapsed,
            };
            Canvas.SetLeft(visual, _padX);
            Canvas.SetTop(visual, _padTop);
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.LowQuality);
            _layerCanvas.Children.Add(visual);
            _poseVisuals[poseId] = visual;
            return visual;
        }

        /// <summary>Swap the whole character for a full-body pose (sit on a ledge / peek).</summary>
        void EnterPose(string poseId)
        {
            if (_poseMode == poseId) return;
            PoseSpec spec;
            if (!_manifest.Poses.TryGetValue(poseId, out spec)) return;
            var visual = GetPoseVisual(poseId);
            if (visual == null) return;
            if (visual.Source == null) visual.Source = PetLayer.LoadBitmap(spec.Image);
            if (_poseVisual != null) _poseVisual.Visibility = Visibility.Collapsed;
            _poseMode = poseId;
            _poseVisual = visual;
            foreach (var layer in _layers) layer.Visual.Visibility = Visibility.Collapsed;
            if (_eyeRig != null) _eyeRig.Root.Visibility = Visibility.Collapsed;
            _poseVisual.Visibility = Visibility.Visible;
            _poseKey = "";
        }

        void ExitPose()
        {
            if (string.IsNullOrEmpty(_poseMode)) return;
            _poseMode = "";
            if (_poseVisual != null) _poseVisual.Visibility = Visibility.Collapsed;
            _poseVisual = null;
            foreach (var layer in _layers)
                layer.Visual.Visibility = (layer.Spec.Hidden || layer.Spec.Retired) ? Visibility.Collapsed : Visibility.Visible;
            if (_eyeRig != null) _eyeRig.Root.Visibility = Visibility.Visible;
            _poseKey = "";
        }

        /// <summary>Canvas distance from the artwork top to the pose's ledge line, in window units.</summary>
        double PoseLedge(string poseId)
        {
            PoseSpec spec;
            if (!_manifest.Poses.TryGetValue(poseId, out spec)) return 0;
            return spec.LedgeY * (_artW / Math.Max(1, _manifest.Canvas.Width));
        }

        /// <summary>
        /// Swap the swap-in "action" layers (v2 split): raised arms while dragged,
        /// clasped arms while shy, the four-frame gait while walking, dangling legs
        /// while dragged. The original arms/legs are hidden instead of overlapped, so
        /// the split pieces never double up.
        /// </summary>
        void StartPeek()
        {
            if (!_manifest.Poses.ContainsKey("peek")) return;
            Rect wa = SystemParameters.WorkArea;
            EnterPose("peek");
            Left = Clamp(Left, wa.Left + 4 - _padX, Math.Max(wa.Left + 4 - _padX, wa.Right - _artW - 4 - _padX));
            Top = wa.Bottom - _padTop - PoseLedge("peek") + 2;
            _peekUntil = _t + 4.5;
            _nextPeekAt = _t + 180 + _random.NextDouble() * 240;
            if (_random.NextDouble() < 0.35) Say("在下面看着你哦～");
        }

        void EndPeek()
        {
            _peekUntil = 0;
            if (_poseMode == "peek") ExitPose();
            Rect wa = SystemParameters.WorkArea;
            Top = wa.Bottom - _padTop - _artH;
            _jumpOffset = -3;
        }

        void UpdatePose()
        {
            if (InPose) return;
            bool drag = _mode == PetState.Drag;
            bool shy = _mode == PetState.Poke;
            bool walk = _mode == PetState.Walk;
            bool armAction = drag || shy;
            string key = (drag ? "d" : "-") + (shy ? "s" : "-") + (walk ? "w" : "-");
            if (key == _poseKey) return;
            _poseKey = key;

            SetLayerVisible("upper_arm_l", !armAction);
            SetLayerVisible("upper_arm_r", !armAction);
            SetLayerVisible("forearm_l", !armAction);
            SetLayerVisible("forearm_r", !armAction);
            SetLayerVisible("hand_l", !armAction);
            SetLayerVisible("hand_r", !armAction);
            SetLayerVisible("arm_l_up", drag);
            SetLayerVisible("arm_r_up", drag);
            SetLayerVisible("arm_l_shy", shy);
            SetLayerVisible("arm_r_shy", shy);
            // The rear hair of the approved master still carries old sleeve/hand pixels;
            // the v2 action variant patches those spots, so use it whenever the arms move.
            SetLayerVisible("hair_back_upper", !armAction);
            SetLayerVisible("hair_back_lower", !armAction);
            SetLayerVisible("hair_back_action", armAction);

            SetLayerVisible("leg_l", !drag && !walk);
            SetLayerVisible("leg_r", !drag && !walk);
            SetLayerVisible("shoe_l", !drag && !walk);
            SetLayerVisible("shoe_r", !drag && !walk);
            SetLayerVisible("gait", walk);
            SetLayerVisible("legs_air", drag);
            if (walk) _gaitFrame = -1;
        }

        void UpdateGait()
        {
            if (InPose || _mode != PetState.Walk) return;
            PetLayer gait;
            if (!_byId.TryGetValue("gait", out gait)) return;
            int frame = 1 + (int)(_walkPhase * 8.0 / (Math.PI / 2)) % 4;
            if (frame == _gaitFrame) return;
            _gaitFrame = frame;
            gait.SetVariant("walk_" + frame);
        }

        // v2 master-locked expression presets (see manifest expressions)
        void ApplyFacePreset(string preset)
        {
            _facePreset = preset;
            string eyes = "open", brow = "none", mouth = "smile", blush = "soft", head = "default";
            switch (preset)
            {
                case "happy": eyes = "happy"; brow = "happy"; break;
                case "celebrate": eyes = "star"; brow = "surprised"; blush = "lines"; head = "brow_ready"; break;
                case "sad": eyes = "sad"; brow = "sad"; mouth = "sad"; head = "brow_ready"; break;
                case "angry": eyes = "angry"; brow = "angry"; mouth = "pout"; head = "brow_ready"; break;
                case "shy": eyes = "heart"; mouth = "talk_4"; blush = "deep"; break;
                case "sleepy": eyes = "sleepy"; mouth = "sleep"; break;
                case "alert": brow = "surprised"; mouth = "surprised"; blush = "soft"; head = "brow_ready"; break;
                case "focused": brow = "determined"; mouth = "smile"; head = "brow_ready"; break;
                default: eyes = "open"; brow = "none"; mouth = "smile"; blush = "soft"; head = "default"; break;
            }
            if (brow != "none") head = "brow_ready";
            _eyesPreset = eyes;

            string rigState = "open", rigPupil = "normal";
            switch (preset)
            {
                case "happy": rigState = "happy"; break;
                case "celebrate": rigState = "open"; rigPupil = "star"; break;
                case "sad": rigState = "sad"; break;
                case "angry": rigState = "angry"; break;
                case "shy": rigState = "open"; rigPupil = "heart"; break;
                case "sleepy": rigState = "sleepy"; break;
                default: rigState = "open"; rigPupil = "normal"; break;
            }
            _rigEyesState = rigState;
            _rigPupil = rigPupil;

            SetHeadFace(head);
            SetBrow(brow);
            SetBlush(blush);
            if (_mode != PetState.Sleep && _blinkPhase < 0)
            {
                SetEyes(eyes);
                if (_eyeRig != null)
                {
                    _eyeRig.SetState(rigState);
                    _eyeRig.SetPupil(rigPupil);
                }
            }
            if (_mode != PetState.Sleep && !_talking) SetMouth(mouth);
        }

        void SetMouth(string variant)
        {
            var layer = MouthLayer();
            if (layer != null) layer.SetVariant(variant);
        }

        void UpdateBlink(double dt)
        {
            if (InPose) return;
            if (_mode == PetState.Sleep) { _blinkPhase = -1; return; }
            if (_blinkPhase >= 0)
            {
                _blinkTimer -= dt;
                if (_blinkTimer <= 0)
                {
                    if (_blinkPhase == 0) { ApplyBlink("half"); _blinkTimer = 0.05; _blinkPhase = 1; }
                    else if (_blinkPhase == 1) { ApplyBlink("closed"); _blinkTimer = 0.12; _blinkPhase = 2; }
                    else if (_blinkPhase == 2) { ApplyBlink("half"); _blinkTimer = 0.05; _blinkPhase = 3; }
                    else { ApplyBlink(_eyesPreset); _blinkPhase = -1; _nextBlinkAt = _t + 2.2 + _random.NextDouble() * 4.0; }
                }
                return;
            }
            if (_t >= _nextBlinkAt) { _blinkPhase = 0; _blinkTimer = 0; }
        }

        void ApplyBlink(string variant)
        {
            var layer = EyesLayer();
            if (layer != null) layer.SetVariant(variant);
            if (_eyeRig != null)
            {
                string state = variant == "half" ? "half" : variant == "closed" ? "closed" : _rigEyesState;
                _eyeRig.SetState(state);
            }
        }

        void InitEyeWarp()
        {
            // Eye assets are now "features only" (no baked skin patch). Warping that
            // sparse layer would slide iris pixels off their socket and expose head
            // skin through the gap, so gaze warp is disabled; head lean still follows.
            return;
            var layer = EyesLayer();
            if (layer == null) return;
            _eyeLayer = layer;
            _eyeLayer.ManagedSource = true;
            _eyeWarp = new WriteableBitmap(layer.Bitmap.PixelWidth, layer.Bitmap.PixelHeight, 96, 96, PixelFormats.Bgra32, null);
            _eyeWarpPixels = new byte[0];
            layer.Visual.Source = _eyeWarp;
            UpdateEyeWarp(true);
        }

        void ComputeEyeBBox()
        {
            var src = _eyeLayer.Bgra;
            int stride = _eyeLayer.Stride;
            int w = _eyeLayer.Bitmap.PixelWidth;
            int h = _eyeLayer.Bitmap.PixelHeight;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    if (src[row + x * 4 + 3] > 12)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < minX || maxY < minY)
            {
                _warpX = _warpY = _warpW = _warpH = 0;
                return;
            }
            int margin = 8;
            _warpX = Math.Max(0, minX - margin);
            _warpY = Math.Max(0, minY - margin);
            int right = Math.Min(w - 1, maxX + margin);
            int bottom = Math.Min(h - 1, maxY + margin);
            _warpW = right - _warpX + 1;
            _warpH = bottom - _warpY + 1;
            _warpStride = _warpW * 4;
            _eyeWarpPixels = new byte[_warpStride * _warpH];
            _eyeWarpWeights = new float[_warpW * _warpH];
        }

        void ComputeEyeWeights()
        {
            if (_warpW <= 0 || _warpH <= 0) return;
            double radius = Math.Max(4.0, _manifest.Gaze.Radius * _eyeWarp.PixelWidth);
            var centers = _manifest.Gaze.Centers;
            for (int y = 0; y < _warpH; y++)
            {
                double py = _warpY + y;
                for (int x = 0; x < _warpW; x++)
                {
                    double px = _warpX + x;
                    double best = 0;
                    for (int i = 0; i < centers.Length; i++)
                    {
                        double cx = centers[i][0] * _eyeWarp.PixelWidth;
                        double cy = centers[i][1] * _eyeWarp.PixelHeight;
                        double dx = px - cx;
                        double dy = py - cy;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        double t = 1.0 - d / radius;
                        if (t <= 0) continue;
                        if (t > 1) t = 1;
                        double f = t * t * (3 - 2 * t);
                        if (f > best) best = f;
                    }
                    _eyeWarpWeights[y * _warpW + x] = (float)best;
                }
            }
        }

        void UpdateEyeWarp(bool force)
        {
            if (_eyeLayer == null || _eyeWarp == null) return;
            string variant = _eyeLayer.CurrentVariant;
            double gx = 0, gy = 0;
            // Only the neutral eye variants have a tight iris-only alpha region.
            // Expressive variants carry a skin patch; warping them would slide that
            // patch against the head and show a rectangular seam.
            bool warpable = variant == "open" || variant == "half" || variant == "closed" || variant == "";
            if (variant != "closed" && warpable)
            {
                // _facing mirrors the whole eye layer; warp happens in image space,
                // so the horizontal gaze direction must be mirrored back.
                gx = _gazeNx * _manifest.Gaze.Shift * _eyeWarp.PixelWidth * (_facing < 0 ? -1.0 : 1.0);
                gy = _gazeNy * _manifest.Gaze.Shift * _eyeWarp.PixelHeight * 0.55;
            }
            bool variantChanged = variant != _warpVariant;
            if (!force && !variantChanged)
            {
                if (_t - _lastWarpUpdate < 0.03) return;
                if (Math.Abs(gx - _warpGx) < 0.35 && Math.Abs(gy - _warpGy) < 0.35) return;
            }
            _warpGx = gx;
            _warpGy = gy;
            _lastWarpUpdate = _t;
            if (variantChanged || force)
            {
                _warpVariant = variant;
                // a new eye variant has a different alpha bbox: rebuild the buffer so no
                // stale iris pixels from the previous frame survive outside the new bbox
                _eyeWarp = new WriteableBitmap(_eyeLayer.Bitmap.PixelWidth, _eyeLayer.Bitmap.PixelHeight, 96, 96, PixelFormats.Bgra32, null);
                _eyeLayer.Visual.Source = _eyeWarp;
                ComputeEyeBBox();
                ComputeEyeWeights();
            }
            if (_warpW <= 0 || _warpH <= 0) return;

            var src = _eyeLayer.Bgra;
            int srcStride = _eyeLayer.Stride;
            int sw = _eyeLayer.Bitmap.PixelWidth;
            int sh = _eyeLayer.Bitmap.PixelHeight;
            var dst = _eyeWarpPixels;
            var weights = _eyeWarpWeights;

            for (int y = 0; y < _warpH; y++)
            {
                int py = _warpY + y;
                int dstRow = y * _warpStride;
                for (int x = 0; x < _warpW; x++)
                {
                    double w = weights[y * _warpW + x];
                    double sx = _warpX + x - gx * w;
                    double sy = py - gy * w;
                    int x0 = (int)Math.Floor(sx);
                    int y0 = (int)Math.Floor(sy);
                    double fx = sx - x0;
                    double fy = sy - y0;
                    double sumA = 0, sumR = 0, sumG = 0, sumB = 0;
                    for (int oy = 0; oy < 2; oy++)
                    {
                        int yy = y0 + oy;
                        if (yy < 0 || yy >= sh) continue;
                        double wy = oy == 0 ? 1 - fy : fy;
                        if (wy <= 0) continue;
                        for (int ox = 0; ox < 2; ox++)
                        {
                            int xx = x0 + ox;
                            if (xx < 0 || xx >= sw) continue;
                            double wx = ox == 0 ? 1 - fx : fx;
                            double wgt = wx * wy;
                            if (wgt <= 0) continue;
                            int idx = yy * srcStride + xx * 4;
                            double a = src[idx + 3] / 255.0;
                            sumA += a * wgt;
                            sumB += src[idx + 0] * a * wgt;
                            sumG += src[idx + 1] * a * wgt;
                            sumR += src[idx + 2] * a * wgt;
                        }
                    }
                    int di = dstRow + x * 4;
                    if (sumA <= 0.0001)
                    {
                        dst[di] = 0; dst[di + 1] = 0; dst[di + 2] = 0; dst[di + 3] = 0;
                    }
                    else
                    {
                        dst[di] = (byte)Math.Min(255, sumB / sumA);
                        dst[di + 1] = (byte)Math.Min(255, sumG / sumA);
                        dst[di + 2] = (byte)Math.Min(255, sumR / sumA);
                        dst[di + 3] = (byte)Math.Min(255, sumA * 255.0);
                    }
                }
            }
            _eyeWarp.WritePixels(new Int32Rect(_warpX, _warpY, _warpW, _warpH), dst, _warpStride, 0);
        }

        // ------------------------------------------------------------ effects

        BitmapSource LoadFx(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            BitmapSource cached;
            if (_fxCache.TryGetValue(name, out cached)) return cached;
            try
            {
                string path = Path.Combine(_options.PetDir, "parts", name + ".png");
                if (!File.Exists(path)) return null;
                cached = PetLayer.LoadBitmap(path);
                _fxCache[name] = cached;
                return cached;
            }
            catch { return null; }
        }

        void SpawnBurst(string glyph, int count, double scale)
        {
            string fx = glyph == "♥" ? "fx_heart" : glyph == "✦" ? "fx_star" : null;

            BitmapSource bmp = LoadFx(fx);
            for (int i = 0; i < count; i++)
            {
                FrameworkElement element;
                if (bmp != null)
                {
                    double size = (22 + _random.NextDouble() * 12) * scale;
                    element = new Image
                    {
                        Source = bmp,
                        Width = size,
                        Height = size,
                        Stretch = Stretch.Uniform,
                        Opacity = 0.95,
                        IsHitTestVisible = false,
                    };
                }
                else
                {
                    element = new TextBlock
                    {
                        Text = glyph,
                        Foreground = new SolidColorBrush(glyph == "♥" ? Color.FromRgb(255, 110, 150) : Color.FromRgb(255, 214, 102)),
                        FontSize = 14 + _random.NextDouble() * 8,
                        Opacity = 0.95,
                    };
                }
                double x = Width * (0.3 + _random.NextDouble() * 0.4);
                double y = Height * (0.2 + _random.NextDouble() * 0.3);
                Canvas.SetLeft(element, x);
                Canvas.SetTop(element, y);
                _fxCanvas.Children.Add(element);
                _particles.Add(new Particle
                {
                    Element = element, X = x, Y = y,
                    Vx = (_random.NextDouble() - 0.5) * 40 * scale,
                    Vy = -30 - _random.NextDouble() * 40,
                    Life = 1.1 + _random.NextDouble() * 0.6, Age = 0,
                });
            }
        }

        void SpawnZzz()
        {
            BitmapSource bmp = LoadFx("fx_zzz");
            FrameworkElement element;
            if (bmp != null)
            {
                element = new Image { Source = bmp, Width = 26, Height = 26, Opacity = 0.9, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            }
            else
            {
                element = new TextBlock
                {
                    Text = "z",
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 120, 150, 210)),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Opacity = 0.9,
                };
            }
            double x = Width * 0.66;
            double y = Height * 0.28;
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            _fxCanvas.Children.Add(element);
            _particles.Add(new Particle { Element = element, X = x, Y = y, Vx = 8, Vy = -16, Life = 2.4, Age = 0 });
        }

        void UpdateParticles(double dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    _fxCanvas.Children.Remove(p.Element);
                    _particles.RemoveAt(i);
                    continue;
                }
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                p.Vy += 30 * dt;
                p.Element.Opacity = Math.Max(0, 1 - p.Age / p.Life);
                Canvas.SetLeft(p.Element, p.X);
                Canvas.SetTop(p.Element, p.Y);
            }
            if (_mode == PetState.Sleep && _random.NextDouble() < dt * 0.8) SpawnZzz();
        }

        void BuildBubble()
        {
            _bubble = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 60, 90, 140)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 5, 9, 6),
                Opacity = 0,
                Visibility = Visibility.Collapsed,
            };
            _bubbleText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(35, 50, 80)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
            };
            _bubble.Child = _bubbleText;
            _fxCanvas.Children.Add(_bubble);
        }

        bool BubbleVisible
        {
            get { return _bubble != null && _bubble.Visibility == Visibility.Visible && _bubble.Opacity > 0.01; }
        }

        void ShowBubble(string text, double holdSeconds = BubbleHoldSeconds)
        {
            if (_bubble == null || string.IsNullOrEmpty(text)) return;
            // only one text box at a time: the speech bubble wins over the balance badge
            HideBalancePeek();
            _bubbleFadingOut = false;
            _bubbleText.Text = text;
            _bubble.Visibility = Visibility.Visible;
            _bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = _bubble.DesiredSize.Width;
            // centred on the artwork and clamped into the window
            double artCenter = _padX + _artW / 2;
            double x = artCenter - bw / 2;
            x = Math.Max(4, Math.Min(x, Math.Max(4, Width - bw - 4)));
            double y = _padTop + _artH * 0.10;
            Canvas.SetLeft(_bubble, x);
            Canvas.SetTop(_bubble, y);
            _bubbleUntil = _t + holdSeconds;
            // stay quiet for a moment after any line, so small talk cannot keep the
            // box on screen forever (each new line would otherwise restart the timer)
            _nextBubbleAt = _t + ChatterGapSeconds;
        }

        // ------------------------------------------------------------ foreground window

        static bool SameRect(Rect a, Rect b)
        {
            return Math.Abs(a.Left - b.Left) < 0.5 && Math.Abs(a.Top - b.Top) < 0.5
                && Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;
        }

        /// <summary>
        /// QA hook: play the foreground window from a small text file
        /// ("left,top,width,height[,fullscreen]" in physical pixels) so the perch / corner
        /// rules can be exercised deterministically without touching the real desktop.
        /// </summary>
        void PollSimulatedForeground()
        {
            int left = 0, top = 0, w = 0, h = 0, full = 0;
            bool ok = false;
            try
            {
                if (File.Exists(_options.QaFgFile))
                {
                    string[] parts = File.ReadAllText(_options.QaFgFile).Split(
                        new[] { ',', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        left = int.Parse(parts[0], CultureInfo.InvariantCulture);
                        top = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        w = int.Parse(parts[2], CultureInfo.InvariantCulture);
                        h = int.Parse(parts[3], CultureInfo.InvariantCulture);
                        full = parts.Length >= 5 ? int.Parse(parts[4], CultureInfo.InvariantCulture) : 0;
                        ok = true;
                    }
                }
            }
            catch { ok = false; }

            if (!ok)
            {
                // simulated foreground went away
                if (_fgStateHwnd != IntPtr.Zero)
                {
                    _fgStateHwnd = IntPtr.Zero;
                    _fgStateValid = false;
                    _fgStateHaveRect = false;
                    _fgFullscreen = false;
                    _fgRestValid = false;
                    _fgStateSince = _t;
                    _lastForegroundSeen = IntPtr.Zero;
                    _lastForegroundChange = _t;
                }
                return;
            }

            double dpi = _dpiScale > 0 ? _dpiScale : 1.0;
            Rect rectD = new Rect(left / dpi, top / dpi, w / dpi, h / dpi);
            IntPtr hwnd = new IntPtr(0x51D0);
            bool fullscreen = full != 0;
            bool accept = !fullscreen && rectD.Width >= 220 && rectD.Height >= 160;
            if (fullscreen)
            {
                _fgRestArea = SystemParameters.WorkArea;
                _fgRestValid = true;
            }
            else
            {
                _fgRestValid = false;
            }

            bool changed = hwnd != _fgStateHwnd
                || fullscreen != _fgFullscreen
                || accept != _fgStateValid
                || !_fgStateHaveRect
                || !SameRect(rectD, _fgStateRect);
            if (changed)
            {
                if (hwnd != _fgStateHwnd) _lastForegroundChange = _t;
                _fgStateSince = _t;
            }
            _fgStateHwnd = hwnd;
            _fgStateRect = rectD;
            _fgStateHaveRect = true;
            _fgStateValid = accept;
            _fgFullscreen = fullscreen;
            _lastForegroundSeen = hwnd;

            if (!accept) return;
            if (_t - _fgStateSince < PerchStableSeconds) return;
            _targetHwnd = hwnd;
            _targetRect = rectD;
            _targetTitle = "qa-sim";
            _targetValid = true;
        }

        void PollForegroundWindow()
        {
            _targetValid = false;
            _targetHwnd = IntPtr.Zero;
            if (_qaPinForeground) return;   // QA: keep the forced foreground state
            if (!string.IsNullOrEmpty(_options.QaFgFile))
            {
                PollSimulatedForeground();
                return;
            }

            IntPtr fg = GetForegroundWindow();
            if (fg != _lastForegroundSeen)
            {
                _lastForegroundSeen = fg;
                _lastForegroundChange = _t;
            }

            bool own = _source != null && fg == _source.Handle;
            bool haveRect = false;
            bool fullscreen = false;
            bool accept = false;
            Rect rectD = default(Rect);
            string title = "";

            bool usable = fg != IntPtr.Zero && (own || (IsWindowVisible(fg) && !IsIconic(fg)));
            if (usable && !own)
            {
                int cloaked;
                if (DwmGetWindowAttribute(fg, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) usable = false;
            }
            if (usable)
            {
                var cls = new StringBuilder(128);
                GetClassName(fg, cls, cls.Capacity);
                string className = cls.ToString();
                if (className == "Progman" || className == "WorkerW" ||
                    className == "Shell_TrayWnd" || className == "DV2ControlHost")
                {
                    usable = false;
                }
            }
            if (usable)
            {
                RECT rect = new RECT();
                bool gotRect = DwmGetWindowAttribute(fg, 9, out rect, Marshal.SizeOf(typeof(RECT))) == 0;
                if (!gotRect) gotRect = GetWindowRect(fg, out rect);
                if (gotRect)
                {
                    haveRect = true;
                    double dpi = _dpiScale > 0 ? _dpiScale : 1.0;
                    rectD = new Rect(rect.Left / dpi, rect.Top / dpi,
                        (rect.Right - rect.Left) / dpi, (rect.Bottom - rect.Top) / dpi);

                    // Fullscreen windows (video, games, F11/kiosk) and maximized windows
                    // have no visible "window edge" to sit on: she must not climb onto
                    // them and block the content. Own window included so a maximized DSH
                    // window also counts as a fullscreen foreground.
                    IntPtr monitor = MonitorFromWindow(fg, 2); // MONITOR_DEFAULTTONEAREST
                    var mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    bool coversMonitor = false;
                    bool haveMonitor = false;
                    if (GetMonitorInfo(monitor, ref mi))
                    {
                        haveMonitor = true;
                        int monW = mi.rcMonitor.Right - mi.rcMonitor.Left;
                        int monH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                        int winW = rect.Right - rect.Left;
                        int winH = rect.Bottom - rect.Top;
                        coversMonitor = winW >= monW - 4 && winH >= monH - 4;
                    }
                    fullscreen = coversMonitor || IsZoomed(fg);

                    if (fullscreen)
                    {
                        // anchor for the corner rest: the true monitor edge for a real
                        // fullscreen window, the work-area edge for a maximized one
                        double dpi2 = _dpiScale > 0 ? _dpiScale : 1.0;
                        RECT area = new RECT();
                        bool haveArea = false;
                        if (haveMonitor)
                        {
                            area = coversMonitor ? mi.rcMonitor : mi.rcWork;
                            haveArea = true;
                        }
                        else
                        {
                            Rect waf = SystemParameters.WorkArea;
                            area.Left = (int)Math.Round(waf.Left * dpi2);
                            area.Top = (int)Math.Round(waf.Top * dpi2);
                            area.Right = (int)Math.Round(waf.Right * dpi2);
                            area.Bottom = (int)Math.Round(waf.Bottom * dpi2);
                            haveArea = true;
                        }
                        if (haveArea)
                        {
                            _fgRestArea = new Rect(area.Left / dpi2, area.Top / dpi2,
                                (area.Right - area.Left) / dpi2, (area.Bottom - area.Top) / dpi2);
                            _fgRestValid = true;
                        }
                    }
                    else
                    {
                        _fgRestValid = false;
                    }

                    if (!own && !fullscreen && rectD.Width >= 220 && rectD.Height >= 160)
                    {
                        var titleBuf = new StringBuilder(256);
                        GetWindowText(fg, titleBuf, titleBuf.Capacity);
                        title = titleBuf.ToString();
                        accept = true;
                    }
                }
            }

            // Remember when the foreground state (hwnd, bounds, kind) last changed. A
            // perch candidate that keeps moving is ignored until it has settled, so she
            // moves over to a window only after it has been stable for a few seconds.
            bool changed = fg != _fgStateHwnd
                || fullscreen != _fgFullscreen
                || accept != _fgStateValid
                || haveRect != _fgStateHaveRect
                || (haveRect && _fgStateHaveRect && !SameRect(rectD, _fgStateRect));
            if (changed) _fgStateSince = _t;
            _fgStateHwnd = fg;
            _fgStateRect = rectD;
            _fgStateHaveRect = haveRect;
            _fgStateValid = accept;
            _fgFullscreen = fullscreen;

            if (!accept) return;
            if (_t - _fgStateSince < PerchStableSeconds) return;

            _targetHwnd = fg;
            _targetRect = rectD;
            _targetTitle = title;
            _targetValid = true;
        }

        /// <summary>
        /// True while she can settle into a full-body pose: plain idle / thinking / working
        /// states, but not while a reaction (poke, celebration, sadness, drag, sleep,
        /// walk) is playing. A fullscreen foreground keeps her low in the corner even
        /// while the agent is busy.
        /// </summary>
        bool CalmForPose()
        {
            return _mode == PetState.Idle || _mode == PetState.Think || _mode == PetState.Work;
        }

        /// <summary>
        /// Fullscreen / maximized foreground: there is no window edge to sit on, so she
        /// slips into the bottom-right corner of the work area and lies low there (the
        /// pack's "peek" pose, upper body peeking over the bottom edge), instead of
        /// climbing to the top-right corner of the screen.
        /// </summary>
        void UpdateCornerRest(double dt)
        {
            if (_t < _perchCooldown) return;
            if (_mode == PetState.Drag || _mode == PetState.Poke || _mode == PetState.Celebrate ||
                _mode == PetState.Sleep || _mode == PetState.Walk)
            {
                if (_cornerRest) EndCornerRest();
                return;
            }
            // stand up first: a sitting pose sliding across the screen looks wrong
            if (_poseMode == "sit") ExitPose();
            // let the fullscreen state settle for a moment first: alt-tabbing through a
            // maximized window should not send her running to the corner every time
            if (_t - _fgStateSince < CornerRestDelaySeconds) return;

            Rect wa = _fgRestValid ? _fgRestArea : SystemParameters.WorkArea;
            bool resting = _cornerRest && _poseMode == "peek";
            double minLeft = wa.Left + 2 - _padX;
            double maxLeft = Math.Max(minLeft, wa.Right - _artW - 4 - _padX);
            double desiredLeft = Clamp(wa.Right - _artW - 8 - _padX, minLeft, maxLeft);
            double desiredTop = resting
                ? wa.Bottom - _padTop - PoseLedge("peek") + 2
                : wa.Bottom - _padTop - _artH;
            double dx = desiredLeft - Left;
            double dy = desiredTop - Top;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            bool tidy = CalmForPose();

            if (resting)
            {
                double k = Math.Min(1.0, dt * 6.0);
                Left += dx * k;
                Top += dy * k;
                _perchActive = true;
                _facing = 1.0;
                if (!tidy) EndCornerRest();
                return;
            }
            if (distance > 1.5)
            {
                double k = Math.Min(1.0, dt * 4.5);
                Left += dx * k;
                Top += dy * k;
                _perchActive = true;
                _cornerSettle = 0;
                return;
            }
            Left = desiredLeft;
            Top = desiredTop;
            _perchActive = true;
            if (_manifest.Poses.ContainsKey("peek"))
            {
                if (tidy) _cornerSettle += dt; else _cornerSettle = 0;
                if (_cornerSettle > CornerRestSettleSeconds) StartCornerRest();
            }
        }

        void StartCornerRest()
        {
            if (_cornerRest && _poseMode == "peek") return;
            EnterPose("peek");
            if (_poseMode != "peek") return;
            _cornerRest = true;
            _cornerSettle = 0;
            _peekUntil = 0;         // stays low until the fullscreen window goes away
            _facing = 1.0;
            if (_random.NextDouble() < 0.3) Say(Pick(new[] { "我在这儿趴一会儿～", "不挡着你～" }));
        }

        void EndCornerRest()
        {
            _cornerRest = false;
            _cornerSettle = 0;
            if (_poseMode == "peek")
            {
                ExitPose();
                // stand up onto the bottom edge where the peek pose was
                Rect wa = _fgRestValid ? _fgRestArea : SystemParameters.WorkArea;
                Top = wa.Bottom - _padTop - _artH;
                _jumpOffset = -3;
            }
        }

        /// <summary>
        /// True for a minute after the user drops her from a drag, as long as the foreground
        /// window has not changed: carrying her somewhere is a request to stay there.
        /// </summary>
        bool DragHoldActive()
        {
            if (_t - _dragReleasedAt >= DragHoldSeconds) return false;
            if (_fgStateHwnd != _dragFgHwnd) return false;
            if (_fgFullscreen != _dragFgFullscreen) return false;
            if (_fgStateHaveRect != _dragFgHaveRect) return false;
            if (_fgStateHaveRect && !SameRect(_fgStateRect, _dragFgRect)) return false;
            return true;
        }

        /// <summary>
        /// Where she should sit across the top of the target window.
        ///
        /// The classic perch spot is the window's top-right corner, but that is exactly where
        /// the caption buttons live (minimize / maximize / close), so parking there covers
        /// them. Two rules, in order of preference:
        ///
        ///  1. Wide window — stay on the right, but shift inwards by the width of the button
        ///     strip plus a margin so the buttons stay clickable.
        ///  2. Narrow window — there is no room to be both on the right and clear of the
        ///     buttons, so sit on the LEFT instead. The left end of a title bar holds only
        ///     the icon and the title text, which nothing depends on.
        ///
        /// Returns the desired window Left (before the work-area clamp).
        /// </summary>
        double PerchLeft(Rect wa)
        {
            double dpi = _dpiScale > 0 ? _dpiScale : 1.0;
            // Three buttons is the common case. Windows pads the strip with
            // SM_CXPADDEDBORDER, but that metric reads 0 inside a system-DPI-aware process
            // (this one) even though the strip really is padded, so keep a floor instead of
            // trusting it: under-estimating here is how the artwork would creep back over
            // the close button.
            double buttons = GetSystemMetrics(SM_CXSIZE) * 3 / dpi;
            buttons += Math.Max(GetSystemMetrics(SM_CXPADDEDBORDER) / dpi, 8);
            const double Margin = 10;

            // Left is a window coordinate; the artwork starts _padX inside it.
            //   right-hand placement: keep the artwork's right edge left of the button strip
            //   left-hand placement:  put the artwork's left edge at window.Left + Margin
            double right = _targetRect.Right - buttons - Margin - _artW - _padX;
            double left = _targetRect.Left + Margin - _padX;

            // Only stay on the right if the artwork still fits inside the window when it is
            // offset clear of the buttons; otherwise the title bar's left end is the only
            // usable ledge.
            bool rightFits = right >= left;
            double desired = rightFits ? right : left;
            // ...and stay on screen even when the window itself is half off it.
            return Clamp(desired, wa.Left + 4 - _padX, Math.Max(wa.Left + 4 - _padX, wa.Right - _artW - 4 - _padX));
        }

        void UpdatePerch(double dt)
        {
            _perchActive = false;

            if (!_windowAware)
            {
                if (InPose) ExitPose();
                _cornerRest = false;
                _perchSettle = 0;
                return;
            }

            // She was just carried somewhere by hand: stay exactly where she was put and
            // leave the automatic perch / corner rules alone for a minute.
            if (DragHoldActive())
            {
                if (InPose) ExitPose();
                _cornerRest = false;
                _perchSettle = 0;
                return;
            }

            // Fullscreen / maximized window in front: no ledge to sit on, so she lies low
            // in the bottom-right corner instead of climbing to the top of the screen.
            if (_fgFullscreen)
            {
                _perchSettle = 0;
                UpdateCornerRest(dt);
                return;
            }

            // The fullscreen window went away: stand back up and resume the normal perch
            // controller (which waits for a window to be stable before moving over).
            if (_cornerRest) EndCornerRest();

            // while peeking, drop back to standing as soon as there is something to perch on
            if (InPose && _poseMode != "sit")
            {
                if (_targetValid && _t >= _perchCooldown) EndPeek();
                return;
            }

            if (!_targetValid)
            {
                if (_poseMode == "sit") ExitPose();
                _perchSettle = 0;
                return;
            }
            if (_t < _perchCooldown) return;
            if (_mode == PetState.Drag || _mode == PetState.Poke || _mode == PetState.Celebrate ||
                _mode == PetState.Alert || _mode == PetState.Sleep || _mode == PetState.Walk)
            {
                if (_poseMode == "sit") ExitPose();
                _perchSettle = 0;
                return;
            }

            Rect wa = SystemParameters.WorkArea;
            bool sitting = _poseMode == "sit";
            if (sitting && _lastTargetHwnd != _targetHwnd)
            {
                ExitPose();
                sitting = false;
            }

            double desiredLeft = PerchLeft(wa);
            // sitting: the pose's ledge line rests on the window's top edge, legs dangling
            // in front of the window. standing: feet on the edge (legacy behaviour).
            double desiredTop = sitting
                ? _targetRect.Top - _padTop - PoseLedge("sit") + 6
                : _targetRect.Top - Height + 6 + _padBottom;
            double minTop = wa.Top + 2 - _padTop;
            double maxTop = sitting
                ? Math.Max(minTop, wa.Bottom - 120)
                : Math.Max(minTop, wa.Bottom - _artH - 4 - _padTop);
            desiredTop = Clamp(desiredTop, minTop, maxTop);

            double dx = desiredLeft - Left;
            double dy = desiredTop - Top;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            // the 5 s window-stability wait happened before the target became valid, so
            // arriving means it is safe to settle down into the sitting pose
            bool calm = CalmForPose();

            if (InPose)
            {
                // sitting: glide onto the ledge line; stand back up when something happens
                double k = Math.Min(1.0, dt * 6.0);
                Left += dx * k;
                Top += dy * k;
                _perchActive = true;
                if (!calm) ExitPose();
            }
            else if (distance > 1.5)
            {
                double k = Math.Min(1.0, dt * 4.5);
                Left += dx * k;
                Top += dy * k;
                _perchActive = true;
                _perchSettle = 0;
                if (distance > 60 && _mode == PetState.Idle) SetState(PetState.Idle, 0);
            }
            else
            {
                Left = desiredLeft;
                Top = desiredTop;
                _perchActive = true;
                if (calm) _perchSettle += dt; else _perchSettle = 0;
                if (_perchSettle > 1.2 && _manifest.Poses.ContainsKey("sit")) EnterPose("sit");
            }

            if (_perchActive)
            {
                double targetCenter = _targetRect.Left + _targetRect.Width * 0.5;
                _facing = targetCenter < Left + Width * 0.5 ? -1.0 : 1.0;
                if (_lastTargetHwnd != _targetHwnd)
                {
                    _lastTargetHwnd = _targetHwnd;
                    _jumpOffset = -3;
                }
            }
        }

        void BuildBalanceBadge()
        {
            _balanceText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 60, 95)),
            };
            _balanceBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(150, 70, 100, 150)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 2, 7, 2),
                Child = _balanceText,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 8, 0),
                Opacity = 0,
                Visibility = Visibility.Collapsed,
            };
            _root.Children.Add(_balanceBadge);
        }

        static decimal ParseMoney(string raw)
        {
            decimal value;
            return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ? value : 0m;
        }

        void RefreshBalance()
        {
            if (_balanceBadge == null) return;
            try
            {
                if (!File.Exists(_options.BalanceFile))
                {
                    HideBalancePeek();
                    return;
                }
                var d = Json.Load(_options.BalanceFile, 64 * 1024);
                if (Json.Str(d, "status") != "ready")
                {
                    HideBalancePeek();
                    return;
                }
                object walletsObj;
                var wallets = d.TryGetValue("wallets", out walletsObj) ? walletsObj as object[] : null;
                if (wallets == null || wallets.Length == 0)
                {
                    HideBalancePeek();
                    return;
                }
                var wallet = wallets[0] as Dictionary<string, object>;
                string currency = Json.Str(wallet, "currency");
                string symbol = currency == "CNY" ? "¥" : currency == "USD" ? "$" : (currency + " ");
                decimal toppedUp = ParseMoney(Json.Str(wallet, "balance"));
                decimal granted = 0;
                object bonusObj;
                var bonus = d.TryGetValue("bonusWallets", out bonusObj) ? bonusObj as object[] : null;
                if (bonus != null)
                {
                    foreach (var item in bonus)
                    {
                        var w = item as Dictionary<string, object>;
                        if (w == null) continue;
                        granted += ParseMoney(Json.Str(w, "balance"));
                    }
                }
                decimal total = toppedUp + granted;
                _balanceText.Text = symbol + total.ToString("0.00", CultureInfo.InvariantCulture);
                _balanceDetails = "余额 " + symbol + total.ToString("0.00", CultureInfo.InvariantCulture)
                    + "（充值 " + symbol + toppedUp.ToString("0.00", CultureInfo.InvariantCulture)
                    + " + 赠送 " + symbol + granted.ToString("0.00", CultureInfo.InvariantCulture) + "）";
                // never pop the badge on a refresh; it only peeks in on interaction
                if (!_state.BalanceVisible) HideBalancePeek();
            }
            catch
            {
                HideBalancePeek();
            }
        }

        void UpdateBubble()
        {
            if (_bubble == null) return;
            if (_bubbleUntil > 0 && _t > _bubbleUntil)
            {
                _bubbleUntil = -1;
                _bubbleFadingOut = true;
            }
            if (_bubbleFadingOut)
            {
                _bubble.Opacity = Math.Max(0, _bubble.Opacity - 0.07);
                if (_bubble.Opacity <= 0)
                {
                    _bubbleFadingOut = false;
                    _bubble.Visibility = Visibility.Collapsed;
                    _bubbleText.Text = "";
                }
                return;
            }
            if (_bubble.Visibility == Visibility.Visible)
            {
                _bubble.Opacity = Math.Min(0.96, _bubble.Opacity + 0.12);
            }
        }

        // ------------------------------------------------------------ balance peek

        void ShowBalancePeek()
        {
            if (_balanceBadge == null || !_state.BalanceVisible) return;
            if (string.IsNullOrEmpty(_balanceText.Text)) return;
            // the speech bubble owns the screen while it is up (one text box at a time)
            if (BubbleVisible) return;
            _badgeFadingOut = false;
            _balanceBadge.Visibility = Visibility.Visible;
            _balanceBadge.Opacity = 1.0;
            _balanceUntil = _t + BalancePeekSeconds;
        }

        void HideBalancePeek()
        {
            _balanceUntil = -1;
            _badgeFadingOut = false;
            if (_balanceBadge == null) return;
            _balanceBadge.Opacity = 0;
            _balanceBadge.Visibility = Visibility.Collapsed;
        }

        void UpdateBalanceOverlay()
        {
            if (_balanceBadge == null) return;
            if (_balanceUntil > 0 && _t > _balanceUntil)
            {
                _balanceUntil = -1;
                _badgeFadingOut = true;
            }
            if (!_badgeFadingOut) return;
            _balanceBadge.Opacity = Math.Max(0, _balanceBadge.Opacity - 0.07);
            if (_balanceBadge.Opacity <= 0)
            {
                _badgeFadingOut = false;
                _balanceBadge.Visibility = Visibility.Collapsed;
            }
        }

        // ------------------------------------------------------------ input

        void StartWalk()
        {
            Rect wa = SystemParameters.WorkArea;
            double minLeft = wa.Left + 40 - _padX;
            double maxLeft = wa.Right - _artW - 40 - _padX;
            if (maxLeft <= minLeft + 80) return;
            double target = minLeft + _random.NextDouble() * (maxLeft - minLeft);
            for (int i = 0; i < 6 && Math.Abs(target - Left) < 100; i++)
                target = minLeft + _random.NextDouble() * (maxLeft - minLeft);
            _walkTargetLeft = target;
            _walkDir = target >= Left ? 1 : -1;
            _facing = _walkDir >= 0 ? 1.0 : -1.0;
            _walkPhase = 0;
            SetState(PetState.Walk, 0);
            if (_random.NextDouble() < 0.4) Say("散散步～");
        }

        void UpdateWalk(double dt)
        {
            double speed = 62;
            Left += _walkDir * speed * dt;
            _walkPhase += dt;
            _walkBob = -Math.Abs(Math.Sin(_walkPhase * 8.0)) * 4.0;
            _walkTilt = _walkDir * 2.0;
            if ((_walkDir > 0 && Left >= _walkTargetLeft) || (_walkDir < 0 && Left <= _walkTargetLeft) || _walkPhase > 9)
            {
                Left = _walkTargetLeft;
                _nextWanderAt = _t + 9 + _random.NextDouble() * 16;
                _facing = 1.0;
                SetState(PetState.Idle, 0);
            }
        }

        void Say(string text)
        {
            // autonomous small talk: never refresh a bubble that is already on screen
            if (BubbleVisible) return;
            if (_t < _nextBubbleAt) return;
            ShowBubble(text, ChatterBubbleSeconds);
        }

        void SayRandom(string[] lines)
        {
            if (lines == null || lines.Length == 0) return;
            Say(lines[_random.Next(lines.Length)]);
        }

        string Pick(string[] lines)
        {
            return lines[_random.Next(lines.Length)];
        }

        void SayNow(string text, double holdSeconds = BubbleHoldSeconds)
        {
            ShowBubble(text, holdSeconds);
        }

        void OnLeftDown(object sender, MouseButtonEventArgs e)
        {
            POINT cp;
            GetCursorPos(out cp);
            _dragStartCursorX = cp.X;
            _dragStartCursorY = cp.Y;
            _dragFromLeft = Left;
            _dragFromTop = Top;
            _prevDragLeft = Left;
            _dragVelX = 0;
            _perchCooldown = _t + 10;
            _lastInteraction = _t;
            if (_mode == PetState.Walk) SetState(PetState.Idle, 0);
            _moved = false;
            _dragging = true;
            CaptureMouse();
            e.Handled = true;
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging)
            {
                // hover over the pet: let the balance badge peek in, but throttle it so
                // it still fades out about ten seconds after the cursor stops
                if (_t - _lastHoverPeek > 4)
                {
                    _lastHoverPeek = _t;
                    ShowBalancePeek();
                }
                return;
            }
            POINT cp;
            if (!GetCursorPos(out cp)) return;
            double dx = (cp.X - _dragStartCursorX) / _dpiScale;
            double dy = (cp.Y - _dragStartCursorY) / _dpiScale;
            if (!_moved && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
            {
                _moved = true;
                // A full-body pose (lying in the corner / sitting on a ledge) cannot be
                // carried: pop back to the normal full body first, then re-anchor the drag
                // at the hand's current position so nothing jumps.
                if (InPose)
                {
                    if (_cornerRest) EndCornerRest(); else ExitPose();
                    _dragFromLeft = Left;
                    _dragFromTop = Top;
                    _dragStartCursorX = cp.X;
                    _dragStartCursorY = cp.Y;
                    dx = 0;
                    dy = 0;
                }
                SetState(PetState.Drag, 0);
                SayNow(Pick(new[] { "哇——", "轻点轻点～", "要带我去哪？" }), ReactionBubbleSeconds);
                ShowBalancePeek();
            }
            if (!_moved) return;
            Left = _dragFromLeft + dx;
            Top = _dragFromTop + dy;
            ClampToWorkArea();
            _lastInteraction = _t;
            _tiltDrag = Clamp(-dx * 0.02, -8, 8);
            double vx = (Left - _prevDragLeft) * 60.0;
            _prevDragLeft = Left;
            _dragVelX = _dragVelX * 0.5 + vx * 0.5;
            if (_dragVelX > 900) _dragVelX = 900;
            if (_dragVelX < -900) _dragVelX = -900;
            e.Handled = true;
        }

        void OnLeftUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            if (_moved)
            {
                _tiltDrag = 0;
                _dragVelX = 0;
                _jumpOffset = -4;
                SetState(PetState.Idle, 0);
                // remember the drop position and what the foreground looked like: she
                // stays there for DragHoldSeconds unless the foreground changes
                _dragReleasedAt = _t;
                _dragFgHwnd = _fgStateHwnd;
                _dragFgRect = _fgStateRect;
                _dragFgHaveRect = _fgStateHaveRect;
                _dragFgFullscreen = _fgFullscreen;
                ScheduleSave();
            }
            else
            {
                double now = _t;
                bool doubleClick = now - _lastClickTime < 0.35 &&
                                   Math.Abs(Left - _lastClickX) < 8 && Math.Abs(Top - _lastClickY) < 8;
                _lastClickTime = now;
                _lastClickX = Left;
                _lastClickY = Top;
                if (doubleClick)
                {
                    SetState(PetState.Celebrate, _t + 1.4);
                    StartTwirl();
                    ShowBubble("好耶！", ReactionBubbleSeconds);
                }
                else
                {
                    SetState(PetState.Poke, _t + 1.1);
                    SpawnBurst("♥", 4, 0.6);
                    ShowBalancePeek();
                    if (_random.NextDouble() < 0.4) ShowBubble("呼哇…", ReactionBubbleSeconds);
                }
            }
            e.Handled = true;
        }

        void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            ShowMenu();
            e.Handled = true;
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) { e.Handled = false; return; }
            double factor = e.Delta > 0 ? 1.08 : 1 / 1.08;
            double next = Clamp(_artW * factor, 100, 900) / _manifest.Canvas.Width / _manifest.Canvas.BaseScale;
            ApplyScale(next, true);
            e.Handled = true;
        }

        void ApplyScale(double scale, bool center)
        {
            double oldCenterX = Left + Width / 2;
            double oldArtBottom = Top + Height - _padBottom;
            _state.Scale = Clamp(scale, 0.4, 2.2);
            ApplyGeometry(_manifest.Canvas.BaseScale * _state.Scale);
            SyncDecodeWidth();
            if (center)
            {
                Left = oldCenterX - Width / 2;
                Top = oldArtBottom - (Height - _padBottom);
            }
            ClampToWorkArea();
            ScheduleSave();
        }

        void ClampToWorkArea()
        {
            Rect wa = SystemParameters.WorkArea;
            if (wa.Width <= 0 || wa.Height <= 0) return;
            if (_poseMode == "peek")
            {
                // the peek pose intentionally hangs below the work area; only clamp x
                Left = Clamp(Left, wa.Left + 2 - _padX, Math.Max(wa.Left + 2 - _padX, wa.Right - _artW - 2 - _padX));
                return;
            }
            // clamp the artwork, not the window: the transparent padding may hang off
            // the screen so she can still be tucked into a corner
            double minLeft = wa.Left + 2 - _padX;
            double maxLeft = Math.Max(minLeft, wa.Right - _artW - 2 - _padX);
            Left = Clamp(Left, minLeft, maxLeft);
            double minTop = wa.Top + 2 - _padTop;
            double maxTop = Math.Max(minTop, wa.Bottom - _artH - 2 - _padTop);
            Top = Clamp(Top, minTop, maxTop);
        }

        // ------------------------------------------------------------ menu

        void ShowMenu()
        {
            var menu = new ContextMenu { PlacementTarget = this };
            var top = new MenuItem { Header = "始终置顶", IsCheckable = true, IsChecked = Topmost };
            top.Click += (s, e) =>
            {
                _topmost = top.IsChecked;
                Topmost = _topmost;
                _state.Topmost = _topmost;
                ScheduleSave();
                ApplyClickThrough();
                if (_topmost) EnsureTopmost();   // re-enter the band right away, not 0.5 s later
            };
            menu.Items.Add(top);

            var through = new MenuItem { Header = "点击穿透（Ctrl+Alt+P）", IsCheckable = true, IsChecked = _clickThrough };
            through.Click += (s, e) => ToggleClickThrough();
            menu.Items.Add(through);

            {
                var awareToggle = new MenuItem { Header = "跟随前台窗口", IsCheckable = true, IsChecked = _windowAware };
                awareToggle.Click += (s, e) =>
                {
                    _windowAware = awareToggle.IsChecked;
                    _state.WindowAware = _windowAware;
                    if (!_windowAware) { _targetValid = false; _perchActive = false; }
                    ScheduleSave();
                };
                menu.Items.Add(awareToggle);
                var awareInfo = new MenuItem { Header = "查看前台窗口" };
                awareInfo.Click += (s, e) =>
                {
                    if (_targetValid)
                        ShowBubble("前台：" + _targetTitle + "\n" + (int)_targetRect.Width + " × " + (int)_targetRect.Height, 20);
                    else
                        ShowBubble("没有可跟随的前台窗口", 20);
                };
                menu.Items.Add(awareInfo);
            }

            if (_balanceBadge != null)
            {
                var balanceToggle = new MenuItem { Header = "显示余额徽标", IsCheckable = true, IsChecked = _state.BalanceVisible };
                balanceToggle.Click += (s, e) =>
                {
                    _state.BalanceVisible = balanceToggle.IsChecked;
                    if (_state.BalanceVisible) ShowBalancePeek();
                    else HideBalancePeek();
                    ScheduleSave();
                };
                menu.Items.Add(balanceToggle);
                var balanceShow = new MenuItem { Header = "查看余额（悬浮 / 点她也看得到）" };
                balanceShow.Click += (s, e) =>
                {
                    ShowBubble(string.IsNullOrEmpty(_balanceDetails) ? "余额暂不可用" : _balanceDetails, 20);
                    ShowBalancePeek();
                };
                menu.Items.Add(balanceShow);
            }

            menu.Items.Add(new Separator());
            foreach (var pct in new[] { 0.6, 0.8, 1.0, 1.2 })
            {
                var mi = new MenuItem { Header = "大小 " + (int)(pct * 100) + "%", IsCheckable = true, IsChecked = Math.Abs(_state.Scale - pct) < 0.01 };
                double p = pct;
                mi.Click += (s, e) => ApplyScale(p, true);
                menu.Items.Add(mi);
            }
            menu.Items.Add(new Separator());
            var reset = new MenuItem { Header = "回到右下角" };
            reset.Click += (s, e) =>
            {
                Rect wa = SystemParameters.WorkArea;
                ApplyScale(0.8, false);
                Left = wa.Right - Width - 24 + _padX;
                Top = wa.Bottom - Height - 12 + _padBottom;
                ScheduleSave();
            };
            menu.Items.Add(reset);
            menu.Items.Add(new Separator());
            var quit = new MenuItem { Header = "退出桌宠" };
            quit.Click += (s, e) => { _userClosed = true; Close(); };
            menu.Items.Add(quit);
            menu.IsOpen = true;
        }

        // ------------------------------------------------------------ click-through

        void ToggleClickThrough()
        {
            _clickThrough = !_clickThrough;
            _state.ClickThrough = _clickThrough;
            ApplyClickThrough();
            ScheduleSave();
        }

        void ApplyClickThrough()
        {
            if (_source == null) return;
            int ex = GetWindowLong(_source.Handle, GWL_EXSTYLE);
            ex |= WS_EX_NOACTIVATE;
            if (_clickThrough) ex |= WS_EX_TRANSPARENT;
            else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLong(_source.Handle, GWL_EXSTYLE, ex);
        }

        // ------------------------------------------------------------ topmost watchdog

        /// <summary>
        /// True when some visible, plausibly-painted window that is NOT itself topmost sits
        /// above <paramref name="me"/> and overlaps it. Such a window is proof that the pet
        /// has been pushed out of the topmost band.
        /// </summary>
        bool HasNonTopmostCover(IntPtr me)
        {
            RECT mine;
            if (!GetWindowRect(me, out mine)) return false;
            IntPtr above = GetWindow(me, GW_HWNDPREV);
            for (int guard = 0; above != IntPtr.Zero && guard < 64; guard++)
            {
                if (above != me && IsWindowVisible(above))
                {
                    RECT r;
                    if (GetWindowRect(above, out r))
                    {
                        // 1x1 helper windows (thumbnail hosts, IME proxies, DWM stubs) are
                        // "visible" but never paint over anything: ignore them.
                        bool real = (r.Right - r.Left) > 4 && (r.Bottom - r.Top) > 4;
                        bool overlaps = r.Left < mine.Right && r.Right > mine.Left &&
                                        r.Top < mine.Bottom && r.Bottom > mine.Top;
                        if (real && overlaps)
                            return (GetWindowLong(above, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0;
                    }
                }
                above = GetWindow(above, GW_HWNDPREV);
            }
            return false;
        }

        /// <summary>
        /// Put her back above ordinary windows after something knocked her out of the
        /// topmost band.
        ///
        /// WS_EX_TOPMOST on its own is not a promise: another process calling
        /// SetWindowPos(HWND_NOTOPMOST), a shell/DWM transition, or a window manager that
        /// re-stacks the band can drop the pet below normal windows. WPF only re-asserts
        /// Topmost when the property value changes, so once she is demoted she stays buried
        /// under every ordinary window until the host restarts — which looks exactly like
        /// "the pet's layer got lower".
        ///
        /// The band bit is the authoritative signal (Windows clears it on demotion), and the
        /// overlap scan catches the rarer case where the bit survives but the stacking is
        /// wrong. Only a NON-topmost cover counts, so she never fights the taskbar or another
        /// always-on-top app for the band.
        /// </summary>
        void EnsureTopmost()
        {
            if (_source == null || !_topmost || _userClosed) return;
            IntPtr me = _source.Handle;
            if (me == IntPtr.Zero) return;

            bool need = (GetWindowLong(me, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0 || HasNonTopmostCover(me);
            if (!need) return;
            SetWindowPos(me, HWND_TOPMOST, 0, 0, 0, 0, SWP_REASSERT);
        }

        // ------------------------------------------------------------ window hook

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                handled = true;
                if (_clickThrough) return new IntPtr(HTTRANSPARENT);
                long lp = lParam.ToInt64();
                int sx = (short)(lp & 0xFFFF);
                int sy = (short)((lp >> 16) & 0xFFFF);
                Point local = ScreenPixelsToLocal(sx, sy);
                bool hit = false;
                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    if (_layers[i].HitTest(local, 40)) { hit = true; break; }
                }
                return new IntPtr(hit ? HTCLIENT : HTTRANSPARENT);
            }
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == 1) { ToggleClickThrough(); handled = true; }
                else if (id == 2) { _userClosed = true; Close(); handled = true; }
            }
            if (msg == WM_WINDOWPOSCHANGING && _topmost && !_userClosed)
            {
                // Refuse to leave the topmost band in the first place. A window that is
                // re-stacked by someone else (HWND_NOTOPMOST, a shell transition, a window
                // manager reshuffling the band) gets one last chance to keep its promise
                // here, which is cheaper and smoother than waiting for EnsureTopmost to
                // notice. WPF writes SWP_NOZORDER in its own SetWindowPos calls, so
                // ordinary moves/resizes are untouched; only an actual z-order change is
                // intercepted.
                var wp = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
                if ((wp.flags & SWP_NOZORDER_BIT) == 0 && wp.hwndInsertAfter != HWND_TOPMOST)
                {
                    wp.hwndInsertAfter = HWND_TOPMOST;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20;
        const int WS_EX_TOPMOST = 0x8;
        const int WS_EX_NOACTIVATE = 0x08000000;

        // WM_WINDOWPOSCHANGING payload: lets us veto a demotion instead of repairing it.
        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }
        const int WM_WINDOWPOSCHANGING = 0x0046;
        const uint SWP_NOZORDER_BIT = 0x0004;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _source = PresentationSource.FromVisual(this) as HwndSource;
            if (_source != null)
            {
                _source.AddHook(WndProc);
                RegisterHotKey(_source.Handle, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_P);
                RegisterHotKey(_source.Handle, 2, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_Q);
            }
            _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            SyncDecodeWidth();
            ApplyClickThrough();
        }

        double GetSystemIdleSeconds()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref info)) return 0;
            uint tick = (uint)Environment.TickCount;
            return ((uint)(tick - info.dwTime)) / 1000.0;
        }

        Point ScreenPixelsToLocal(int px, int py)
        {
            double dx = px / _dpiScale;
            double dy = py / _dpiScale;
            return new Point(dx - Left, dy - Top);
        }

        static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        // ------------------------------------------------------------ decode size

        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        // Caption button metrics (physical pixels): the strip she must not cover when
        // perching on the top-right corner of a window.
        const int SM_CXSIZE = 30;            // width of a caption button
        const int SM_CXPADDEDBORDER = 92;    // extra frame padding around it

        /// <summary>Monitor scale factor before the window exists (system DPI aware app).</summary>
        static double DetectDpiScale()
        {
            try
            {
                int phys = GetSystemMetrics(0);
                double dip = SystemParameters.PrimaryScreenWidth;
                if (phys > 0 && dip > 0)
                {
                    double s = phys / dip;
                    if (s >= 0.5 && s <= 4.0) return s;
                }
            }
            catch { }
            return 1.0;
        }

        /// <summary>Bitmap decode width that keeps the artwork pixel-sharp at the drawn size.</summary>
        int WantedDecodeWidth(double artScale, double dpi)
        {
            if (dpi < 0.1) dpi = 1.0;
            double artPx = _manifest.Canvas.Width * artScale * dpi;
            int width = (int)Math.Ceiling(artPx * 1.02);
            if (width < 512) width = 512;
            if (width > 1024) width = 1024;
            return width;
        }

        void ReloadLayerBitmaps()
        {
            foreach (var layer in _layers) layer.Reload();
            if (_eyeRig != null) _eyeRig.Reload();
            foreach (var kv in _poseVisuals)
            {
                PoseSpec pose;
                if (_manifest.Poses.TryGetValue(kv.Key, out pose))
                    kv.Value.Source = PetLayer.LoadBitmap(pose.Image);
            }
        }

        /// <summary>QA hook: exercise the re-decode path that a live zoom change uses.</summary>
        public void QaReloadAtDecode(int width)
        {
            if (width < 64) return;
            PetLayer.DecodeWidth = width;
            ReloadLayerBitmaps();
            ApplyGeometry(_drawnScale);
        }

        /// <summary>QA hook: force a pose so an offscreen dump can inspect it.</summary>
        public void QaForceMode(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return;
            switch (mode)
            {
                case "walk":
                    _lastInteraction = _t;
                    _lastActivity = _t;
                    StartWalk();
                    break;
                case "drag":
                    SetState(PetState.Drag, _t + 5.0);
                    _dragVelX = 180;
                    break;
                case "poke":
                    SetState(PetState.Poke, _t + 5.0);
                    break;
                case "idle":
                    SetState(PetState.Idle, 0);
                    break;
                case "sit":
                    _perchCooldown = _t + 3600;   // freeze the perch controller for the dump
                    EnterPose("sit");
                    break;
                case "peek":
                    if (_poseMode != "peek")
                    {
                        _perchCooldown = _t + 3600;
                        EnterPose("peek");
                        Rect wa = SystemParameters.WorkArea;
                        Top = wa.Bottom - _padTop - PoseLedge("peek") + 2;
                        _peekUntil = 0;
                    }
                    break;
                case "stand":
                    ExitPose();
                    break;
                case "perch-sit":
                    // let the real perch controller place her on the foreground window and
                    // settle into the sitting pose (deep-idle timers backdated)
                    _lastInteraction = -100;
                    _lastActivity = -100;
                    _lastForegroundChange = -100;
                    _fgStateSince = _t - 100;
                    _agentBusy = false;
                    _perchCooldown = 0;
                    break;
                case "corner":
                    // fullscreen / maximized foreground: she must end up lying low in the
                    // bottom-right corner instead of climbing to the screen's top-right
                    _qaPinForeground = true;
                    _fgFullscreen = true;
                    _fgStateSince = _t - 100;
                    _lastInteraction = -100;
                    _lastActivity = -100;
                    _lastForegroundChange = -100;
                    _agentBusy = false;
                    _perchCooldown = 0;
                    break;
            }
            UpdatePose();
        }

        /// <summary>QA hook: append a machine-readable foreground / perch trace line.</summary>
        void QaTrace()
        {
            if (string.IsNullOrEmpty(_options.QaTrace)) return;
            if (_t - _lastQaTrace < 0.5) return;
            _lastQaTrace = _t;
            try
            {
                Func<double, string> n = v => v.ToString("0.##", CultureInfo.InvariantCulture);
                string fgTitle = "";
                if (!string.IsNullOrEmpty(_options.QaFgFile)) fgTitle = "qa-sim";
                else if (!_qaPinForeground && _fgStateHwnd != IntPtr.Zero)
                {
                    var tb = new StringBuilder(128);
                    GetWindowText(_fgStateHwnd, tb, tb.Capacity);
                    fgTitle = tb.ToString();
                }
                var sb = new StringBuilder();
                sb.Append("t=").Append(n(_t))
                  .Append(" pet=").Append(n(Left)).Append(',').Append(n(Top))
                  .Append(" mode=").Append(_mode)
                  .Append(" pose=").Append(string.IsNullOrEmpty(_poseMode) ? "-" : _poseMode)
                  .Append(" full=").Append(_fgFullscreen)
                  .Append(" stateAge=").Append(n(_t - _fgStateSince))
                  .Append(" target=").Append(_targetValid)
                  .Append(" rect=").Append(n(_targetRect.Left)).Append(',').Append(n(_targetRect.Top))
                  .Append(',').Append(n(_targetRect.Right)).Append(',').Append(n(_targetRect.Bottom))
                  .Append(" perch=").Append(_perchActive)
                  .Append(" rest=").Append(_cornerRest)
                  .Append(" restArea=").Append(n(_fgRestArea.Left)).Append(',').Append(n(_fgRestArea.Top))
                  .Append(',').Append(n(_fgRestArea.Right)).Append(',').Append(n(_fgRestArea.Bottom))
                  .Append(" gaze=").Append(n(_gazeNx)).Append(',').Append(n(_gazeNy))
                  .Append(" cursorAge=").Append(n(_t - _lastCursorMoveAt))
                  .Append(" fg=\"").Append(fgTitle).Append('"');
                File.AppendAllText(_options.QaTrace, sb.ToString() + "\n", Encoding.UTF8);
            }
            catch { }
        }

        void SyncDecodeWidth()
        {
            int wanted = WantedDecodeWidth(_drawnScale, _dpiScale);
            if (Math.Abs(wanted - PetLayer.DecodeWidth) < 24) return;
            PetLayer.DecodeWidth = wanted;
            ReloadLayerBitmaps();
        }

        // ------------------------------------------------------------ geometry

        // Lay the artwork out inside a window that is slightly larger than the canvas,
        // so tails, hair and tilt have room to move without hitting the window edge.
        void ApplyGeometry(double scale)
        {
            double artW = Math.Max(60, _manifest.Canvas.Width * scale);
            double artH = Math.Max(60, _manifest.Canvas.Height * scale);
            _drawnScale = scale;
            _artW = artW;
            _artH = artH;
            _padX = artW * PadXRatio;
            _padTop = artH * PadTopRatio;
            _padBottom = artH * PadBottomRatio;

            double winW = artW + _padX * 2;
            double winH = artH + _padTop + _padBottom;
            Width = winW;
            Height = winH;
            _layerCanvas.Width = winW;
            _layerCanvas.Height = winH;
            _fxCanvas.Width = winW;
            _fxCanvas.Height = winH;

            foreach (var layer in _layers)
            {
                layer.Visual.Width = artW;
                layer.Visual.Height = artH;
                layer.Offset = new Point(_padX, _padTop);
                Canvas.SetLeft(layer.Visual, _padX);
                Canvas.SetTop(layer.Visual, _padTop);
            }
            if (_eyeRig != null)
            {
                _eyeRig.Layout(artW, artH);
                Canvas.SetLeft(_eyeRig.Root, _padX);
                Canvas.SetTop(_eyeRig.Root, _padTop);
            }
            foreach (var poseVisual in _poseVisuals.Values)
            {
                poseVisual.Width = artW;
                poseVisual.Height = artH;
                Canvas.SetLeft(poseVisual, _padX);
                Canvas.SetTop(poseVisual, _padTop);
            }

            if (_bubble != null)
            {
                // keep the bubble inside the artwork's width: the pet often perches near
                // the edge of the screen, and a wider box would be sliced by the screen
                // edge (long lines wrap instead of being cut)
                _bubble.MaxWidth = Math.Max(120, _artW);
                _bubbleText.MaxWidth = Math.Max(96, _artW - 20);
                _bubbleText.TextWrapping = TextWrapping.Wrap;
                _bubbleText.TextAlignment = TextAlignment.Center;
            }
            if (_balanceBadge != null)
            {
                _balanceBadge.Margin = new Thickness(0, _padTop + 6, _padX + 8, 0);
            }
        }

        void RestorePosition()
        {
            Rect wa = SystemParameters.WorkArea;
            double left, top;
            if (!double.IsNaN(_state.Left) && !double.IsNaN(_state.Top))
            {
                left = _state.Left;
                top = _state.Top;
            }
            else
            {
                // bottom-right corner of the artwork, clear of the taskbar
                left = wa.Right - _artW - 24 - _padX;
                top = wa.Bottom - _artH - 12 - _padTop;
            }
            Left = left;
            Top = top;
            ClampToWorkArea();
        }

        void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        void WriteClosedMarker()
        {
            try
            {
                string dir = Path.GetDirectoryName(_options.StateFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "pet.closed"), DateTime.UtcNow.ToString("o"), Encoding.UTF8);
            }
            catch { }
        }

        void WriteDiag()
        {
            if (string.IsNullOrEmpty(_options.Diag)) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("window=" + Width + "x" + Height);
                sb.AppendLine("art=" + _artW + "x" + _artH + " pad=" + _padX + "/" + _padTop + "/" + _padBottom);
                sb.AppendLine("canvas=" + _manifest.Canvas.Width + "x" + _manifest.Canvas.Height);
                foreach (var l in _layers)
                    sb.AppendLine("layer " + l.Spec.Id + " z=" + l.Spec.Z + " variants=" + l.Variants.Count);
                File.WriteAllText(_options.Diag, sb.ToString());
            }
            catch { }
        }

        // ---- QA helper: render the live visual tree to a PNG without showing the window.
        // Used to inspect exactly what the host composits (seams cannot be blamed on the
        // desktop screenshot pipeline this way).
        public void SaveFrame(string path)
        {
            _root.UpdateLayout();
            double dpi = 1.0;
            var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null) dpi = src.CompositionTarget.TransformToDevice.M11;
            if (dpi <= 0.05) dpi = 1.0;
            int w = (int)Math.Ceiling(Math.Max(1.0, Width) * dpi);
            int h = (int)Math.Ceiling(Math.Max(1.0, Height) * dpi);
            var bmp = new RenderTargetBitmap(w, h, 96.0 * dpi, 96.0 * dpi, PixelFormats.Pbgra32);
            bmp.Render(_root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
            // QA sidecar: where the window sat and which controller was in charge, so an
            // offscreen dump can also prove the placement rules (corner rest / perch wait)
            try
            {
                Func<double, string> num = v => v.ToString("0.###", CultureInfo.InvariantCulture);
                var sb = new StringBuilder();
                sb.Append("{\"left\":").Append(num(Left))
                  .Append(",\"top\":").Append(num(Top))
                  .Append(",\"width\":").Append(num(Width))
                  .Append(",\"height\":").Append(num(Height))
                  .Append(",\"pose\":\"").Append(_poseMode).Append('"')
                  .Append(",\"cornerRest\":").Append(_cornerRest ? "true" : "false")
                  .Append(",\"fgFullscreen\":").Append(_fgFullscreen ? "true" : "false")
                  .Append(",\"perchActive\":").Append(_perchActive ? "true" : "false")
                  .Append(",\"targetValid\":").Append(_targetValid ? "true" : "false")
                  .Append(",\"stableSeconds\":").Append(num(_t - _fgStateSince))
                  .Append(",\"mode\":\"").Append(_mode).Append('"')
                  .Append('}');
                File.WriteAllText(path + ".json", sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (Exception ex)
            {
                LogCrash(ex, args);
                MessageBox.Show(ex.Message, "DSH Pet", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool dump = !string.IsNullOrEmpty(options.DumpFrame) || options.QaInstance;
            bool created = true;
            Mutex instanceMutex = null;
            try
            {
                // In dump mode the single-instance guard is skipped so QA can render a
                // frame from a pet pack while the user's pet keeps running.
                if (!dump)
                {
                    instanceMutex = new Mutex(true, "Local\\DshPetHost", out created);
                    if (!created) return;
                }

                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    LogCrash(e.Exception, args);
                    e.Handled = true;
                    app.Shutdown();
                };
                PetWindow window;
                try
                {
                    window = new PetWindow(options);
                }
                catch (Exception ex)
                {
                    LogCrash(ex, args);
                    MessageBox.Show("无法加载宠物包：\n" + ex.Message, "DSH Pet", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (options.QaInstance && !string.IsNullOrEmpty(options.QaMode))
                {
                    // keep-alive QA instance: apply the forced pose shortly after loading
                    var qaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                    qaTimer.Tick += (s3, e3) => { qaTimer.Stop(); window.QaForceMode(options.QaMode); };
                    qaTimer.Start();
                }
                if (!string.IsNullOrEmpty(options.DumpFrame))
                {
                    window.Opacity = 0;
                    if (options.ReloadDecode > 0) window.QaReloadAtDecode(options.ReloadDecode);
                    if (!string.IsNullOrEmpty(options.QaMode)) window.QaForceMode(options.QaMode);
                    var dumpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(80, options.DumpDelayMs)) };
                    dumpTimer.Tick += (s2, e2) =>
                    {
                        dumpTimer.Stop();
                        try { window.SaveFrame(options.DumpFrame); }
                        catch (Exception ex) { LogCrash(ex, args); }
                        window.Close();
                    };
                    dumpTimer.Start();
                }
                app.Run(window);
            }
            finally
            {
                if (instanceMutex != null) instanceMutex.Dispose();
            }
        }

        static void LogCrash(Exception ex, string[] args)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dsh-pet");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    DateTime.UtcNow.ToString("o") + " args=[" + string.Join(" ", args) + "]\n" + ex + "\n\n",
                    Encoding.UTF8);
            }
            catch { }
        }
    }
}





































