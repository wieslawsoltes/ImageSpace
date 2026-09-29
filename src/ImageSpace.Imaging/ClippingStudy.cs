using ImageSpace.Core;

namespace ImageSpace.Imaging;

/// <summary>Original procedural clipping study with editable base, texture, sheen and clipped adjustment.</summary>
public static class ClippingStudy
{
    public static ImageDocument Create()
    {
        var document = new ImageDocument(1000, 680, "Clipping study");
        var background = Layer.Raster("Paper", 1000, 680);
        background.Pixels!.Fill(new(25, 29, 38));
        var basis = new Layer
        {
            Name = "Rounded window · clipping base",
            Kind = LayerKind.Rectangle,
            X = 120,
            Y = 125,
            Width = 760,
            Height = 385,
            CornerRadius = 68,
            Color = Rgba32.White
        };
        var texture = Layer.Raster("Aurora texture", 1000, 680);
        texture.IsClipped = true;
        var row = new byte[1000 * 4];
        for (var y = 0; y < 680; y++)
        {
            for (var x = 0; x < 1000; x++)
            {
                var wave = .5 + .5 * Math.Sin(x * .008 + y * .013);
                var height = y / 680.0;
                var at = x * 4;
                row[at] = Rgba32.Byte(24 + 125 * wave * height);
                row[at + 1] = Rgba32.Byte(55 + 145 * wave);
                row[at + 2] = Rgba32.Byte(120 + 90 * (1 - height));
                row[at + 3] = 255;
            }
            texture.Pixels!.WriteRow(0, y, row);
        }
        var sheen = new Layer
        {
            Name = "Elliptical sheen",
            Kind = LayerKind.Ellipse,
            X = 550,
            Y = -15,
            Width = 630,
            Height = 540,
            Color = new(176, 224, 255, 120),
            Blend = LayerBlend.Screen,
            IsClipped = true
        };
        var tone = new Layer
        {
            Name = "Clipped saturation",
            Kind = LayerKind.Adjustment,
            Adjustment = AdjustmentKind.Saturation,
            Amount = 24,
            IsClipped = true
        };
        var title = new Layer
        {
            Name = "CLIPPED / NOT CUT",
            Kind = LayerKind.Text,
            Text = "CLIPPED / NOT CUT",
            FontSize = 48,
            Bold = true,
            X = 120,
            Y = 38,
            Width = 800,
            Height = 65,
            Color = new(230, 234, 243)
        };
        var subtitle = new Layer
        {
            Name = "Editable source",
            Kind = LayerKind.Text,
            Text = "A shared shape. Independent layers. Original pixels.",
            FontSize = 22,
            X = 122,
            Y = 547,
            Width = 760,
            Height = 32,
            Color = new(170, 184, 205)
        };
        document.Layers.AddRange([background, basis, texture, sheen, tone, title, subtitle]);
        document.ActiveLayerId = texture.Id;
        document.Validate();
        return document;
    }
}
