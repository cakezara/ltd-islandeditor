using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MapIslandEditor.Models;

namespace MapIslandEditor;

public enum MapEditMode
{
    MoveObjects,
    AddLand,
    DeleteLand
}

public sealed class MapCanvasControl : Control
{
    private const double BaseCellSize = 8.0;
    private const double MinZoom = 0.08;
    private const double MaxZoom = 16.0;

    private readonly SolidColorBrush _backgroundBrush = new(Color.Parse("#121212"));
    private readonly SolidColorBrush _tileBorderBrush = new(Color.Parse("#1f2a36"));
    private readonly SolidColorBrush _iconTextBrush = new(Color.Parse("#101010"));
    private readonly SolidColorBrush _objectStrokeBrush = new(Color.Parse("#20150a"));
    private readonly SolidColorBrush _objectFillBrush = new(Color.Parse("#ff8b3d"));
    private readonly Pen _tileBorderPen;
    private readonly Pen _objectStrokePen;
    private readonly Pen _selectedObjectPen;
    private readonly Pen _moveGizmoPen;
    private readonly Pen _coordMinorPen;
    private readonly Pen _coordMajorPen;
    private readonly Pen _coordAxisPen;
    private readonly SolidColorBrush _coordTextBrush;
    private readonly Typeface _typeface = new("Segoe UI");

    private MapProject? _map;
    private Func<uint, string>? _nameResolver;
    private int _tileStep = 1;
    private bool _pointerDown;
    private bool _draggingObject;
    private bool _paintingTiles;
    private bool _tileRectSelecting;
    private int _tileRectStartX = -1;
    private int _tileRectStartY = -1;
    private int _tileRectEndX = -1;
    private int _tileRectEndY = -1;
    private int _dragObjectIndex = -1;
    private int _dragStartX;
    private int _dragStartY;
    private Point _lastPointer;
    private double _panX;
    private double _panY;
    private double _zoom = 1;
    private int _selectedObjectIndex = -1;
    private Point _hoverPointer;
    private bool _hasHoverPointer;
    private uint _landHash;
    private uint _waterHash;
    private Dictionary<uint, IBrush> _tileBrushByHash = [];
    private readonly Dictionary<uint, IBrush> _generatedTileBrushCache = [];

    public event EventHandler<MapObjectMovedEventArgs>? ObjectMoved;
    public event EventHandler<ObjectSelectedEventArgs>? ObjectSelected;
    public event EventHandler? TilePainted;
    public event EventHandler? TilePaintStrokeStarted;
    public event EventHandler? TilePaintStrokeCompleted;

    private MapEditMode _editMode = MapEditMode.MoveObjects;

    public MapEditMode EditMode
    {
        get => _editMode;
        set
        {
            _editMode = value;
            UpdateCursor();
            InvalidateVisual();
        }
    }

    public int SelectedObjectIndex
    {
        get => _selectedObjectIndex;
        set
        {
            _selectedObjectIndex = value;
            InvalidateVisual();
        }
    }

    public MapCanvasControl()
    {
        _tileBorderPen = new Pen(_tileBorderBrush, 1);
        _objectStrokePen = new Pen(_objectStrokeBrush, 1);
        _selectedObjectPen = new Pen(Brushes.Yellow, 2);
        _moveGizmoPen = new Pen(new SolidColorBrush(Color.Parse("#FFF2F2F2")), 2);
        _coordMinorPen = new Pen(new SolidColorBrush(Color.Parse("#22FFFFFF")), 1);
        _coordMajorPen = new Pen(new SolidColorBrush(Color.Parse("#40FFFFFF")), 1);
        _coordAxisPen = new Pen(new SolidColorBrush(Color.Parse("#A6FFB30F")), 1.8);
        _coordTextBrush = new SolidColorBrush(Color.Parse("#E6FFB30F"));
        Focusable = true;
        ClipToBounds = true;
    }

    public void SetMap(MapProject? map, Func<uint, string> nameResolver)
    {
        _map = map;
        _nameResolver = nameResolver;
        _draggingObject = false;
        _paintingTiles = false;
        _tileRectSelecting = false;
        _tileRectStartX = -1;
        _tileRectStartY = -1;
        _tileRectEndX = -1;
        _tileRectEndY = -1;
        _dragObjectIndex = -1;
        _pointerDown = false;
        _selectedObjectIndex = -1;
        _hasHoverPointer = false;
        RecalculateTileStep();
        CenterAndFit();
        UpdateCursor();
        InvalidateVisual();
    }

