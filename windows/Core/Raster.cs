using SkiaSharp;

namespace Compositor.Core;

public static class Raster {
    public const long PixelLimit = 64_000_000;
    public static readonly IReadOnlyDictionary<string, SKBlendMode> Blends = new Dictionary<string, SKBlendMode> {
        ["Normal"] = SKBlendMode.SrcOver, ["Multiply"] = SKBlendMode.Multiply, ["Screen"] = SKBlendMode.Screen,
        ["Overlay"] = SKBlendMode.Overlay, ["Darken"] = SKBlendMode.Darken, ["Lighten"] = SKBlendMode.Lighten,
        ["Difference"] = SKBlendMode.Difference, ["Exclusion"] = SKBlendMode.Exclusion
    };
    public static void CheckSize(int width, int height) {
        if (width < 1 || height < 1 || width > 30_000 || height > 30_000 || (long)width * height > PixelLimit)
            throw new OperationException(ErrorCode.DocumentTooLarge, "每边最多 30,000 像素，画布/源像素预算 6400 万。");
    }
    public static (byte[] Png, int Width, int Height) Import(byte[] bytes, bool pngOnly = false) {
        using var stream = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(stream);
        if (codec == null || (pngOnly && codec.EncodedFormat != SKEncodedImageFormat.Png) ||
            (!pngOnly && codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg)))
            throw new OperationException(ErrorCode.MissingAsset, "首版仅支持 PNG/JPEG 导入，项目资源必须为 PNG。");
        CheckSize(codec.Info.Width, codec.Info.Height);
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap == null) throw new OperationException(ErrorCode.MissingAsset);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return (data.ToArray(), bitmap.Width, bitmap.Height);
    }
    public static byte[] Render(DocumentData doc, int maxSide = 0, bool checkerboard = false) {
        CheckSize(doc.Width, doc.Height);
        var scale = maxSide > 0 ? Math.Min(1, (double)maxSide / Math.Max(doc.Width, doc.Height)) : 1;
        using var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, (int)Math.Ceiling(doc.Width * scale)), Math.Max(1, (int)Math.Ceiling(doc.Height * scale)), SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        if (surface == null) throw new OperationException(ErrorCode.DocumentTooLarge);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale((float)scale);
        if (checkerboard) {
            using var paint = new SKPaint();
            for (var y = 0; y < doc.Height; y += 24) for (var x = 0; x < doc.Width; x += 24) {
                paint.Color = (x / 24 + y / 24) % 2 == 0 ? new SKColor(72, 75, 83) : new SKColor(59, 62, 69);
                canvas.DrawRect(x, y, 24, 24, paint);
            }
        }
        var map = doc.Layers.ToDictionary(l => l.Id);
        IEnumerable<LayerData> InDrawOrder(Guid? parent) {
            foreach (var item in doc.Layers.Where(l => l.Parent == parent)) {
                if (item.IsGroup) { foreach (var child in InDrawOrder(item.Id)) yield return child; }
                else yield return item;
            }
        }
        foreach (var layer in InDrawOrder(null).Where(l => l.Png != null)) {
            var opacity = layer.Opacity; var visible = layer.Visible; var parent = layer.Parent;
            while (parent is Guid id) { var p = map[id]; opacity *= p.Opacity; visible &= p.Visible; parent = p.Parent; }
            if (!visible || opacity <= 0) continue;
            using var image = SKImage.FromEncodedData(layer.Png);
            if (image == null) throw new OperationException(ErrorCode.MissingAsset, layer.Name);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)), BlendMode = Blends[layer.Blend], IsAntialias = true };
            canvas.Save();
            canvas.Translate((float)(layer.OriginX + layer.Width / 2), (float)(layer.OriginY + layer.Height / 2));
            canvas.RotateDegrees((float)layer.Rotation);
            canvas.Scale(layer.FlipX ? -1 : 1, layer.FlipY ? -1 : 1);
            var sampling = layer.Transform["sampling"]?.GetValue<string>() == "Nearest" ? SKFilterMode.Nearest : SKFilterMode.Linear;
            canvas.DrawImage(image, SKRect.Create((float)-layer.Width / 2, (float)-layer.Height / 2, (float)layer.Width, (float)layer.Height), new SKSamplingOptions(sampling), paint);
            canvas.Restore();
        }
        using var snapshot = surface.Snapshot(); using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
    public static byte[] SolidPng(int width, int height, byte r, byte g, byte b) {
        using var surface = SKSurface.Create(new SKImageInfo(width, height)); surface.Canvas.Clear(new SKColor(r, g, b));
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    public static (byte R, byte G, byte B, byte A) Pixel(byte[] bytes, int x, int y) {
        using var bitmap = SKBitmap.Decode(bytes); var p = bitmap.GetPixel(x, y); return (p.Red, p.Green, p.Blue, p.Alpha);
    }
}
