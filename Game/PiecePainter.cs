using ChromaDrop.Core;
using Godot;

namespace ChromaDrop.Game;

public static class PiecePainter
{
  public static readonly Color BoardColor = PixelUi.DeepBlue;
  private static readonly Rect2[] Marks =
  [
    new Rect2(72, 90, 16, 16),  // circle
    new Rect2(54, 126, 16, 16), // square
    new Rect2(90, 108, 16, 16), // pointed mark
    new Rect2(72, 126, 16, 16)  // arrow
  ];

  public static void Draw(CanvasItem target, Piece piece, Vector2 origin, float size,
    bool ghost = false, float flash = 0, bool polluted = false)
  {
    var tint = new Color(1, 1, 1, ghost ? 0.42f : 1);
    foreach (var cell in piece.Cells)
    {
      var position = origin + new Vector2(cell.X, cell.Y) * size;
      target.DrawTextureRectRegion(PixelUi.Sheet,
        new Rect2(position, Vector2.One * size), PixelUi.TileRegion(piece.Color), tint);
      if (flash > 0 && !ghost)
        target.DrawTextureRectRegion(PixelUi.Sheet,
          new Rect2(position, Vector2.One * size), PixelUi.WhiteTileRegion,
          new Color(1, 1, 1, flash * 0.7f));
    }
    if (ghost) return;
    if (polluted)
    {
      foreach (var cell in piece.Cells)
      {
        var position = origin + new Vector2(cell.X, cell.Y) * size + Vector2.One * 4;
        target.DrawRect(new Rect2(position, Vector2.One * (size - 8)), PixelUi.Cream, false, 2);
      }
    }
    // A single Kenney symbol identifies the whole shape as one game piece.
    var first = piece.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).First();
    var topLeft = origin + new Vector2(first.X, first.Y) * size;
    var markSize = size / 2;
    target.DrawTextureRectRegion(PixelUi.Sheet,
      new Rect2(topLeft + Vector2.One * (size - markSize) / 2, Vector2.One * markSize),
      Marks[piece.Color], PixelUi.Ink);
  }
}