    public void SetTerrainHashes(uint landHash, uint waterHash)
    {
        _landHash = landHash;
        _waterHash = waterHash;
    }

    public void SetTileColorMap(Dictionary<uint, Color> colors)
    {
        _tileBrushByHash = colors.ToDictionary(
            kv => kv.Key,
            kv => (IBrush)new SolidColorBrush(kv.Value));
        _generatedTileBrushCache.Clear();
        InvalidateVisual();
    }

    public void PanBy(double dx, double dy)
    {
        _panX += dx;
        _panY += dy;
        InvalidateVisual();
    }

    public void ZoomBy(double factor)
    {
        _zoom = Clamp(_zoom * factor, MinZoom, MaxZoom);
        InvalidateVisual();
    }

    public void ResetView()
    {
        CenterAndFit();
        InvalidateVisual();
    }

    public bool TryGetGridAtLastPointer(out int gridX, out int gridY)
    {
        if (!_hasHoverPointer)
        {
            gridX = -1;
            gridY = -1;
            return false;
        }

        return TryGetGridAtPoint(_hoverPointer, out gridX, out gridY);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        CenterAndFit();
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var properties = e.GetCurrentPoint(this).Properties;
        var isLeft = properties.IsLeftButtonPressed;
        var isRight = properties.IsRightButtonPressed;
        _lastPointer = e.GetPosition(this);
        _hoverPointer = _lastPointer;
        _hasHoverPointer = true;
        UpdateCursor();

        _dragObjectIndex = HitTestObject(_lastPointer);
        if (_dragObjectIndex >= 0)
        {
            _selectedObjectIndex = _dragObjectIndex;
            ObjectSelected?.Invoke(this, new ObjectSelectedEventArgs { Index = _dragObjectIndex });
            InvalidateVisual();
        }

        if (isRight)
        {
            UpdateCursor();
            return;
        }

        if (!isLeft)
        {
            UpdateCursor();
            return;
        }

        _pointerDown = true;

        if (isLeft && (EditMode == MapEditMode.AddLand || EditMode == MapEditMode.DeleteLand))
        {
            TilePaintStrokeStarted?.Invoke(this, EventArgs.Empty);
            _paintingTiles = true;
            _tileRectSelecting = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (_tileRectSelecting)
            {
                if (TryGetGridAtPointClamped(_lastPointer, out var startX, out var startY))
                {
                    _tileRectStartX = startX;
                    _tileRectStartY = startY;
                    _tileRectEndX = startX;
                    _tileRectEndY = startY;
                }
            }
            else
            {
                _tileRectStartX = -1;
                _tileRectStartY = -1;
                _tileRectEndX = -1;
                _tileRectEndY = -1;
                TryPaintTileAtPoint(_lastPointer);
            }
            InvalidateVisual();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (isLeft && EditMode == MapEditMode.MoveObjects && _dragObjectIndex >= 0)
        {
            _draggingObject = true;
            if (_map is not null && _dragObjectIndex < _map.Objects.Count)
            {
                _dragStartX = _map.Objects[_dragObjectIndex].GridPosX;
                _dragStartY = _map.Objects[_dragObjectIndex].GridPosY;
            }
            TrySnapObjectToPoint(_dragObjectIndex, _lastPointer);
            InvalidateVisual();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        _draggingObject = false;
        _paintingTiles = false;
        e.Pointer.Capture(this);
        UpdateCursor();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hoverPointer = e.GetPosition(this);
        _hasHoverPointer = true;
        UpdateCursor();
        if (!_pointerDown)
        {
            InvalidateVisual();
            return;
        }

        var position = _hoverPointer;
        var dx = position.X - _lastPointer.X;
        var dy = position.Y - _lastPointer.Y;
        _lastPointer = position;

        if (_paintingTiles)
        {
            if (_tileRectSelecting)
            {
                if (TryGetGridAtPointClamped(position, out var endX, out var endY))
                {
                    _tileRectEndX = endX;
                    _tileRectEndY = endY;
                }
                InvalidateVisual();
                return;
            }

            TryPaintTileAtPoint(position);
            InvalidateVisual();
            return;
        }

        if (_draggingObject && _dragObjectIndex >= 0)
        {
            TrySnapObjectToPoint(_dragObjectIndex, position);
            InvalidateVisual();
            return;
        }

        _panX += dx;
        _panY += dy;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pointerDown)
        {
            return;
        }

        if (_map is not null && _draggingObject && _dragObjectIndex >= 0 && _dragObjectIndex < _map.Objects.Count)
        {
            var obj = _map.Objects[_dragObjectIndex];
            ObjectMoved?.Invoke(this, new MapObjectMovedEventArgs
            {
                Index = _dragObjectIndex,
                OldGridPosX = _dragStartX,
                OldGridPosY = _dragStartY,
                GridPosX = obj.GridPosX,
                GridPosY = obj.GridPosY
            });
        }

        _pointerDown = false;
        _draggingObject = false;
        if (_paintingTiles)
        {
            if (_tileRectSelecting)
            {
                if (TryPaintTileRect())
                {
                    TilePainted?.Invoke(this, EventArgs.Empty);
                }
            }
            TilePaintStrokeCompleted?.Invoke(this, EventArgs.Empty);
        }
        _paintingTiles = false;
        _tileRectSelecting = false;
        _tileRectStartX = -1;
        _tileRectStartY = -1;
        _tileRectEndX = -1;
        _tileRectEndY = -1;
        _dragObjectIndex = -1;
        e.Pointer.Capture(null);
        UpdateCursor();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hoverPointer = e.GetPosition(this);
        _hasHoverPointer = true;
        UpdateCursor();
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hasHoverPointer = false;
        UpdateCursor();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = e.Delta.Y > 0 ? 1.1 : 0.9;
        _zoom = Clamp(_zoom * factor, MinZoom, MaxZoom);
        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        context.FillRectangle(_backgroundBrush, new Rect(Bounds.Size));
        if (_map is null)
        {
            return;
        }

        RenderTiles(context);
        RenderCoordinateOverlay(context);
        RenderObjects(context);
        RenderModeOverlay(context);
    }

    private void RenderTiles(DrawingContext context)
    {
        if (_map is null)
        {
            return;
        }

        var effectiveStep = GetRenderTileStep();
        var halfWidth = _map.Width * 0.5;
        var halfHeight = _map.Height * 0.5;
        var worldCell = BaseCellSize * effectiveStep;
        var drawSize = Math.Max(1, worldCell * _zoom);
        var halfDraw = drawSize * 0.5;
        var drawBorder = drawSize >= 6;
        GetVisibleGridBounds(_map, out var minX, out var maxX, out var minY, out var maxY);
        minX = AlignDownToStep(minX, effectiveStep);
        minY = AlignDownToStep(minY, effectiveStep);

        for (var y = minY; y <= maxY; y += effectiveStep)
        {
            for (var x = minX; x <= maxX; x += effectiveStep)
            {
                var hash = _map.Grid[x, y];
                var brush = GetTileBrush(hash);
                var p = WorldToScreen((x - halfWidth) * BaseCellSize, (y - halfHeight) * BaseCellSize);
                var rect = new Rect(p.X - halfDraw, p.Y - halfDraw, drawSize, drawSize);
                context.DrawRectangle(brush, null, rect);
                if (drawBorder)
                {
                    context.DrawRectangle(null, _tileBorderPen, rect);
                }
            }
        }
    }

    private void RenderObjects(DrawingContext context)
    {
        if (_map is null)
        {
            return;
        }

        var halfWidth = _map.Width * 0.5;
        var halfHeight = _map.Height * 0.5;
        var diameter = Math.Max(6, BaseCellSize * _zoom * 0.9);
        var radius = diameter * 0.5;

        for (var i = 0; i < _map.Objects.Count; i++)
        {
            var obj = _map.Objects[i];
            var px = (obj.GridPosX - halfWidth) * BaseCellSize;
            var pz = (obj.GridPosY - halfHeight) * BaseCellSize;
            var p = WorldToScreen(px, pz);
            var rect = new Rect(p.X - radius, p.Y - radius, diameter, diameter);

            context.DrawEllipse(_objectFillBrush, _objectStrokePen, rect.Center, radius, radius);
            if (i == _selectedObjectIndex)
            {
                context.DrawEllipse(null, _selectedObjectPen, rect.Center, radius + 2, radius + 2);
                if (EditMode == MapEditMode.MoveObjects)
                {
                    var arm = radius + Math.Max(7, _zoom * 3);
                    context.DrawLine(_moveGizmoPen, new Point(p.X - arm, p.Y), new Point(p.X + arm, p.Y));
                    context.DrawLine(_moveGizmoPen, new Point(p.X, p.Y - arm), new Point(p.X, p.Y + arm));
                }
            }

            var label = GetObjectIcon(_nameResolver?.Invoke(obj.Hash) ?? "(unknown)");
            var text = new FormattedText(
                label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                Math.Max(10, radius * 1.05),
                _iconTextBrush);

            context.DrawText(text, new Point(p.X - text.Width * 0.5, p.Y - text.Height * 0.5));
        }
    }

    private void RenderCoordinateOverlay(DrawingContext context)
    {
        if (_map is null)
        {
            return;
        }

        if (_zoom < 0.12)
        {
            return;
        }

        var halfWidth = _map.Width * 0.5;
        var halfHeight = _map.Height * 0.5;
        var topZ = -halfHeight * BaseCellSize;
        var bottomZ = (_map.Height - 1 - halfHeight) * BaseCellSize;
        var leftX = -halfWidth * BaseCellSize;
        var rightX = (_map.Width - 1 - halfWidth) * BaseCellSize;

        var minorTiles = 1;
        while (BaseCellSize * _zoom * minorTiles < 24)
        {
            minorTiles *= 2;
        }

        var majorTiles = minorTiles * 4;
        GetVisibleGridBounds(_map, out var minX, out var maxX, out var minY, out var maxY);
        minX = AlignDownToStep(minX, minorTiles);
        minY = AlignDownToStep(minY, minorTiles);
        maxX = Math.Min(_map.Width - 1, maxX + minorTiles);
        maxY = Math.Min(_map.Height - 1, maxY + minorTiles);
        var majorMinX = AlignDownToStep(minX, majorTiles);
        var majorMinY = AlignDownToStep(minY, majorTiles);

        for (var x = minX; x <= maxX; x += minorTiles)
        {
            var worldX = (x - halfWidth) * BaseCellSize;
            var top = WorldToScreen(worldX, topZ);
            var bottom = WorldToScreen(worldX, bottomZ);
            context.DrawLine(_coordMinorPen, top, bottom);
        }

        for (var y = minY; y <= maxY; y += minorTiles)
        {
            var worldZ = (y - halfHeight) * BaseCellSize;
            var left = WorldToScreen(leftX, worldZ);
            var right = WorldToScreen(rightX, worldZ);
            context.DrawLine(_coordMinorPen, left, right);
        }

        var axisTop = WorldToScreen(leftX, topZ);
        var axisBottom = WorldToScreen(leftX, bottomZ);
        var axisLeft = WorldToScreen(leftX, topZ);
        var axisRight = WorldToScreen(rightX, topZ);
        context.DrawLine(_coordAxisPen, axisTop, axisBottom);
        context.DrawLine(_coordAxisPen, axisLeft, axisRight);

        var labelY = 4.0;
        var labelX = 6.0;

        for (var x = majorMinX; x <= maxX; x += majorTiles)
        {
            var coord = x;
            var point = WorldToScreen((x - halfWidth) * BaseCellSize, 0);
            if (point.X < -40 || point.X > Bounds.Width + 40)
            {
                continue;
            }

            context.DrawLine(_coordMajorPen, WorldToScreen((x - halfWidth) * BaseCellSize, topZ), WorldToScreen((x - halfWidth) * BaseCellSize, bottomZ));
            var text = new FormattedText(
                coord.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                11,
                _coordTextBrush);
            context.DrawText(text, new Point(point.X - text.Width * 0.5, labelY));
        }

        for (var y = majorMinY; y <= maxY; y += majorTiles)
        {
            var coord = y;
            var point = WorldToScreen(0, (y - halfHeight) * BaseCellSize);
            if (point.Y < -20 || point.Y > Bounds.Height + 20)
            {
                continue;
            }

            context.DrawLine(_coordMajorPen, WorldToScreen(leftX, (y - halfHeight) * BaseCellSize), WorldToScreen(rightX, (y - halfHeight) * BaseCellSize));
            var text = new FormattedText(
                coord.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                11,
                _coordTextBrush);
            context.DrawText(text, new Point(labelX, point.Y - text.Height * 0.5));
        }

        var xLabel = new FormattedText("Grid X", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 12, _coordTextBrush);
        var yLabel = new FormattedText("Grid Y", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 12, _coordTextBrush);
        context.DrawText(xLabel, new Point(Bounds.Width - xLabel.Width - 6, labelY));
        context.DrawText(yLabel, new Point(labelX, 4));
    }

    private int HitTestObject(Point point)
    {
        if (_map is null)
        {
            return -1;
        }

        var halfWidth = _map.Width * 0.5;
        var halfHeight = _map.Height * 0.5;
        var radius = Math.Max(6, BaseCellSize * _zoom * 0.45);
        var r2 = radius * radius;

        for (var i = _map.Objects.Count - 1; i >= 0; i--)
        {
            var obj = _map.Objects[i];
            var p = WorldToScreen((obj.GridPosX - halfWidth) * BaseCellSize, (obj.GridPosY - halfHeight) * BaseCellSize);
            var dx = point.X - p.X;
            var dy = point.Y - p.Y;
            if (dx * dx + dy * dy <= r2)
            {
                return i;
            }
        }

        return -1;
    }

    private void TrySnapObjectToPoint(int objectIndex, Point point)
    {
        if (_map is null || objectIndex < 0 || objectIndex >= _map.Objects.Count)
        {
            return;
        }

        var world = ScreenToWorld(point);
        var halfWidth = _map.Width * 0.5;
        var halfHeight = _map.Height * 0.5;
        var gx = (int)Math.Round(world.X / BaseCellSize + halfWidth);
        var gy = (int)Math.Round(world.Y / BaseCellSize + halfHeight);

        gx = Math.Clamp(gx, 0, _map.Width - 1);
        gy = Math.Clamp(gy, 0, _map.Height - 1);

        _map.Objects[objectIndex].GridPosX = gx;
        _map.Objects[objectIndex].GridPosY = gy;
    }

    private bool TryPaintTileAtPoint(Point point)
    {
        if (_map is null || !TryGetGridAtPoint(point, out var gx, out var gy))
        {
            return false;
        }

        var target = EditMode == MapEditMode.AddLand ? _landHash : _waterHash;
        if (_map.Grid[gx, gy] == target)
        {
            return false;
        }

        _map.Grid[gx, gy] = target;
        TilePainted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool TryPaintTileRect()
    {
        if (_map is null || _tileRectStartX < 0 || _tileRectStartY < 0 || _tileRectEndX < 0 || _tileRectEndY < 0)
        {
            return false;
        }

        var minX = Math.Min(_tileRectStartX, _tileRectEndX);
        var maxX = Math.Max(_tileRectStartX, _tileRectEndX);
        var minY = Math.Min(_tileRectStartY, _tileRectEndY);
        var maxY = Math.Max(_tileRectStartY, _tileRectEndY);
        var target = EditMode == MapEditMode.AddLand ? _landHash : _waterHash;
        var changed = false;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (_map.Grid[x, y] == target)
                {
                    continue;
                }

                _map.Grid[x, y] = target;
                changed = true;
            }
        }

        return changed;
    }

