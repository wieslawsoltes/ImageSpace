namespace ImageSpace.Core;

/// <summary>A control point in the 8-bit input/output tonal domain.</summary>
public readonly record struct CurvePoint(int Input, int Output);
