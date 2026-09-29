using SkiaSharp;

namespace ImageSpace.Skia;

/// <summary>Draws a split comparison without compositing either image over the other.</summary>
public static class ImageComparisonRenderer
{
    /// <summary>
    /// Draws the original left of <paramref name="split"/> and the result to its right.
    /// Both images blend directly with the caller's existing background. The images,
    /// canvas and graphics context remain owned by the caller; no readback is performed.
    /// </summary>
    public static void Draw(SKCanvas canvas, SKImage before, SKImage after, SKRect bounds, float split)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (!float.IsFinite(split) || split is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(split));
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom) ||
            !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || bounds.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        var edge = bounds.Left + bounds.Width * split;
        // Disjoint, non-antialiased clips prevent a translucent original from
        // blending over the filtered pixels. Each sample is composited exactly once.
        if (split > 0)
            DrawRegion(canvas, before, bounds, new SKRect(bounds.Left, bounds.Top, edge, bounds.Bottom));
        if (split < 1)
            DrawRegion(canvas, after, bounds, new SKRect(edge, bounds.Top, bounds.Right, bounds.Bottom));
    }

    private static void DrawRegion(SKCanvas canvas, SKImage image, SKRect bounds, SKRect clip)
    {
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(clip, SKClipOperation.Intersect, false);
            canvas.DrawImage(image, bounds);
        }
        finally { canvas.RestoreToCount(save); }
    }
}