    private bool TryGetGridAtPoint(Point point, out int gx, out int gy)
    {
        gx = -1;
        gy = -1;
        if (_map is null)
        {
            return false;
        }

        var world = ScreenToWorld(point);
        var halfWidth = _map.Width * 0.5;
        var halfHeight = _map.Height * 0.5;
        gx = (int)Math.Round(world.X / BaseCellSize + halfWidth);
        gy = (int)Math.Round(world.Y / BaseCellSize + halfHeight);
        return gx >= 0 && gy >= 0 && gx < _map.Width && gy < _map.Height;
    }

    private bool TryGetGridAtPointClamped(Point point, out int gx, out int gy)
    {
        gx = -1;
        gy = -1;
        if (_map is null)
        {
            return false;
        }

        var world = ScreenToWorld(point);
        var halfWidth = _map.Width * 0.5;
        var halfHeight = _map.Height * 0.5;
        gx = (int)Math.Round(world.X / BaseCellSize + halfWidth);
        gy = (int)Math.Round(world.Y / BaseCellSize + halfHeight);
        gx = Math.Clamp(gx, 0, _map.Width - 1);
        gy = Math.Clamp(gy, 0, _map.Height - 1);
        return true;
    }

    private bool TryGetTileCenterAtPoint(Point point, out Point center, out int gx, out int gy)
    {
        center = default;
        if (!TryGetGridAtPoint(point, out gx, out gy) || _map is null)
        {
            return false;
        }

        var halfWidth = _map.Width * 0.5;
        var halfHeight = _map.Height * 0.5;
        center = WorldToScreen((gx - halfWidth) * BaseCellSize, (gy - halfHeight) * BaseCellSize);
        return true;
    }

