namespace ChromaDrop.Core;

/// <summary>Seeded, non-overlapping single-cell obstacles in the lower six rows.</summary>
public static class AnchoredObstacles
{
  public const int Count = 3;

  public static void Populate(Board board, Random random, ColorProfile colors)
  {
    var candidates = new List<Piece>();
    for (var y = Board.Height - 6; y < Board.Height; y++)
      for (var x = 0; x < Board.Width; x++)
      {
        var piece = Piece.Create(0, Shape.Single, 0, x, y);
        if (board.CanPlace(piece)) candidates.Add(piece);
      }
    var shuffled = candidates.ToArray();
    random.Shuffle(shuffled);
    var palette = colors.Weights.SelectMany((weight, color) => Enumerable.Repeat(color, weight)).ToArray();
    var added = new List<Piece>();
    var id = -1;
    foreach (var candidate in shuffled)
    {
      if (!board.CanPlace(candidate)) continue;
      while (board.Pieces.Any(piece => piece.Id == id)) id--;
      var obstacle = candidate with { Id = id--, Color = palette[random.Next(palette.Length)], Anchored = true };
      board.Add(obstacle);
      if (board.FindMatches().Any(piece => piece.Id == obstacle.Id))
      {
        board.Remove([obstacle]);
        continue;
      }
      added.Add(obstacle);
      if (added.Count == Count) return;
    }
    board.Remove(added);
    throw new InvalidOperationException("Not enough room for anchored obstacles.");
  }
}
