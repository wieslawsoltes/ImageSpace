using System.Buffers;
using ImageSpace.Core;

namespace ImageSpace.Imaging;

/// <summary>An exact axis-aligned boundary segment on integer pixel edges.</summary>
public readonly record struct SelectionEdge(int X1, int Y1, int X2, int Y2);
public readonly record struct SelectionContourStatistics(long Samples, long Segments, long ScratchBytes);

/// <summary>
/// Streams the exact threshold boundary using two pooled pixel rows and one vertical-run table.
/// Collinear unit edges are merged; holes, disconnected components and soft-alpha thresholding
/// are unchanged. Work is O(width * height), temporary storage O(width), not O(image pixels).
/// </summary>
public static class SelectionContours
{
    public static SelectionContourStatistics Trace(PixelSurface selection, Action<SelectionEdge> emit,
        byte threshold = 128, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(emit);
        if (threshold == 0)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        cancellationToken.ThrowIfCancellationRequested();
        var width = selection.Width;
        var height = selection.Height;
        var stride = checked(width * 4);
        var rows = ArrayPool<byte>.Shared.Rent(checked(stride * 2));
        int[]? starts = null;
        try
        {
            starts = ArrayPool<int>.Shared.Rent(width + 1);
            starts.AsSpan(0, width + 1).Fill(-1);
            rows.AsSpan(0, stride * 2).Clear();
            long segments = 0;
            var previousOffset = 0;
            var currentOffset = stride;
            for (var y = 0; y <= height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = rows.AsSpan(previousOffset, stride);
                var current = rows.AsSpan(currentOffset, stride);
                if (y < height)
                    selection.CopyRowTo(0, y, current);
                else
                    current.Clear();
                var horizontalStart = -1;
                var left = false;
                for (var x = 0; x <= width; x++)
                {
                    var inside = x < width && current[x * 4 + 3] >= threshold;
                    var above = x < width && previous[x * 4 + 3] >= threshold;
                    if (inside != above)
                    {
                        if (horizontalStart < 0)
                            horizontalStart = x;
                    }
                    else if (horizontalStart >= 0)
                    {
                        emit(new SelectionEdge(horizontalStart, y, x, y));
                        segments++;
                        horizontalStart = -1;
                    }
                    if (inside != left)
                    {
                        if (starts[x] < 0)
                            starts[x] = y;
                    }
                    else if (starts[x] >= 0)
                    {
                        emit(new SelectionEdge(x, starts[x], x, y));
                        segments++;
                        starts[x] = -1;
                    }
                    left = inside;
                }
                (previousOffset, currentOffset) = (currentOffset, previousOffset);
            }
            return new SelectionContourStatistics((long)width * height, segments, (long)stride * 2 + (long)(width + 1) * sizeof(int));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rows, clearArray: true);
            if (starts is not null)
                ArrayPool<int>.Shared.Return(starts);
        }
    }
}
