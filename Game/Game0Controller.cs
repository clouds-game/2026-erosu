using ChromaDrop.Core;
using ChromaDrop.Core.Game0;
using Godot;

namespace ChromaDrop.Game;

public partial class Game0Controller : Control
{
  private static readonly Color[] ShapeColors =
  [
    new("30d6ef"), new("458ef2"), new("f5963d"), new("ffe15d"),
    new("59db68"), new("bd82ef"), new("f16670")
  ];
  private readonly Rect2[] _handSlots = new Rect2[Game0Session.HandSize];
  private readonly Rect2[] _catSlots = new Rect2[Game0Session.CatCapacity];
  private Game0Session _session = null!;
  private Texture2D _catImage = null!;
  private Texture2D _frameImage = null!;
  private Texture2D _patternImage = null!;
  private Texture2D _clockwiseImage = null!;
  private Texture2D _counterClockwiseImage = null!;
  private Label _score = null!;
  private Label _over = null!;
  private Button _pause = null!;
  private Button _restart = null!;
  private Rect2 _board;
  private Rect2 _timer;
  private Rect2 _handPanel;
  private Rect2 _sidePanel;
  private float _cell;
  private float _scale;
  private Vector2 _offset;
  private bool _paused;
  private bool _dragging;
  private FallingPiece? _falling;
  private PieceSource _source;
  private int _slot;
  private int _orientation;
  private Vector2 _pointer;
  private bool _canDrop;
  private EntrySide _side;
  private int _lane;
  private int _seed;

  private sealed class FallingPiece
  {
    public required PieceSource Source { get; init; }
    public required int Slot { get; init; }
    public required Shape Shape { get; init; }
    public required EntrySide Side { get; init; }
    public required int Lane { get; init; }
    public required int Orientation { get; init; }
    public required Cell[] Cells { get; init; }
    public required Vector2 Start { get; init; }
    public required Vector2 End { get; init; }
    public required float Duration { get; init; }
    public required bool Lands { get; init; }
    public required bool Blocked { get; init; }
    public float Elapsed { get; set; }
  }

  public override void _Ready()
  {
    TextureFilter = TextureFilterEnum.Nearest;
    _catImage = GD.Load<Texture2D>("res://Assets/Cats/cat-cell.png");
    _frameImage = GD.Load<Texture2D>("res://Assets/PixelUI/Ancient/tan.png");
    _patternImage = GD.Load<Texture2D>("res://Assets/PixelUI/Pattern/quiet.png");
    _clockwiseImage = GD.Load<Texture2D>("res://Assets/Icons/rotate-clockwise.png");
    _counterClockwiseImage = GD.Load<Texture2D>("res://Assets/Icons/rotate-counterclockwise.png");
    _seed = (int)(Time.GetTicksMsec() & 0x7fffffff);
    var args = OS.GetCmdlineUserArgs();
    _session = args.Contains("--demo")
      ? new Game0Session(_seed, initialCats: new Shape?[] { Shape.L })
      : new Game0Session(_seed);
    if (args.Contains("--demo"))
    {
      var i = Enumerable.Range(0, Game0Session.HandSize).First(index => _session.Hand[index] == Shape.I);
      _session.Place(PieceSource.Hand, i, EntrySide.Top, 6, 1);
    }
    if (args.Contains("--lock")) _session.Advance(Game0Session.HandSeconds);
    _score = MakeLabel(34, PixelUi.Cream);
    _over = MakeLabel(45, PixelUi.Cream);
    _over.HorizontalAlignment = HorizontalAlignment.Center;
    _pause = MakeButton("Ⅱ");
    _pause.Pressed += () => { _paused = !_paused; _pause.Text = _paused ? "▶" : "Ⅱ"; QueueRedraw(); };
    _restart = MakeButton("重新开始");
    _restart.Pressed += Restart;
    Layout();
    var capture = args.FirstOrDefault(arg => arg.StartsWith("--capture="));
    if (capture is not null) CaptureAfterDraw(capture["--capture=".Length..]);
  }

  private async void CaptureAfterDraw(string path)
  {
    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    var result = GetViewport().GetTexture().GetImage().SavePng(path);
    if (result != Error.Ok) GD.PushError($"Could not save screenshot: {result}");
    GetTree().Quit(result == Error.Ok ? 0 : 1);
  }

  private void Restart()
  {
    _session = new Game0Session(++_seed);
    _paused = false;
    _dragging = false;
    _falling = null;
    _pause.Text = "Ⅱ";
    _canDrop = false;
    QueueRedraw();
  }

