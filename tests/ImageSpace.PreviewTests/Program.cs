using System.Text.Json;
using ImageSpace.Skia;
using SkiaSharp;

var tests = new List<(string Name, Action Body)>();
void Test(string name, Action body) => tests.Add((name, body));
void Check(bool value, string message = "Assertion failed")
{
    if (!value) throw new InvalidOperationException(message);
}
SKImage Solid(SKColor color)
{
    using var bitmap = new SKBitmap(new SKImageInfo(8, 4, SKColorType.Rgba8888, SKAlphaType.Premul));
    bitmap.Erase(color);
    return SKImage.FromBitmap(bitmap);
}
foreach (var beforeAlpha in new byte[] { 0, 128, 255 })
foreach (var afterAlpha in new byte[] { 0, 128, 255 })
foreach (var split in new[] { 0f, .5f, 1f })
{
    var a = beforeAlpha;
    var b = afterAlpha;
    var ratio = split;
    Test($"transparent comparison before={a} after={b} split={ratio}", () =>
    {
        using var before = Solid(new SKColor(220, 40, 70, a));
        using var after = Solid(new SKColor(20, 190, 230, b));
        using var target = new SKBitmap(new SKImageInfo(8, 4, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(target);
        canvas.Clear(new SKColor(80, 90, 100));
        var saved = canvas.SaveCount;
        ImageComparisonRenderer.Draw(canvas, before, after, new SKRect(0, 0, 8, 4), ratio);
        Check(canvas.SaveCount == saved, "Unbalanced save stack");
        using var left = new SKBitmap(target.Info);
        using var right = new SKBitmap(target.Info);
        using (var c = new SKCanvas(left)) { c.Clear(new SKColor(80, 90, 100)); c.DrawImage(before, 0, 0); }
        using (var c = new SKCanvas(right)) { c.Clear(new SKColor(80, 90, 100)); c.DrawImage(after, 0, 0); }
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 8; x++)
                Check(target.GetPixel(x, y) == (x < 8 * ratio ? left : right).GetPixel(x, y), $"Sample {x},{y} was blended twice or omitted.");
    });
}
Test("comparison preserves caller clipping and transform", () =>
{
    using var before = Solid(SKColors.Red);
    using var after = Solid(SKColors.Blue);
    using var target = new SKBitmap(20, 12);
    using var canvas = new SKCanvas(target);
    canvas.Clear(SKColors.Green);
    canvas.Translate(3, 2);
    canvas.ClipRect(new SKRect(0, 0, 6, 4));
    var matrix = canvas.TotalMatrix;
    var clip = canvas.LocalClipBounds;
    var count = canvas.SaveCount;
    ImageComparisonRenderer.Draw(canvas, before, after, new SKRect(0, 0, 8, 4), .5f);
    Check(canvas.TotalMatrix == matrix && canvas.LocalClipBounds == clip && canvas.SaveCount == count);
    Check(target.GetPixel(3, 2) == SKColors.Red && target.GetPixel(8, 3) == SKColors.Blue);
    Check(target.GetPixel(9, 3) == SKColors.Green && target.GetPixel(2, 2) == SKColors.Green);
});
foreach (var split in new[] { float.NaN, float.NegativeInfinity, -.1f, 1.1f })
{
    var value = split;
    Test("invalid split rejects without changing canvas " + value, () =>
    {
        using var image = Solid(SKColors.Red);
        using var bitmap = new SKBitmap(8, 4);
        using var canvas = new SKCanvas(bitmap);
        var saved = canvas.SaveCount;
        try { ImageComparisonRenderer.Draw(canvas, image, image, new SKRect(0, 0, 8, 4), value); }
        catch (ArgumentOutOfRangeException) { Check(canvas.SaveCount == saved); return; }
        throw new Exception("Expected split rejection");
    });
}
Test("empty bounds reject before drawing", () =>
{
    using var image = Solid(SKColors.Red);
    using var bitmap = new SKBitmap(8, 4);
    using var canvas = new SKCanvas(bitmap);
    try { ImageComparisonRenderer.Draw(canvas, image, image, SKRect.Empty, .5f); }
    catch (ArgumentOutOfRangeException) { return; }
    throw new Exception("Expected bounds rejection");
});
var results = new List<object>();
var failures = 0;
foreach (var (name, body) in tests)
{
    try { body(); results.Add(new { name, passed = true }); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); results.Add(new { name, passed = false, error = error.ToString() }); }
}
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/preview-tests.json", JsonSerializer.Serialize(new { total = tests.Count, passed = tests.Count - failures, failed = failures, results }));
Console.WriteLine($"PREVIEW_TESTS total={tests.Count} passed={tests.Count - failures} failed={failures}");
return failures == 0 ? 0 : 1;
