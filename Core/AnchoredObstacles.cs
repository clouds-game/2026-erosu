namespace ChromaDrop.Core;

/// <summary>Seeded single-cell pressure between completed turns.</summary>
public static class AnchoredObstacles
{
  public const int InitialCount = 3;
  public const double SpawnProbability = 0.2;

  public static void Populate(Board board, Random random, ColorProfile colors, ref int nextId)
  {
    for (var i = 0; i < InitialCount; i++)
      if (!TrySpawn(board, random, colors, ref nextId))
        throw new InvalidOperationException("Not enough room for initial anchored obstacles.");
  }

  public static bool TrySpawn(Board board, Random random, ColorProfile colors, ref int nextId) =>
    TrySpawn(board, random, colors.SampleColor(random), ref nextId);

  public static bool TrySpawn(Board board, Random random, int color, ref int nextId)
  {
    var id = nextId;
    while (board.Pieces.Any(piece => piece.Id == id)) id--;
    var empty = new List<Cell>();
    for (var y = Board.Height - 6; y < Board.Height; y++)
      for (var x = 0; x < Board.Width; x++)
        if (board.CanPlace(Piece.Create(id, Shape.Single, 0, x, y))) empty.Add(new Cell(x, y));
    if (empty.Count == 0) return false;

    var candidates = new List<Piece>();
    foreach (var cell in empty)
    {
      var piece = Piece.Create(id, Shape.Single, color, cell.X, cell.Y) with { Anchored = true };
      board.Add(piece);
      var matches = board.FindMatches().Any(match => match.Id == id);
      board.Remove([piece]);
      if (!matches) candidates.Add(piece);
    }
    // Do not reroll the color or overwrite occupied cells when blocked.
    if (candidates.Count == 0) return false;
    board.Add(candidates[random.Next(candidates.Count)]);
    nextId = id - 1;
    return true;
  }
}
