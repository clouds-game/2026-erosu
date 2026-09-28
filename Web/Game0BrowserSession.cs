using ChromaDrop.Core;
using ChromaDrop.Core.Game0;

namespace ChromaDrop.Web;

public readonly record struct Game0Point(double X, double Y)
{
  public Game0Point Lerp(Game0Point target, double amount) =>
    new(X + (target.X - X) * amount, Y + (target.Y - Y) * amount);
  public double DistanceTo(Game0Point target) =>
    Math.Sqrt(Math.Pow(target.X - X, 2) + Math.Pow(target.Y - Y, 2));
}

public readonly record struct Game0Rect(double X, double Y, double Width, double Height)
{
  public double Right => X + Width;
  public double Bottom => Y + Height;
  public bool Contains(Game0Point point) =>
    point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
}

public sealed class Game0FallingPiece
{
  public required PieceSource Source { get; init; }
  public required int Slot { get; init; }
  public required Shape Shape { get; init; }
  public required EntrySide Side { get; init; }
  public required int Lane { get; init; }
  public required int Orientation { get; init; }
  public required IReadOnlyList<Cell> Cells { get; init; }
  public required Game0Point Start { get; init; }
  public required Game0Point End { get; init; }
  public required double Duration { get; init; }
  public required bool Lands { get; init; }
  public required bool Blocked { get; init; }
  public double Elapsed { get; internal set; }
  public Game0Point Origin => Start.Lerp(End, Math.Min(1, Elapsed / Duration));
}

/// <summary>Browser-independent input and animation for the native Game0 screen.</summary>
public sealed class Game0BrowserSession
{
  public const double Width = 1448;
  public const double Height = 1086;
  public const double CellSize = 636.0 / Game0Session.Size;
  public static Game0Rect Board { get; } = new(383, 360, 636, 636);
  public static Game0Rect HandPanel { get; } = new(158, 137, 1132, 180);
  public static Game0Rect SidePanel { get; } = new(1050, 520, 272, 500);
  public static Game0Rect Timer { get; } = new(383, 1005, 636, 30);
  private int _seed;

  public Game0Session Session { get; private set; }
  public bool Paused { get; private set; }
  public bool Dragging { get; private set; }
  public Game0FallingPiece? Falling { get; private set; }
  public PieceSource Source { get; private set; }
  public int Slot { get; private set; }
  public int Orientation { get; private set; }
  public Game0Point Pointer { get; private set; }
  public bool CanDrop { get; private set; }
  public EntrySide Side { get; private set; }
  public int Lane { get; private set; }
  public Shape? SelectedShape => Source == PieceSource.Hand ? Session.Hand[Slot] : Session.Cats[Slot];

  public Game0BrowserSession(int seed = 0, Game0Session? session = null)
  {
    _seed = seed;
    Session = session ?? new Game0Session(seed);
  }

  public static Game0Rect HandSlot(int index) => new(181 + index * 155, 173, 145, 111);
  public static Game0Rect CatSlot(int index) => new(1080 + index % 2 * 119, 778 + index / 2 * 115, 96, 96);

  public void Restart()
  {
    Session = new Game0Session(unchecked(++_seed));
    Paused = false;
    Falling = null;
    CancelDrag();
    Source = PieceSource.Hand;
    Slot = 0;
    Orientation = 0;
  }

  public void TogglePause()
  {
    if (Session.IsOver) return;
    Paused = !Paused;
    if (Paused) CancelDrag();
  }

  public void PauseForFocus()
  {
    CancelDrag();
    if (!Session.IsOver) Paused = true;
  }

  public void Advance(double seconds)
  {
    if (Paused || !double.IsFinite(seconds) || seconds <= 0) return;
    if (Falling is { } fall)
    {
      fall.Elapsed += seconds;
      if (fall.Elapsed >= fall.Duration)
      {
        Falling = null;
        if (fall.Lands) Session.Place(fall.Source, fall.Slot, fall.Side, fall.Lane, fall.Orientation);
      }
    }
    else Session.Advance(seconds);
    if (Dragging)
    {
      if (!Session.CanSelect(Source, Slot)) CancelDrag();
      else UpdateDropTarget();
    }
  }

  public void KeyChanged(string key, bool pressed)
  {
    if (!pressed) return;
    if (key == "Escape")
    {
      if (Dragging) CancelDrag();
      else TogglePause();
    }
    else if (key == "F5") Restart();
    else if (Dragging && key is "KeyR" or "ArrowUp" or "Space") RotateHeldPiece();
  }

