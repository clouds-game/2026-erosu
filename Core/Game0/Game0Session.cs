using ChromaDrop.Core;

namespace ChromaDrop.Core.Game0;

public enum EntrySide { Top, Right, Bottom, Left }
public enum PieceSource { Hand, Cat }
public sealed record PlacedCell(Shape Shape, bool Cat);
public sealed record DropPreview(IReadOnlyList<Cell> Cells, IReadOnlyList<int> Rings);
public sealed record DropTrace(Cell EndOrigin, DropPreview? Landing, bool HitStructure);
public sealed record PlaceResult(bool Accepted, int Rings, int Points, bool CatEarned);

/// <summary>The complete, deterministic rules for the center-ring prototype.</summary>
public sealed class Game0Session
{
  public const int Size = 13;
  public const int Center = Size / 2;
  public const int HandSize = 7;
  public const int CatCapacity = 4;
  public const int RotationPeriod = 4;
  public const double HandSeconds = 75;

  private static readonly Cell[] Directions = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];
  private static readonly Shape[] AllShapes = [Shape.I, Shape.J, Shape.L, Shape.O, Shape.S, Shape.T, Shape.Z];
  private readonly Dictionary<Cell, PlacedCell> _tiles = [];
  private readonly Random _random;
  private readonly Shape?[] _hand = new Shape?[HandSize];
  private readonly Shape?[] _cats = new Shape?[CatCapacity];

  public IReadOnlyDictionary<Cell, PlacedCell> Tiles => _tiles;
  public IReadOnlyList<Shape?> Hand => _hand;
  public IReadOnlyList<Shape?> Cats => _cats;
  public int Score { get; private set; }
  public int TotalRings { get; private set; }
  public int SuccessfulPlacements { get; private set; }
  public int UntilRotation { get; private set; } = RotationPeriod;
  public bool ClockwiseNext { get; private set; } = true;
  public bool Locked { get; private set; }
  public bool IsOver { get; private set; }
  public double HandTimeLeft { get; private set; } = HandSeconds;
  public int HandNumber { get; private set; }

  public Game0Session(int seed = 0, IReadOnlyDictionary<Cell, PlacedCell>? initialTiles = null,
    IReadOnlyList<Shape?>? initialCats = null)
  {
    _random = new Random(seed);
    if (initialTiles is not null)
      foreach (var (cell, tile) in initialTiles)
      {
        if (!Inside(cell) || cell == new Cell(Center, Center))
          throw new ArgumentException("Scenario tile is outside the playable ring.", nameof(initialTiles));
        _tiles.Add(cell, tile);
      }
    if (initialCats is not null)
    {
      if (initialCats.Count > CatCapacity) throw new ArgumentException("Too many scenario cats.", nameof(initialCats));
      for (var i = 0; i < initialCats.Count; i++) _cats[i] = initialCats[i];
    }
    NewHand();
  }

  public void Advance(double seconds)
  {
    if (IsOver || Locked || seconds <= 0) return;
    HandTimeLeft = Math.Max(0, HandTimeLeft - seconds);
    if (HandTimeLeft > 0) return;
    Locked = true;
    CheckGameOver();
  }

  public bool CanSelect(PieceSource source, int index)
  {
    if (IsOver) return false;
    if (source == PieceSource.Cat) return index >= 0 && index < CatCapacity && _cats[index].HasValue;
    return index >= 0 && index < HandSize && _hand[index].HasValue &&
      (!Locked || index == Array.FindIndex(_hand, shape => shape.HasValue));
  }

  public DropPreview? Preview(PieceSource source, int index, EntrySide side, int lane, int rotation) =>
    Trace(source, index, side, lane, rotation)?.Landing;

  public DropTrace? Trace(PieceSource source, int index, EntrySide side, int lane, int rotation)
  {
    if (!CanSelect(source, index) || source == PieceSource.Hand && side != EntrySide.Top) return null;
    var shape = source == PieceSource.Hand ? _hand[index]!.Value : _cats[index]!.Value;
    var cells = ShapeCells(shape, rotation);
    var width = cells.Max(cell => cell.X) + 1;
    var height = cells.Max(cell => cell.Y) + 1;
    var limit = side is EntrySide.Top or EntrySide.Bottom ? Size - width : Size - height;
    if (lane < 0 || lane > limit) return null;

    Cell[]? lastInside = null;
    Cell? lastFreeOrigin = null;
    for (var step = 0; step < Size + Math.Max(width, height) + 1; step++)
    {
      var origin = side switch
      {
        EntrySide.Top => new Cell(lane, -height + step),
        EntrySide.Bottom => new Cell(lane, Size - step),
        EntrySide.Left => new Cell(-width + step, lane),
        _ => new Cell(Size - step, lane)
      };
      var placed = cells.Select(cell => cell.Offset(origin.X, origin.Y)).ToArray();
      if (placed.Any(cell => _tiles.ContainsKey(cell) || cell == new Cell(Center, Center)))
      {
        if (lastInside is null || !lastInside.Any(TouchesStructure))
          return new DropTrace(lastFreeOrigin ?? origin, null, true);
        var occupied = _tiles.Keys.Concat(lastInside).ToHashSet();
        return new DropTrace(lastFreeOrigin!.Value,
          new DropPreview(lastInside, FullRings(occupied)), true);
      }
      if (placed.All(Inside)) lastInside = placed;
      else if (lastInside is not null)
      {
        var beyond = side switch
        {
          EntrySide.Top => new Cell(lane, Size),
          EntrySide.Bottom => new Cell(lane, -height),
          EntrySide.Left => new Cell(Size, lane),
          _ => new Cell(-width, lane)
        };
        return new DropTrace(beyond, null, false);
      }
      lastFreeOrigin = origin;
    }
    return null;
  }

  public PlaceResult Place(PieceSource source, int index, EntrySide side, int lane, int rotation)
  {
    var preview = Preview(source, index, side, lane, rotation);
    if (preview is null) return new PlaceResult(false, 0, 0, false);
    var shape = source == PieceSource.Hand ? _hand[index]!.Value : _cats[index]!.Value;
    foreach (var cell in preview.Cells) _tiles.Add(cell, new PlacedCell(shape, source == PieceSource.Cat));
    if (source == PieceSource.Hand) _hand[index] = null;
    else _cats[index] = null;

    var rings = 0;
    var points = 0;
    var firstWave = true;
    var catEarned = false;
    while (true)
    {
      var full = FullRings(_tiles.Keys.ToHashSet());
      if (full.Count == 0) break;
      rings += full.Count;
      points += 50 * full.Count * (full.Count + 1);
      if (firstWave && full.Count >= 2 && Array.Exists(_cats, cat => !cat.HasValue))
      {
        _cats[Array.FindIndex(_cats, cat => !cat.HasValue)] = AllShapes[_random.Next(AllShapes.Length)];
        catEarned = true;
      }
      firstWave = false;
      foreach (var cell in _tiles.Keys.Where(cell => full.Contains(Radius(cell))).ToArray()) _tiles.Remove(cell);
      Settle();
    }
    Score += points;
    TotalRings += rings;
    SuccessfulPlacements++;
    UntilRotation--;
    if (UntilRotation == 0)
    {
      RotateBoard();
      UntilRotation = RotationPeriod;
      // The first prototype uses a stable rhythm; the direction is announced.
      ClockwiseNext = !ClockwiseNext;
    }
    if (_hand.All(shape => !shape.HasValue)) NewHand();
    CheckGameOver();
    return new PlaceResult(true, rings, points, catEarned);
  }

  public bool HasLegalPlacement(PieceSource source, int index)
  {
    if (!CanSelect(source, index)) return false;
    var sides = source == PieceSource.Hand ? [EntrySide.Top] : Enum.GetValues<EntrySide>();
    foreach (var side in sides)
      for (var rotation = 0; rotation < 4; rotation++)
        for (var lane = 0; lane < Size; lane++)
          if (Preview(source, index, side, lane, rotation) is not null) return true;
    return false;
  }

  private void CheckGameOver()
  {
    if (Enumerable.Range(0, HandSize).Any(index => HasLegalPlacement(PieceSource.Hand, index))) return;
    // Cats are a final chance to reconnect a playable position, even during Lock.
    IsOver = !Enumerable.Range(0, CatCapacity).Any(index => HasLegalPlacement(PieceSource.Cat, index));
  }

  private void NewHand()
  {
    var shapes = AllShapes.ToArray();
    for (var i = shapes.Length - 1; i > 0; i--)
    {
      var j = _random.Next(i + 1);
      (shapes[i], shapes[j]) = (shapes[j], shapes[i]);
    }
    for (var i = 0; i < HandSize; i++) _hand[i] = shapes[i];
    HandNumber++;
    HandTimeLeft = HandSeconds;
    Locked = false;
  }

  private void RotateBoard()
  {
    var rotated = _tiles.ToArray();
    _tiles.Clear();
    foreach (var (cell, tile) in rotated)
    {
      var target = ClockwiseNext ? new Cell(Size - 1 - cell.Y, cell.X) : new Cell(cell.Y, Size - 1 - cell.X);
      _tiles.Add(target, tile);
    }
  }

  private void Settle()
  {
    // Every move strictly reduces distance to the center, so resolution terminates.
    for (var step = 0; step < Size * Size * Size; step++)
    {
      var moved = false;
      foreach (var component in Components().OrderBy(group => group.Min(Distance)))
      {
        if (component.Any(cell => Directions.Any(direction => cell.Offset(direction.X, direction.Y) == new Cell(Center, Center))))
          continue;
        var owned = component.ToHashSet();
        var currentDistance = component.Sum(Distance);
        var option = Directions.Select(direction => new
        {
          Direction = direction,
          Targets = component.Select(cell => cell.Offset(direction.X, direction.Y)).ToArray()
        }).Where(candidate => candidate.Targets.All(cell => Inside(cell) && cell != new Cell(Center, Center) &&
          (!_tiles.ContainsKey(cell) || owned.Contains(cell))))
          .Select(candidate => new { candidate.Direction, candidate.Targets, Gain = currentDistance - candidate.Targets.Sum(Distance) })
          .Where(candidate => candidate.Gain > 0).OrderByDescending(candidate => candidate.Gain).FirstOrDefault();
        if (option is null) continue;
        var contents = component.Select(cell => _tiles[cell]).ToArray();
        foreach (var cell in component) _tiles.Remove(cell);
        for (var i = 0; i < option.Targets.Length; i++) _tiles.Add(option.Targets[i], contents[i]);
        moved = true;
        break;
      }
      if (!moved) return;
    }
    throw new InvalidOperationException("Center settling failed to converge.");
  }

  private IReadOnlyList<Cell[]> Components()
  {
    var unseen = _tiles.Keys.ToHashSet();
    var result = new List<Cell[]>();
    while (unseen.Count > 0)
    {
      var start = unseen.MinBy(cell => cell.Y * Size + cell.X);
      var group = new List<Cell> { start };
      unseen.Remove(start);
      for (var i = 0; i < group.Count; i++)
        foreach (var direction in Directions)
        {
          var neighbor = group[i].Offset(direction.X, direction.Y);
          if (unseen.Remove(neighbor)) group.Add(neighbor);
        }
      result.Add(group.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray());
    }
    return result;
  }

  private bool TouchesStructure(Cell cell) => Directions.Any(direction =>
  {
    var neighbor = cell.Offset(direction.X, direction.Y);
    return neighbor == new Cell(Center, Center) || _tiles.ContainsKey(neighbor);
  });

  private static bool Inside(Cell cell) => cell.X >= 0 && cell.X < Size && cell.Y >= 0 && cell.Y < Size;
  private static int Distance(Cell cell) => Math.Abs(cell.X - Center) + Math.Abs(cell.Y - Center);
  private static int Radius(Cell cell) => Math.Max(Math.Abs(cell.X - Center), Math.Abs(cell.Y - Center));

  private static IReadOnlyList<int> FullRings(HashSet<Cell> occupied)
  {
    var full = new List<int>();
    for (var radius = 1; radius <= Center; radius++)
    {
      var complete = true;
      for (var y = Center - radius; y <= Center + radius && complete; y++)
        for (var x = Center - radius; x <= Center + radius; x++)
          if (Math.Max(Math.Abs(x - Center), Math.Abs(y - Center)) == radius && !occupied.Contains(new Cell(x, y)))
          {
            complete = false;
            break;
          }
      if (complete) full.Add(radius);
    }
    return full;
  }

  public static Cell[] ShapeCells(Shape shape, int rotation)
  {
    var cells = Piece.Create(0, shape, 0).Cells.ToArray();
    for (var i = 0; i < ((rotation % 4) + 4) % 4; i++)
    {
      var maxY = cells.Max(cell => cell.Y);
      cells = cells.Select(cell => new Cell(maxY - cell.Y, cell.X)).ToArray();
      var minX = cells.Min(cell => cell.X);
      var minY = cells.Min(cell => cell.Y);
      cells = cells.Select(cell => cell.Offset(-minX, -minY)).ToArray();
    }
    return cells;
  }
}
