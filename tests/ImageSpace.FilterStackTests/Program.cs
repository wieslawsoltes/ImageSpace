using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Editing;
using ImageSpace.Filters;

var tests = new List<(string Name, Func<Task> Body)>();
void Test(string name, Func<Task> body) => tests.Add((name, body));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Equal(PixelSurface a, PixelSurface b) => Check(a.Width == b.Width && a.Height == b.Height && a.ToRgba().SequenceEqual(b.ToRgba()), "Pixel mismatch");
async Task Reject(Func<Task> action) { try { await action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or OperationCanceledException) { return; } throw new Exception("Expected rejection"); }
PixelSurface Source(int width = 11, int height = 9)
{
    var source = new PixelSurface(width, height);
    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        source.Set(x, y, new Rgba32((byte)(x * 23 + y * 7), (byte)(x * 7 + y * 19), (byte)(x * 11 + y * 13), (byte)((x + y) % 3 == 0 ? 127 : 255)));
    return source;
}
EditorSession Session()
{
    var source = Source(); var layer = Layer.Raster("Filtered", source.Width, source.Height); layer.Pixels = source;
    return new EditorSession(new ImageDocument(source.Width, source.Height) { Layers = [layer], ActiveLayerId = layer.Id });
}
Task<IFilterSession> Cpu(PixelSurface source, CancellationToken token) => Task.FromResult<IFilterSession>(new CpuFilterSession(source));
foreach (var kind in Enum.GetValues<FilterKind>())
{
    var filter = kind;
    Test("single-step established CPU contract: " + kind, async () =>
    {
        var source = Source(); var saved = source.ToRgba(); var operation = FilterOperation.Default(filter);
        await using var session = new CpuFilterSession(source);
        var result = await session.ApplyAsync([operation]);
        Equal(FilterEngine.Apply(source, filter, operation.Amount, operation.Secondary), result);
        Check(source.ToRgba().SequenceEqual(saved));
    });
}
Test("stack order, reset-to-source, disabled stages and ownership", async () =>
{
    var source = Source(); var captured = source.Snapshot();
    await using var session = new CpuFilterSession(source);
    source.Fill(Rgba32.Black);
    FilterOperation[] recipe = [new(FilterKind.Sepia), new(FilterKind.Invert), new(FilterKind.Noise, 20, Enabled: false)];
    var result = await session.ApplyAsync(recipe);
    Equal(FilterEngine.Apply(FilterEngine.Apply(captured, FilterKind.Sepia), FilterKind.Invert), result);
    var second = await session.ApplyAsync(recipe); Equal(result, second);
    result.Fill(Rgba32.White); Equal(captured, await session.ApplyAsync([]));
    Check(!second.ToRgba().SequenceEqual((await session.ApplyAsync([new(FilterKind.Invert), new(FilterKind.Sepia)])).ToRgba()));
});
Test("validation rejects null operations, invalid enum, huge/nonfinite parameters and excessive stacks", async () =>
{
    await using var session = new CpuFilterSession(Source());
    foreach (var operation in new FilterOperation[] { new((FilterKind)999), new(FilterKind.Gamma, float.NaN), new(FilterKind.Invert, 1000001), new(FilterKind.Invert, Secondary: float.PositiveInfinity), null! })
        await Reject(() => session.ApplyAsync([operation]));
    await Reject(() => session.ApplyAsync(Enumerable.Repeat(new FilterOperation(FilterKind.Invert), 17).ToArray()));
    await Reject(() => session.ApplyAsync(null!));
});
Test("preview scaling captures values without changing authored settings", () =>
{
    FilterOperation[] recipe = [new(FilterKind.GaussianBlur, 8), new(FilterKind.Pixelate, 20), new(FilterKind.Gamma, 1.2f)];
    var scaled = FilterRecipe.ForPreview(recipe, .25f);
    Check(scaled[0].Amount == 2 && scaled[1].Amount == 5 && scaled[2].Amount == 1.2f);
    Check(recipe[0].Amount == 8 && recipe[1].Amount == 20);
    return Task.CompletedTask;
});
Test("already cancelled and disposed sessions reject without work", async () =>
{
    var session = new CpuFilterSession(Source());
    await Reject(() => session.ApplyAsync([new(FilterKind.Invert)], new CancellationToken(true)));
    await session.DisposeAsync(); await session.DisposeAsync();
    await Reject(() => session.ApplyAsync([]));
});
Test("stack is one undo entry and preserves exact authored source on undo", async () =>
{
    var session = Session(); var before = session.Document.ActiveLayer!.Pixels!.Snapshot();
    await session.ApplyFilterStackAsync([new(FilterKind.Invert), new(FilterKind.Grayscale)], Cpu);
    Check(session.History.Count == 1 && session.IsDirty);
    var after = session.Document.ActiveLayer!.Pixels!.Snapshot();
    Equal(FilterEngine.Apply(FilterEngine.Apply(before, FilterKind.Invert), FilterKind.Grayscale), after);
    session.Undo(); Equal(before, session.Document.ActiveLayer!.Pixels!);
    session.Redo(); Equal(after, session.Document.ActiveLayer!.Pixels!);
});
Test("disabled-only recipe performs no transaction or backend allocation", async () =>
{
    var session = Session(); var invoked = false;
    await session.ApplyFilterStackAsync([new(FilterKind.Invert, Enabled: false)], (source, ct) => { invoked = true; return Cpu(source, ct); });
    Check(!invoked && !session.IsDirty && session.History.Count == 0);
});
foreach (var mode in new[] { "locked", "mask", "text", "transaction" })
{
    var reason = mode;
    Test("reject invalid target: " + reason, async () =>
    {
        var session = Session(); var layer = session.Document.ActiveLayer!;
        if (reason == "locked") layer.Locked = true;
        if (reason == "mask") { layer.Mask = Source(); session.Document.EditMask = true; }
        if (reason == "text") layer.Kind = LayerKind.Text;
        if (reason == "transaction") session.Begin("Existing gesture");
        var called = false;
        await Reject(() => session.ApplyFilterStackAsync([new(FilterKind.Invert)], (src, ct) => { called = true; return Cpu(src, ct); }));
        Check(!called && session.History.Count == 0);
    });
}
Test("selection is applied once after the complete stack", async () =>
{
    var session = Session(); var source = session.Document.ActiveLayer!.Pixels!.Snapshot();
    var mask = new PixelSurface(source.Width, source.Height); mask.Set(2, 3, Rgba32.White); session.Document.Selection = mask;
    await session.ApplyFilterStackAsync([new(FilterKind.Invert)], Cpu);
    var output = session.Document.ActiveLayer!.Pixels!;
    var expected = FilterEngine.Apply(source, FilterKind.Invert);
    for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
        Check(output.Get(x, y) == (x == 2 && y == 3 ? expected.Get(x, y) : source.Get(x, y)));
});
foreach (var kind in new[] { "revision", "pixels", "selection", "transform", "cancel" })
{
    var mode = kind;
    Test("stale asynchronous result rejected: " + mode, async () =>
    {
        var session = Session(); var layer = session.Document.ActiveLayer!; var source = layer.Pixels!;
        var token = new CancellationTokenSource();
        var pending = new TaskCompletionSource<PixelSurface>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeSession((_, _) => pending.Task);
        var task = session.ApplyFilterStackAsync([new(FilterKind.Invert)], (_, _) => Task.FromResult<IFilterSession>(backend), token.Token);
        if (mode == "revision") session.Notify();
        if (mode == "pixels") source.Set(0, 0, Rgba32.White);
        if (mode == "selection") session.Document.Selection = new PixelSurface(source.Width, source.Height);
        if (mode == "transform") layer.X += 1;
        if (mode == "cancel") token.Cancel();
        pending.SetResult(Source());
        await Reject(() => task);
        Check(session.History.Count == 0 && backend.Disposed && !session.IsInTransaction);
        token.Dispose();
    });
}
Test("invalid output dimensions reject before opening a transaction", async () =>
{
    var session = Session(); var backend = new FakeSession((_, _) => Task.FromResult(new PixelSurface(1, 1)));
    await Reject(() => session.ApplyFilterStackAsync([new(FilterKind.Invert)], (_, _) => Task.FromResult<IFilterSession>(backend)));
    Check(session.History.Count == 0 && backend.Disposed);
});
var failures = 0; var results = new List<object>();
foreach (var (name, body) in tests)
{
    try { await body(); results.Add(new { name, passed = true }); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error); results.Add(new { name, passed = false, error = error.ToString() }); }
}
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/filter-stack-tests.json", JsonSerializer.Serialize(new { total = tests.Count, passed = tests.Count - failures, failed = failures, results }));
// Actual established C# outputs supplement the independently implemented scalar JS oracle.
var fixture = Source(19, 17);
var fixtures = Enum.GetValues<FilterKind>().Where(kind => kind != FilterKind.Noise).Select(kind =>
{
    var op = FilterOperation.Default(kind);
    return new { kind = kind.ToString(), amount = op.Amount, secondary = op.Secondary,
        expected = FilterEngine.Apply(fixture, kind, op.Amount, op.Secondary).ToRgba() };
});
File.WriteAllText("artifacts/filter-stack-fixtures.json", JsonSerializer.Serialize(new { width = fixture.Width, height = fixture.Height, source = fixture.ToRgba(), cases = fixtures }));
Console.WriteLine($"FILTER_STACK_TESTS total={tests.Count} passed={tests.Count - failures} failed={failures}");
return failures == 0 ? 0 : 1;

internal sealed class FakeSession(Func<IReadOnlyList<FilterOperation>, CancellationToken, Task<PixelSurface>> apply) : IFilterSession
{
    public bool Disposed { get; private set; }
    public string Backend => "Test backend";
    public Task<PixelSurface> ApplyAsync(IReadOnlyList<FilterOperation> operations, CancellationToken token = default) => apply(operations, token);
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