  public void PointerChanged(string phase, double x, double y, int button)
  {
    if (phase == "cancel") { CancelDrag(); return; }
    if (!double.IsFinite(x) || !double.IsFinite(y)) return;
    Pointer = new(x, y);
    if (phase == "move")
    {
      if (Dragging) UpdateDropTarget();
      return;
    }
    if (Paused || Session.IsOver || Falling is not null) return;
    if (phase == "down" && button == 2 && Dragging) { RotateHeldPiece(); return; }
    if (button != 0) return;
    if (phase == "down")
    {
      for (var i = 0; i < Game0Session.HandSize; i++)
        if (HandSlot(i).Contains(Pointer) && Session.CanSelect(PieceSource.Hand, i))
        {
          BeginDrag(PieceSource.Hand, i);
          return;
        }
      for (var i = 0; i < Game0Session.CatCapacity; i++)
        if (CatSlot(i).Contains(Pointer) && Session.CanSelect(PieceSource.Cat, i))
        {
          BeginDrag(PieceSource.Cat, i);
          return;
        }
    }
    else if (phase == "up" && Dragging)
    {
      UpdateDropTarget();
      if (CanDrop) StartFall();
      CancelDrag();
    }
  }

  private void BeginDrag(PieceSource source, int slot)
  {
    Dragging = true;
    Source = source;
    Slot = slot;
    Orientation = 0;
    UpdateDropTarget();
  }

  private void CancelDrag() { Dragging = false; CanDrop = false; }

  private void RotateHeldPiece()
  {
    Orientation = (Orientation + 1) % 4;
    UpdateDropTarget();
  }

  private void UpdateDropTarget()
  {
    if (!Dragging || !Session.CanSelect(Source, Slot) || SelectedShape is not { } shape)
    {
      CancelDrag();
      return;
    }
    var cells = Game0Session.ShapeCells(shape, Orientation);
    var width = cells.Max(cell => cell.X) + 1;
    var height = cells.Max(cell => cell.Y) + 1;
    Side = Source == PieceSource.Hand ? EntrySide.Top : NearestSide(Pointer);
    if (Source == PieceSource.Hand)
    {
      CanDrop = Pointer.X >= Board.X && Pointer.X <= Board.Right &&
        Pointer.Y >= Board.Y - CellSize * 1.5 && Pointer.Y <= Board.Bottom;
      Lane = Math.Clamp(Round((Pointer.X - Board.X) / CellSize - width / 2.0), 0, Game0Session.Size - width);
      return;
    }
    var edgeDistance = Side switch
    {
      EntrySide.Top => Math.Abs(Pointer.Y - Board.Y),
      EntrySide.Bottom => Math.Abs(Pointer.Y - Board.Bottom),
      EntrySide.Left => Math.Abs(Pointer.X - Board.X),
      _ => Math.Abs(Pointer.X - Board.Right)
    };
    var alongEdge = Side is EntrySide.Top or EntrySide.Bottom
      ? Pointer.X >= Board.X - CellSize && Pointer.X <= Board.Right + CellSize
      : Pointer.Y >= Board.Y - CellSize && Pointer.Y <= Board.Bottom + CellSize;
    CanDrop = edgeDistance <= CellSize * 1.5 && alongEdge;
    if (!CanDrop) return;
    var lane = Side is EntrySide.Top or EntrySide.Bottom
      ? (Pointer.X - Board.X) / CellSize - width / 2.0
      : (Pointer.Y - Board.Y) / CellSize - height / 2.0;
    Lane = Math.Clamp(Round(lane), 0, Game0Session.Size - (Side is EntrySide.Top or EntrySide.Bottom ? width : height));
  }

  private void StartFall()
  {
    if (SelectedShape is not { } shape) return;
    var cells = Game0Session.ShapeCells(shape, Orientation);
    var width = cells.Max(cell => cell.X) + 1;
    var height = cells.Max(cell => cell.Y) + 1;
    var trace = Session.Trace(Source, Slot, Side, Lane, Orientation);
    if (trace is null) return;
    var start = Side switch
    {
      EntrySide.Top => new Game0Point(Lane, -height),
      EntrySide.Bottom => new Game0Point(Lane, Game0Session.Size),
      EntrySide.Left => new Game0Point(-width, Lane),
      _ => new Game0Point(Game0Session.Size, Lane)
    };
    var end = new Game0Point(trace.EndOrigin.X, trace.EndOrigin.Y);
    var blocked = trace.HitStructure && trace.Landing is null;
    var lands = trace.Landing is not null;
    Falling = new Game0FallingPiece
    {
      Source = Source, Slot = Slot, Shape = shape, Side = Side, Lane = Lane,
      Orientation = Orientation, Cells = cells, Start = start, End = end,
      Duration = lands || blocked ? Math.Clamp(start.DistanceTo(end) * 0.035, 0.2, 0.4)
        : Math.Clamp(start.DistanceTo(end) * 0.038, 0.4, 0.58),
      Lands = lands, Blocked = blocked
    };
  }

  private static EntrySide NearestSide(Game0Point pointer)
  {
    var distances = new[] { Math.Abs(pointer.Y - Board.Y), Math.Abs(pointer.X - Board.Right),
      Math.Abs(pointer.Y - Board.Bottom), Math.Abs(pointer.X - Board.X) };
    return (EntrySide)Array.IndexOf(distances, distances.Min());
  }

  private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
