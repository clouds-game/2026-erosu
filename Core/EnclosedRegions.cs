namespace ChromaDrop.Core;

public static class EnclosedRegions
{
  public static int Fill(Board board, Random random, ColorProfile colors, ref int nextId)
  {
    var cells = board.FindEnclosedEmptyCells();
    foreach (var cell in cells)
    {
      var id = nextId;
      while (board.Pieces.Any(piece => piece.Id == id)) id--;
      board.Add(Piece.Create(id, Shape.Single, colors.SampleColor(random), cell.X, cell.Y)
        with { Anchored = true });
      nextId = id - 1;
    }
    return cells.Count;
  }
}
