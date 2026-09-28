namespace ImageSpace.Documents;

/// <summary>Adobe PSD channel compression codes. Prediction is row-local, modulo-256 for RGB/8.</summary>
public enum PsdCompression : ushort
{
    Raw = 0,
    PackBits = 1,
    Zip = 2,
    ZipPrediction = 3
}
