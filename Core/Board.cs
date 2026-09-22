namespace ChromaDrop.Core;

public sealed class Board
{
  public const int Width = 10;
  public const int Height = 18;
  private static readonly Cell[] Neighbors = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
  private readonly List<Piece> _pieces = [];
  public IReadOnlyList<Piece> Pieces => _pieces.AsReadOnly();

  public bool CanPlace(Piece piece)
  {
    var occupied = _pieces.SelectMany(block => block.Cells).ToHashSet();
    return piece.Cells.All(cell => cell.X >= 0 && cell.X < Width &&
      cell.Y >= 0 && cell.Y < Height && !occupied.Contains(cell));
  }

  public void Add(Piece piece)
  {
    if (_pieces.Any(block => block.Id == piece.Id) || !CanPlace(piece))
      throw new InvalidOperationException("The piece overlaps the board or has a duplicate identity.");
    _pieces.Add(piece);
  }

  public IReadOnlyList<Piece> FindMatches()
  {
    var occupied = new Dictionary<Cell, Piece>();
    foreach (var piece in _pieces)
      foreach (var cell in piece.Cells)
        occupied.Add(cell, piece);

    var visited = new HashSet<int>();
    var matches = new List<Piece>();
    foreach (var piece in _pieces)
    {
      if (!visited.Add(piece.Id)) continue;
      var group = new List<Piece> { piece };
      for (var i = 0; i < group.Count; i++)
      {
        foreach (var cell in group[i].Cells)
        {
          foreach (var neighbor in Neighbors)
          {
            if (occupied.TryGetValue(cell.Offset(neighbor.X, neighbor.Y), out var other) &&
              other.Color == piece.Color && visited.Add(other.Id))
              group.Add(other);
          }
        }
      }
      if (group.Count >= 3) matches.AddRange(group);
    }
    return matches;
  }

  public void Remove(IEnumerable<Piece> pieces)
  {
    var ids = pieces.Select(piece => piece.Id).ToHashSet();
    _pieces.RemoveAll(piece => ids.Contains(piece.Id));
  }

  public bool StepGravity()
  {
    var occupied = new Dictionary<Cell, int>();
    foreach (var piece in _pieces)
      foreach (var cell in piece.Cells)
        occupied.Add(cell, piece.Id);

    // Support propagates upward from the floor. Everything else falls together,
    // including interlocked shapes; list order cannot change the result.
    var movable = _pieces.Select(piece => piece.Id).ToHashSet();
    bool changed;
    do
    {
      changed = false;
      foreach (var piece in _pieces)
      {
        if (!movable.Contains(piece.Id)) continue;
        var supported = piece.Cells.Any(cell => cell.Y + 1 >= Height ||
          (occupied.TryGetValue(cell.Offset(0, 1), out var below) &&
            below != piece.Id && !movable.Contains(below)));
        if (supported) changed |= movable.Remove(piece.Id);
      }
    } while (changed);

    for (var i = 0; i < _pieces.Count; i++)
      if (movable.Contains(_pieces[i].Id)) _pieces[i] = _pieces[i].Offset(0, 1);
    return movable.Count > 0;
  }
}