  public override void _Process(double delta)
  {
    Layout();
    if (!_paused)
    {
      if (_falling is { } fall)
      {
        fall.Elapsed += (float)delta;
        if (fall.Elapsed >= fall.Duration)
        {
          _falling = null;
          if (fall.Lands)
            _session.Place(fall.Source, fall.Slot, fall.Side, fall.Lane, fall.Orientation);
        }
      }
      else _session.Advance(delta);
    }
    _score.Text = _session.Score.ToString();
    _over.Visible = _session.IsOver;
    _restart.Visible = _session.IsOver;
    _pause.Disabled = _session.IsOver;
    _over.Text = "GAME OVER";
    if (_dragging) UpdateDropTarget();
    QueueRedraw();
  }

  private void Layout()
  {
    var size = GetViewportRect().Size;
    _scale = Mathf.Min(size.X / 1448, size.Y / 1086);
    _offset = (size - new Vector2(1448, 1086) * _scale) / 2;
    Rect2 R(float x, float y, float w, float h) =>
      new(_offset + new Vector2(x, y) * _scale, new Vector2(w, h) * _scale);
    _board = R(383, 360, 636, 636);
    _cell = _board.Size.X / Game0Session.Size;
    _handPanel = R(158, 137, 1132, 180);
    _sidePanel = R(1050, 520, 272, 500);
    _timer = R(383, 1005, 636, 30);
    for (var i = 0; i < _handSlots.Length; i++)
      _handSlots[i] = R(181 + i * 155, 173, 145, 111);
    for (var i = 0; i < _catSlots.Length; i++)
      _catSlots[i] = R(1080 + (i % 2) * 119, 778 + (i / 2) * 115, 96, 96);
    _score.Position = R(239, 45, 190, 55).Position;
    _score.Size = R(239, 45, 190, 55).Size;
    _pause.Position = R(1327, 38, 88, 85).Position;
    _pause.Size = R(1327, 38, 88, 85).Size;
    _over.Position = R(383, 624, 636, 70).Position;
    _over.Size = R(383, 624, 636, 70).Size;
    _restart.Position = R(610, 712, 180, 58).Position;
    _restart.Size = R(610, 712, 180, 58).Size;
  }

