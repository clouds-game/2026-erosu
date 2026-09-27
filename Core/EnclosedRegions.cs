namespace ChromaDrop.Core;

public static class EnclosedRegions
{
  private static readonly Cell[] Neighbors = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

  public static int Fill(Board board, Random random, ColorProfile colors, ref int nextId,
    IReadOnlyList<Cell>? cells = null)
  {
    cells ??= board.FindEnclosedEmptyCells();
    // Draw once per cell, then merge only this event's edge-connected colors.
    var remaining = cells.ToDictionary(cell => cell, _ => colors.SampleColor(random));
    foreach (var cell in cells)
    {
      if (!remaining.Remove(cell, out var color)) continue;
      var group = new List<Cell> { cell };
      for (var index = 0; index < group.Count; index++)
        foreach (var direction in Neighbors)
        {
          var neighbor = group[index].Offset(direction.X, direction.Y);
          if (remaining.TryGetValue(neighbor, out var other) && other == color)
          {
            remaining.Remove(neighbor);
            group.Add(neighbor);
          }
        }
      var id = nextId;
      while (board.Pieces.Any(piece => piece.Id == id)) id--;
      board.Add(new Piece(id, group.Count == 1 ? Shape.Single : Shape.Cluster, color, group.ToArray()));
      nextId = id - 1;
    }
    return cells.Count;
  }
}
