using System.Numerics;
using Microsoft.UI.Xaml.Automation;

namespace ImageSpace.Workbench;

public sealed partial class MaskPropertiesEditor
{
    private readonly StackPanel _placementFields = new() { Spacing = 5 };
    private StudioButton _link = null!;
    private StudioButton _align = null!;
    private NumericField _x = null!, _y = null!, _width = null!, _height = null!, _angle = null!;

    private void BuildPlacementControls(StackPanel body)
    {
        _link = new StudioButton("Toggle mask link", () => Change(() =>
        {
            if (Current is { } layer)
                _session.SetMaskLinked(!layer.MaskLinked);
        }));
        _align = new StudioButton("Align mask to layer", () => Change(_session.AlignMaskToLayer));
        body.Children.Add(Studio.Row(_link, _align));
        _x = new NumericField("Mask X", 0, -1_000_000, 1_000_000, 125);
        _y = new NumericField("Mask Y", 0, -1_000_000, 1_000_000, 125);
        _width = new NumericField("Mask width", 1, .001, 1_000_000, 125);
        _height = new NumericField("Mask height", 1, .001, 1_000_000, 125);
        _angle = new NumericField("Mask angle", 0, -360, 360, double.NaN);
        _x.ValueChanged += value => EditPlacement(matrix => matrix with { M31 = (float)value });
        _y.ValueChanged += value => EditPlacement(matrix => matrix with { M32 = (float)value });
        _width.ValueChanged += value => EditPlacement(matrix =>
        {
            var current = new Vector2(matrix.M11, matrix.M12).Length() * Current!.Mask!.Width;
            return Matrix3x2.CreateScale((float)value / Math.Max(1e-8f, current), 1) * matrix;
        });
        _height.ValueChanged += value => EditPlacement(matrix =>
        {
            var current = new Vector2(matrix.M21, matrix.M22).Length() * Current!.Mask!.Height;
            return Matrix3x2.CreateScale(1, (float)value / Math.Max(1e-8f, current)) * matrix;
        });
        _angle.ValueChanged += value => EditPlacement(matrix =>
        {
            var delta = (float)value * MathF.PI / 180 - MathF.Atan2(matrix.M12, matrix.M11);
            return matrix * Matrix3x2.CreateRotation(delta, new Vector2(matrix.M31, matrix.M32));
        });
        _placementFields.Children.Add(Studio.Row(_x, _y));
        _placementFields.Children.Add(Studio.Row(_width, _height));
        _placementFields.Children.Add(_angle);
        body.Children.Add(_placementFields);
    }

    private void EditPlacement(Func<Matrix3x2, Matrix3x2> update) => Change(() =>
    {
        if (Current is { MaskLinked: false } layer)
            _session.SetMaskDocumentPlacement(AffinePlacement.FromMatrix(update(layer.MaskDocumentTransform)));
    });

    private void RefreshPlacement(Layer layer, bool editable)
    {
        _link.IsEnabled = _align.IsEnabled = editable;
        _placementFields.Visibility = layer.MaskLinked ? Visibility.Collapsed : Visibility.Visible;
        _link.Content = Studio.Row(new IconView(layer.MaskLinked ? "link" : "unlink"),
            Studio.Label(layer.MaskLinked ? "Linked" : "Unlinked", 11));
        ToolTipService.SetToolTip(_link, layer.MaskLinked
            ? "Unlink to transform layer and mask independently." : "Relink without moving the mask.");
        _x.IsEnabled = _y.IsEnabled = _width.IsEnabled = _height.IsEnabled = _angle.IsEnabled = editable && !layer.MaskLinked;
        var matrix = layer.MaskDocumentTransform;
        _x.Value = matrix.M31;
        _y.Value = matrix.M32;
        _width.Value = new Vector2(matrix.M11, matrix.M12).Length() * (layer.Mask?.Width ?? 1);
        _height.Value = new Vector2(matrix.M21, matrix.M22).Length() * (layer.Mask?.Height ?? 1);
        _angle.Value = MathF.Atan2(matrix.M12, matrix.M11) * 180 / MathF.PI;
    }
}