  public override void _Input(InputEvent input)
  {
    if (input is InputEventKey key && key.Pressed && !key.Echo)
    {
      if (key.Keycode == Key.Escape)
      {
        if (_dragging) { _dragging = false; _canDrop = false; }
        else if (!_session.IsOver) { _paused = !_paused; _pause.Text = _paused ? "▶" : "Ⅱ"; }
      }
      else if (_dragging && key.Keycode is Key.R or Key.Up or Key.Space)
      {
        _orientation = (_orientation + 1) % 4;
        UpdateDropTarget();
      }
      else if (key.Keycode == Key.F5) Restart();
      return;
    }
    if (input is InputEventMouseMotion motion)
    {
      _pointer = motion.Position;
      if (_dragging) UpdateDropTarget();
      return;
    }
    if (input is not InputEventMouseButton mouse || _paused || _session.IsOver || _falling is not null) return;
    _pointer = mouse.Position;
    if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed && _dragging)
    {
      _orientation = (_orientation + 1) % 4;
      UpdateDropTarget();
      return;
    }
    if (mouse.ButtonIndex != MouseButton.Left) return;
    if (mouse.Pressed)
    {
      for (var i = 0; i < _handSlots.Length; i++)
        if (_handSlots[i].HasPoint(_pointer) && _session.CanSelect(PieceSource.Hand, i))
        {
          BeginDrag(PieceSource.Hand, i);
          return;
        }
      for (var i = 0; i < _catSlots.Length; i++)
        if (_catSlots[i].HasPoint(_pointer) && _session.CanSelect(PieceSource.Cat, i))
        {
          BeginDrag(PieceSource.Cat, i);
          return;
        }
    }
    else if (_dragging)
    {
      UpdateDropTarget();
      if (_canDrop) StartFall();
      _dragging = false;
      _canDrop = false;
    }
  }

  private void BeginDrag(PieceSource source, int slot)
  {
    _dragging = true;
    _source = source;
    _slot = slot;
    _orientation = 0;
    UpdateDropTarget();
  }

  private void UpdateDropTarget()
  {
    if (!_dragging || !_session.CanSelect(_source, _slot)) { _canDrop = false; return; }
    var shape = _source == PieceSource.Hand ? _session.Hand[_slot]!.Value : _session.Cats[_slot]!.Value;
    var cells = Game0Session.ShapeCells(shape, _orientation);
    var width = cells.Max(cell => cell.X) + 1;
    var height = cells.Max(cell => cell.Y) + 1;
    _side = _source == PieceSource.Hand ? EntrySide.Top : NearestSide(_pointer);
    if (_source == PieceSource.Hand)
    {
      // Any board column may be released, even when the piece will pass through and return.
      _canDrop = _pointer.X >= _board.Position.X && _pointer.X <= _board.End.X &&
        _pointer.Y >= _board.Position.Y - _cell * 1.5f && _pointer.Y <= _board.End.Y;
      _lane = Mathf.Clamp(Mathf.RoundToInt((_pointer.X - _board.Position.X) / _cell - width / 2f),
        0, Game0Session.Size - width);
      return;
    }
    var edgeDistance = _side switch
    {
      EntrySide.Top => Mathf.Abs(_pointer.Y - _board.Position.Y),
      EntrySide.Bottom => Mathf.Abs(_pointer.Y - _board.End.Y),
      EntrySide.Left => Mathf.Abs(_pointer.X - _board.Position.X),
      _ => Mathf.Abs(_pointer.X - _board.End.X)
    };
    var alongEdge = _side is EntrySide.Top or EntrySide.Bottom
      ? _pointer.X >= _board.Position.X - _cell && _pointer.X <= _board.End.X + _cell
      : _pointer.Y >= _board.Position.Y - _cell && _pointer.Y <= _board.End.Y + _cell;
    if (edgeDistance > _cell * 1.5f || !alongEdge) { _canDrop = false; return; }
    _lane = _side is EntrySide.Top or EntrySide.Bottom
      ? Mathf.RoundToInt((_pointer.X - _board.Position.X) / _cell - width / 2f)
      : Mathf.RoundToInt((_pointer.Y - _board.Position.Y) / _cell - height / 2f);
    _lane = Mathf.Clamp(_lane, 0, Game0Session.Size - (_side is EntrySide.Top or EntrySide.Bottom ? width : height));
    _canDrop = true;
  }

  private void StartFall()
  {
    var shape = _source == PieceSource.Hand ? _session.Hand[_slot]!.Value : _session.Cats[_slot]!.Value;
    var cells = Game0Session.ShapeCells(shape, _orientation);
    var width = cells.Max(cell => cell.X) + 1;
    var height = cells.Max(cell => cell.Y) + 1;
    var trace = _session.Trace(_source, _slot, _side, _lane, _orientation);
    if (trace is null) return;
    var start = _side switch
    {
      EntrySide.Top => new Vector2(_lane, -height),
      EntrySide.Bottom => new Vector2(_lane, Game0Session.Size),
      EntrySide.Left => new Vector2(-width, _lane),
      _ => new Vector2(Game0Session.Size, _lane)
    };
    var end = new Vector2(trace.EndOrigin.X, trace.EndOrigin.Y);
    var blocked = trace.HitStructure && trace.Landing is null;
    _falling = new FallingPiece
    {
      Source = _source, Slot = _slot, Shape = shape, Side = _side, Lane = _lane,
      Orientation = _orientation, Cells = cells, Start = start, End = end,
      Duration = trace.Landing is null
        ? blocked ? Mathf.Clamp(start.DistanceTo(end) * 0.035f, 0.2f, 0.4f)
          : Mathf.Clamp(start.DistanceTo(end) * 0.038f, 0.4f, 0.58f)
        : Mathf.Clamp(start.DistanceTo(end) * 0.035f, 0.2f, 0.4f),
      Lands = trace.Landing is not null, Blocked = blocked
    };
  }

  private EntrySide NearestSide(Vector2 position)
  {
    var distances = new[]
    {
      Mathf.Abs(position.Y - _board.Position.Y),
      Mathf.Abs(position.X - _board.End.X),
      Mathf.Abs(position.Y - _board.End.Y),
      Mathf.Abs(position.X - _board.Position.X)
    };
    return (EntrySide)Array.IndexOf(distances, distances.Min());
  }

  public override void _Draw()
  {
    DrawRect(new Rect2(Vector2.Zero, Size), new Color("246ab3"));
    for (var y = 0f; y < Size.Y; y += 48)
      for (var x = 0f; x < Size.X; x += 48)
        DrawTextureRect(_patternImage, new Rect2(x, y, 48, 48), false,
          new Color(0.12f, 0.25f, 0.43f, 0.04f));
    DrawPanel(_handPanel);
    for (var i = 0; i < _handSlots.Length; i++)
    {
      if (_session.Hand[i] is not { } shape) continue;
      if (_session.Locked && _session.CanSelect(PieceSource.Hand, i))
        DrawRect(_handSlots[i].Grow(2 * _scale), PixelUi.Cream, false, 3 * _scale);
      var inMotion = _dragging && _source == PieceSource.Hand && _slot == i ||
        _falling is { Source: PieceSource.Hand } handFall && handFall.Slot == i;
      DrawMiniShape(shape, false, _handSlots[i], inMotion ? 0.4f : 1);
    }
    DrawRect(_board, new Color("112e50"));
    for (var y = 0; y < Game0Session.Size; y++)
      for (var x = 0; x < Game0Session.Size; x++)
      {
        var rect = new Rect2(_board.Position + new Vector2(x, y) * _cell, Vector2.One * _cell);
        if ((x + y) % 2 == 1) DrawRect(rect, new Color("193b62"));
        DrawRect(rect, new Color("4a719a", 0.45f), false, 1);
      }
    foreach (var (cell, tile) in _session.Tiles) DrawBoardTile(cell, tile.Shape, tile.Cat, 1);
    DrawTile(_board.Position + Vector2.One * Game0Session.Center * _cell, _cell,
      PixelUi.Cream, 1, false);
    if (_falling is { } fall)
    {
      if (fall.Blocked && fall.Side == EntrySide.Top)
      {
        var width = fall.Cells.Max(cell => cell.X) + 1;
        DrawRect(new Rect2(_board.Position.X + fall.Lane * _cell,
          _board.Position.Y - 5 * _scale, width * _cell, 5 * _scale),
          new Color("f16670"));
      }
      var origin = fall.Start.Lerp(fall.End, Mathf.Min(1, fall.Elapsed / fall.Duration));
      foreach (var cell in fall.Cells)
      {
        var position = _board.Position + (origin + new Vector2(cell.X, cell.Y)) * _cell;
        if (position.X + _cell <= _board.Position.X || position.X >= _board.End.X ||
            position.Y + _cell <= _board.Position.Y || position.Y >= _board.End.Y) continue;
        DrawTile(position, _cell, ColorFor(fall.Shape), 1, fall.Source == PieceSource.Cat);
      }
    }
    if (_dragging)
    {
      if (_source == PieceSource.Hand && _canDrop)
      {
        var selected = _session.Hand[_slot]!.Value;
        var width = Game0Session.ShapeCells(selected, _orientation).Max(cell => cell.X) + 1;
        var entryLeft = _board.Position.X + _lane * _cell;
        var entryWidth = width * _cell;
        DrawRect(new Rect2(entryLeft, _board.Position.Y - 5 * _scale,
          entryWidth, 5 * _scale), new Color("30d6ef"));
        var arrowSize = 30 * _scale;
        DrawTextureRectRegion(PixelUi.Sheet,
          new Rect2(entryLeft + (entryWidth - arrowSize) / 2,
            _board.Position.Y - 37 * _scale, arrowSize, arrowSize),
          new Rect2(414, 453, 16, 16));
      }
      var held = _source == PieceSource.Hand ? _session.Hand[_slot]!.Value : _session.Cats[_slot]!.Value;
      var heldCells = Game0Session.ShapeCells(held, _orientation);
      foreach (var cell in heldCells)
        DrawTile(_pointer + new Vector2(cell.X - 1, cell.Y - 1) * _cell * 0.7f,
          _cell * 0.7f, ColorFor(held), 0.8f, _source == PieceSource.Cat);
    }
    DrawPanel(_sidePanel);
    DrawLine(_offset + new Vector2(1079, 645) * _scale,
      _offset + new Vector2(1292, 645) * _scale, new Color("52769a"), 3 * _scale);
    DrawLine(_offset + new Vector2(1079, 753) * _scale,
      _offset + new Vector2(1292, 753) * _scale, new Color("52769a"), 3 * _scale);
    DrawTextureRect(_session.ClockwiseNext ? _clockwiseImage : _counterClockwiseImage,
      new Rect2(_offset + new Vector2(1082, 550) * _scale, Vector2.One * 84 * _scale), false);
    DrawTextureRectRegion(PixelUi.Sheet,
      new Rect2(_offset + new Vector2(1144, 656) * _scale, new Vector2(84, 84) * _scale),
      _session.ClockwiseNext ? new Rect2(414, 469, 16, 16) : new Rect2(398, 469, 16, 16));
    for (var i = 0; i < Game0Session.RotationPeriod; i++)
      DrawCircle(_offset + new Vector2(1192 + i * 28, 590) * _scale, 10 * _scale,
        i < _session.UntilRotation ? PixelUi.Gold : new Color("274c75"));
    foreach (var slot in _catSlots)
    {
      DrawRect(slot, new Color("16395f"));
      DrawRect(slot, new Color("7d9bbc"), false, 3 * _scale);
    }
    for (var i = 0; i < _catSlots.Length; i++)
      if (_session.Cats[i] is { } shape)
      {
        var inMotion = _dragging && _source == PieceSource.Cat && _slot == i ||
          _falling is { Source: PieceSource.Cat } catFall && catFall.Slot == i;
        DrawMiniShape(shape, true, _catSlots[i], inMotion ? 0.4f : 1);
      }
    DrawRect(_timer, _session.Locked ? new Color("f32735") : new Color("133553"));
    if (!_session.Locked)
      DrawRect(new Rect2(_timer.Position, new Vector2(_timer.Size.X * (float)(_session.HandTimeLeft / Game0Session.HandSeconds), _timer.Size.Y)),
        new Color("23e344"));
    DrawRect(_timer, new Color("07182a"), false, 5 * _scale);
    if (_paused) DrawRect(_board, new Color(0, 0, 0, 0.5f));
    if (_session.IsOver) DrawRect(_board, new Color(0, 0, 0, 0.65f));
  }

  private void DrawPanel(Rect2 destination)
  {
    // Reuse the project's Kenney Ancient frame with stable pixel-sized corners.
    const float sourceEdge = 8;
    var targetEdge = 13 * _scale;
    var x = new[] { destination.Position.X, destination.Position.X + targetEdge,
      destination.End.X - targetEdge, destination.End.X };
    var y = new[] { destination.Position.Y, destination.Position.Y + targetEdge,
      destination.End.Y - targetEdge, destination.End.Y };
    var source = new[] { 0f, sourceEdge, 48 - sourceEdge, 48f };
    for (var row = 0; row < 3; row++)
      for (var column = 0; column < 3; column++)
        DrawTextureRectRegion(_frameImage,
          new Rect2(x[column], y[row], x[column + 1] - x[column], y[row + 1] - y[row]),
          new Rect2(source[column], source[row], source[column + 1] - source[column],
            source[row + 1] - source[row]));
    DrawRect(destination.Grow(-targetEdge), new Color("0d2948"));
  }

  private void DrawBoardTile(Cell cell, Shape shape, bool cat, float alpha) =>
    DrawTile(_board.Position + new Vector2(cell.X, cell.Y) * _cell, _cell, ColorFor(shape), alpha, cat);

  private void DrawMiniShape(Shape shape, bool cat, Rect2 slot, float alpha)
  {
    var cells = Game0Session.ShapeCells(shape, 0);
    var width = cells.Max(cell => cell.X) + 1;
    var height = cells.Max(cell => cell.Y) + 1;
    var padding = cat ? 4 : 12;
    var tile = Mathf.Min(cat ? 31 : 23,
      Mathf.Min((slot.Size.X - padding) / width, (slot.Size.Y - padding) / height));
    var origin = slot.Position + (slot.Size - new Vector2(width, height) * tile) / 2;
    foreach (var cell in cells) DrawTile(origin + new Vector2(cell.X, cell.Y) * tile, tile, ColorFor(shape), alpha, cat);
  }

  private void DrawTile(Vector2 position, float size, Color tint, float alpha, bool cat)
  {
    if (cat) tint = new Color("f5963d");
    DrawTextureRectRegion(PixelUi.Sheet, new Rect2(position, Vector2.One * size),
      PixelUi.WhiteTileRegion, new Color(tint.R, tint.G, tint.B, alpha));
    if (cat) DrawTextureRect(_catImage,
      new Rect2(position + Vector2.One * (size * 0.025f), Vector2.One * (size * 0.95f)),
      false, new Color(1, 1, 1, alpha));
  }

  private static Color ColorFor(Shape shape) => ShapeColors[(int)shape];

  private Label MakeLabel(int fontSize, Color color)
  {
    var label = new Label { MouseFilter = MouseFilterEnum.Ignore };
    label.AddThemeFontSizeOverride("font_size", fontSize);
    label.AddThemeColorOverride("font_color", color);
    AddChild(label);
    return label;
  }

  private Button MakeButton(string text)
  {
    var button = new Button { Text = text };
    button.AddThemeFontSizeOverride("font_size", 24);
    AddChild(button);
    return button;
  }
}
