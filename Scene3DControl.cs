using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MapIslandEditor.Models;

namespace MapIslandEditor;

public sealed class Scene3DControl : Control
{
    private readonly SolidColorBrush _bgBrush = new(Color.Parse("#101214"));
    private readonly SolidColorBrush _pointBrush = new(Color.Parse("#f2b366"));
    private readonly SolidColorBrush _textBrush = new(Color.Parse("#d6d6d6"));
    private readonly Pen _meshPen = new(new SolidColorBrush(Color.Parse("#f2b366")), 1);
    private readonly Typeface _typeface = new("Segoe UI");

    private List<SceneMeshInstance> _instances = [];
    private Vector3 _sceneCenter = Vector3.Zero;
    private float _sceneScale = 1f;
    private bool _pointerDown;
    private Point _lastPointer;
    private float _yaw = -0.8f;
    private float _pitch = -0.6f;
    private float _distance = 380f;
    private float _panX;
    private float _panY;

    public Scene3DControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public void SetScene(IReadOnlyList<SceneMeshInstance> instances)
    {
        _instances = instances.ToList();
        RecomputeBounds();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(_bgBrush, new Rect(Bounds.Size));

        if (_instances.Count == 0)
        {
            var text = new FormattedText("No 3D models loaded", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 14, _textBrush);
            context.DrawText(text, new Point((Bounds.Width - text.Width) * 0.5, (Bounds.Height - text.Height) * 0.5));
            return;
        }

        var cx = Bounds.Width * 0.5 + _panX;
        var cy = Bounds.Height * 0.5 + _panY;
        var pointsDrawn = 0;
        var linesDrawn = 0;

        foreach (var obj in _instances)
        {
            var pos = obj.Mesh.Positions;
            if (pos.Count < 3)
            {
                continue;
            }

            var indices = obj.Mesh.Indices;
            if (indices.Count >= 3)
            {
                var triangleCount = indices.Count / 3;
                var triangleStep = Math.Max(1, triangleCount / 3200);
                for (var t = 0; t < triangleCount; t += triangleStep)
                {
                    var i = t * 3;
                    var ia = indices[i];
                    var ib = indices[i + 1];
                    var ic = indices[i + 2];

                    if (!TryProjectVertex(obj, ia, cx, cy, out var a) ||
                        !TryProjectVertex(obj, ib, cx, cy, out var b) ||
                        !TryProjectVertex(obj, ic, cx, cy, out var c))
                    {
                        continue;
                    }

                    context.DrawLine(_meshPen, a, b);
                    context.DrawLine(_meshPen, b, c);
                    context.DrawLine(_meshPen, c, a);
                    linesDrawn += 3;
                }
            }
            else
            {
                var step = Math.Max(1, pos.Count / 1200);
                for (var i = 0; i + 2 < pos.Count; i += 3 * step)
                {
                    if (!TryProjectVertex(obj, i / 3, cx, cy, out var point))
                    {
                        continue;
                    }

                    context.DrawEllipse(_pointBrush, null, point, 1.3, 1.3);
                    pointsDrawn++;
                }
            }
        }

        var info = new FormattedText($"Objects: {_instances.Count}  Lines: {linesDrawn}  Points: {pointsDrawn}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 11, _textBrush);
        context.DrawText(info, new Point(10, 8));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _pointerDown = true;
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pointerDown)
        {
            return;
        }

        var p = e.GetPosition(this);
        var dx = (float)(p.X - _lastPointer.X);
        var dy = (float)(p.Y - _lastPointer.Y);
        _lastPointer = p;

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _panX += dx;
            _panY += dy;
        }
        else
        {
            _yaw += dx * 0.008f;
            _pitch = Math.Clamp(_pitch + dy * 0.008f, -1.45f, 1.45f);
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pointerDown = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = e.Delta.Y > 0 ? 0.88f : 1.12f;
        _distance = Math.Clamp(_distance * factor, 60f, 2200f);
        InvalidateVisual();
        e.Handled = true;
    }

    private Vector3 Project(Vector3 world)
    {
        var cp = MathF.Cos(_pitch);
        var sp = MathF.Sin(_pitch);
        var cy = MathF.Cos(_yaw);
        var sy = MathF.Sin(_yaw);

        var x1 = world.X * cy - world.Z * sy;
        var z1 = world.X * sy + world.Z * cy;

        var y2 = world.Y * cp - z1 * sp;
        var z2 = world.Y * sp + z1 * cp;

        var z = z2 + _distance;
        if (z < 1f)
        {
            z = 1f;
        }

        var fov = 620f;
        var sx = x1 * (fov / z);
        var sy2 = -y2 * (fov / z);
        return new Vector3(sx, sy2, z);
    }

    private bool TryProjectVertex(SceneMeshInstance obj, int vertexIndex, double cx, double cy, out Point screen)
    {
        screen = default;
        var pos = obj.Mesh.Positions;
        var offset = vertexIndex * 3;
        if (offset < 0 || offset + 2 >= pos.Count)
        {
            return false;
        }

        var lx = pos[offset] * _sceneScale;
        var ly = pos[offset + 1] * _sceneScale;
        var lz = pos[offset + 2] * _sceneScale;

        var rotY = obj.RotY * MathF.PI / 180f;
        var sin = MathF.Sin(rotY);
        var cos = MathF.Cos(rotY);

        var rx = lx * cos - lz * sin;
        var rz = lx * sin + lz * cos;

        var wx = rx + obj.GridX * 8f;
        var wy = ly;
        var wz = rz + obj.GridY * 8f;

        var projected = Project(new Vector3(wx, wy, wz) - _sceneCenter);
        if (projected.Z <= 0.01f)
        {
            return false;
        }

        var px = cx + projected.X;
        var py = cy + projected.Y;
        if (px < -6 || py < -6 || px > Bounds.Width + 6 || py > Bounds.Height + 6)
        {
            return false;
        }

        screen = new Point(px, py);
        return true;
    }

    private void RecomputeBounds()
    {
        if (_instances.Count == 0)
        {
            _sceneCenter = Vector3.Zero;
            _sceneScale = 1f;
            return;
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var obj in _instances)
        {
            min.X = MathF.Min(min.X, obj.GridX * 8f);
            min.Z = MathF.Min(min.Z, obj.GridY * 8f);
            max.X = MathF.Max(max.X, obj.GridX * 8f);
            max.Z = MathF.Max(max.Z, obj.GridY * 8f);
        }

        _sceneCenter = new Vector3((min.X + max.X) * 0.5f, 0, (min.Z + max.Z) * 0.5f);
        _sceneScale = 0.06f;
    }
}
