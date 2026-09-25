using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: AuvryxelIcon <output.ico>");
    return 2;
}

var outputPath = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
var frames = sizes.Select(CreatePng).ToArray();
using var output = File.Create(outputPath);
using var writer = new BinaryWriter(output);
writer.Write((ushort)0); // ICONDIR reserved
writer.Write((ushort)1); // ICO image type
writer.Write((ushort)frames.Length);
var imageOffset = 6 + 16 * frames.Length;
for (var i = 0; i < frames.Length; i++)
{
    var size = sizes[i];
    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)0); // palette
    writer.Write((byte)0); // reserved
    writer.Write((ushort)1); // planes
    writer.Write((ushort)32); // bits per pixel
    writer.Write((uint)frames[i].Length);
    writer.Write((uint)imageOffset);
    imageOffset += frames[i].Length;
}
foreach (var frame in frames) writer.Write(frame);
Console.WriteLine($"Wrote {sizes.Length} icon sizes to {outputPath}");
return 0;

static byte[] CreatePng(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / 512d, size / 512d));
        var background = new SolidColorBrush(Color.FromRgb(17, 26, 24));
        background.Freeze();
        dc.DrawRoundedRectangle(background, null, new Rect(0, 0, 512, 512), 124, 124);

        var halo = new RadialGradientBrush
        {
            Center = new Point(.5, .5),
            GradientOrigin = new Point(.5, .5),
            RadiusX = .5,
            RadiusY = .5
        };
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(87, 57, 127, 112), 0));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, 23, 36, 32), 1));
        halo.Freeze();
        dc.DrawEllipse(halo, null, new Point(256, 250), 180, 180);

        var aurora = new LinearGradientBrush
        {
            StartPoint = new Point(92, 408),
            EndPoint = new Point(413, 108),
            MappingMode = BrushMappingMode.Absolute
        };
        aurora.GradientStops.Add(new GradientStop(Color.FromRgb(114, 228, 192), 0));
        aurora.GradientStops.Add(new GradientStop(Color.FromRgb(160, 232, 214), .55));
        aurora.GradientStops.Add(new GradientStop(Color.FromRgb(181, 200, 255), 1));
        aurora.Freeze();
        var markPen = new Pen(aurora, 28) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        markPen.Freeze();
        dc.DrawGeometry(null, markPen, Geometry.Parse("M125,373 L246,143 C250,135 262,135 266,143 L387,373"));
        dc.DrawGeometry(null, new Pen(aurora, 24) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M178,276 L334,276"));

        var star = new SolidColorBrush(Color.FromRgb(213, 255, 241));
        star.Freeze();
        dc.DrawGeometry(star, null, Geometry.Parse("M248,147 L258,112 L269,147 L304,158 L269,169 L258,204 L248,169 L213,158 Z"));
        dc.Pop();
    }

    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    bitmap.Freeze();
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
}