    private Rect GetTileRectScreenBounds(int minX, int maxX, int minY, int maxY)
    {
        if (_map is null)
        {
            return new Rect();
        }

        var halfWidth = _map.Width * 0.5;
        var halfHeight = _map.Height * 0.5;
        var topLeft = WorldToScreen((minX - halfWidth) * BaseCellSize, (minY - halfHeight) * BaseCellSize);
        var bottomRight = WorldToScreen((maxX - halfWidth) * BaseCellSize, (maxY - halfHeight) * BaseCellSize);
        var cellSize = BaseCellSize * _zoom;
        var left = Math.Min(topLeft.X, bottomRight.X) - cellSize * 0.5;
        var top = Math.Min(topLeft.Y, bottomRight.Y) - cellSize * 0.5;
        var right = Math.Max(topLeft.X, bottomRight.X) + cellSize * 0.5;
        var bottom = Math.Max(topLeft.Y, bottomRight.Y) + cellSize * 0.5;
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private void RenderModeOverlay(DrawingContext context)
    {
        if (_map is null || !_hasHoverPointer)
        {
            return;
        }

        if (EditMode is MapEditMode.AddLand or MapEditMode.DeleteLand)
        {
            if (_tileRectSelecting && _tileRectStartX >= 0 && _tileRectStartY >= 0 && _tileRectEndX >= 0 && _tileRectEndY >= 0)
            {
                var minX = Math.Min(_tileRectStartX, _tileRectEndX);
                var maxX = Math.Max(_tileRectStartX, _tileRectEndX);
                var minY = Math.Min(_tileRectStartY, _tileRectEndY);
                var maxY = Math.Max(_tileRectStartY, _tileRectEndY);
                var rect = GetTileRectScreenBounds(minX, maxX, minY, maxY);
                var stroke = EditMode == MapEditMode.AddLand
                    ? new Pen(new SolidColorBrush(Color.Parse("#CC00E676")), 2)
                    : new Pen(new SolidColorBrush(Color.Parse("#CCD50000")), 2);
                var rectFill = EditMode == MapEditMode.AddLand
                    ? new SolidColorBrush(Color.Parse("#3300E676"))
                    : new SolidColorBrush(Color.Parse("#33D50000"));
                context.DrawRectangle(rectFill, stroke, rect);
            }

            var radius = Math.Max(8, BaseCellSize * _zoom * 0.8);
            var fill = EditMode == MapEditMode.AddLand
                ? new SolidColorBrush(Color.Parse("#6600C853"))
                : new SolidColorBrush(Color.Parse("#66D50000"));
            var symbol = EditMode == MapEditMode.AddLand ? "+" : "-";
            var text = new FormattedText(
                symbol,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                Math.Max(12, radius),
                Brushes.White);
            context.DrawEllipse(fill, _moveGizmoPen, _hoverPointer, radius, radius);
            context.DrawText(text, new Point(_hoverPointer.X - text.Width * 0.5, _hoverPointer.Y - text.Height * 0.5));
            return;
        }

        if (EditMode == MapEditMode.MoveObjects)
        {
            var radius = Math.Max(8, 5 + _zoom * 1.8);
            var fill = new SolidColorBrush(Color.Parse("#55FFFFFF"));
            context.DrawEllipse(fill, _moveGizmoPen, _hoverPointer, radius, radius);
            context.DrawLine(_moveGizmoPen, new Point(_hoverPointer.X - radius - 4, _hoverPointer.Y), new Point(_hoverPointer.X + radius + 4, _hoverPointer.Y));
            context.DrawLine(_moveGizmoPen, new Point(_hoverPointer.X, _hoverPointer.Y - radius - 4), new Point(_hoverPointer.X, _hoverPointer.Y + radius + 4));
        }
    }

    private Point WorldToScreen(double worldX, double worldZ)
    {
        return new Point(
            Bounds.Width * 0.5 + _panX + worldX * _zoom,
            Bounds.Height * 0.5 + _panY + worldZ * _zoom);
    }

    private Point ScreenToWorld(Point screen)
    {
        return new Point(
            (screen.X - Bounds.Width * 0.5 - _panX) / _zoom,
            (screen.Y - Bounds.Height * 0.5 - _panY) / _zoom);
    }

    private void RecalculateTileStep()
    {
        if (_map is null)
        {
            _tileStep = 1;
            return;
        }

        var cellCount = _map.Width * _map.Height;
        var maxTiles = 3500;
        _tileStep = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(cellCount / (double)maxTiles)));
    }

    private void CenterAndFit()
    {
        if (_map is null || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            _panX = 0;
            _panY = 0;
            _zoom = 1;
            return;
        }

        var worldWidth = Math.Max(1, _map.Width * BaseCellSize);
        var worldHeight = Math.Max(1, _map.Height * BaseCellSize);
        var zx = Bounds.Width * 0.84 / worldWidth;
        var zy = Bounds.Height * 0.84 / worldHeight;
        _zoom = Clamp(Math.Min(zx, zy), MinZoom, MaxZoom);
        _panX = 0;
        _panY = 0;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private IBrush GetTileBrush(uint hash)
    {
        if (_tileBrushByHash.TryGetValue(hash, out var semanticBrush))
        {
            return semanticBrush;
        }

        if (_generatedTileBrushCache.TryGetValue(hash, out var cached))
        {
            return cached;
        }

        var created = (IBrush)new SolidColorBrush(Color.Parse(HashToColor(hash)));
        _generatedTileBrushCache[hash] = created;
        return created;
    }

    private int GetRenderTileStep()
    {
        var step = _tileStep;
        var pixelsPerTile = BaseCellSize * _zoom * _tileStep;
        if (pixelsPerTile < 1.5)
        {
            step = Math.Max(step, _tileStep * 4);
        }
        else if (pixelsPerTile < 2.5)
        {
            step = Math.Max(step, _tileStep * 2);
        }

        return Math.Max(1, step);
    }

    private void GetVisibleGridBounds(MapProject map, out int minX, out int maxX, out int minY, out int maxY)
    {
        var leftWorld = (0 - Bounds.Width * 0.5 - _panX) / _zoom;
        var rightWorld = (Bounds.Width - Bounds.Width * 0.5 - _panX) / _zoom;
        var topWorld = (0 - Bounds.Height * 0.5 - _panY) / _zoom;
        var bottomWorld = (Bounds.Height - Bounds.Height * 0.5 - _panY) / _zoom;
        var halfWidth = map.Width * 0.5;
        var halfHeight = map.Height * 0.5;
        minX = Math.Clamp((int)Math.Floor(Math.Min(leftWorld, rightWorld) / BaseCellSize + halfWidth) - 2, 0, map.Width - 1);
        maxX = Math.Clamp((int)Math.Ceiling(Math.Max(leftWorld, rightWorld) / BaseCellSize + halfWidth) + 2, 0, map.Width - 1);
        minY = Math.Clamp((int)Math.Floor(Math.Min(topWorld, bottomWorld) / BaseCellSize + halfHeight) - 2, 0, map.Height - 1);
        maxY = Math.Clamp((int)Math.Ceiling(Math.Max(topWorld, bottomWorld) / BaseCellSize + halfHeight) + 2, 0, map.Height - 1);
    }

    private static int AlignDownToStep(int value, int step)
    {
        if (step <= 1)
        {
            return value;
        }

        return value - (value % step);
    }

    private void UpdateCursor()
    {
        Cursor = EditMode switch
        {
            MapEditMode.AddLand => new Cursor(StandardCursorType.None),
            MapEditMode.DeleteLand => new Cursor(StandardCursorType.None),
            _ => new Cursor(StandardCursorType.Arrow)
        };
    }

    private static string HashToColor(uint value)
    {
        var r = (int)((value >> 16) & 0x7F) + 64;
        var g = (int)((value >> 8) & 0x7F) + 64;
        var b = (int)(value & 0x7F) + 64;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string GetObjectIcon(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "(unknown)")
        {
            return "?";
        }

        var n = name.ToLowerInvariant();
        if (n.Contains("tree") || n.Contains("palm"))
        {
            return "T";
        }

        if (n.Contains("rock") || n.Contains("stone"))
        {
            return "R";
        }

        if (n.Contains("weed") || n.Contains("grass") || n.Contains("plant"))
        {
            return "W";
        }

        if (n.Contains("house") || n.Contains("building") || n.Contains("shop") || n.Contains("facility"))
        {
            return "H";
        }

        return name[..1].ToUpperInvariant();
    }
}
