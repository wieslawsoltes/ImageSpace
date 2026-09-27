namespace ImageSpace.Core;

public sealed record LevelsAdjustment
{
    public LevelsChannel Rgb { get; init; } = new();
    public LevelsChannel Red { get; init; } = new();
    public LevelsChannel Green { get; init; } = new();
    public LevelsChannel Blue { get; init; } = new();

    public LevelsChannel GetChannel(ToneChannel channel) => channel switch
    {
        ToneChannel.Rgb => Rgb,
        ToneChannel.Red => Red,
        ToneChannel.Green => Green,
        ToneChannel.Blue => Blue,
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    public LevelsAdjustment WithChannel(ToneChannel channel, LevelsChannel levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        levels.Validate();
        return channel switch
        {
            ToneChannel.Rgb => this with { Rgb = levels },
            ToneChannel.Red => this with { Red = levels },
            ToneChannel.Green => this with { Green = levels },
            ToneChannel.Blue => this with { Blue = levels },
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }

    public void Validate()
    {
        if (Rgb is null || Red is null || Green is null || Blue is null)
        {
            throw new InvalidDataException("All four Levels channels are required.");
        }
        Rgb.Validate();
        Red.Validate();
        Green.Validate();
        Blue.Validate();
    }
}
