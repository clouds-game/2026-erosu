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
    new Rect2(72, 126, 16, 16), // arrow
    new Rect2(54, 90, 16, 16)   // cross
  ];
  private static readonly Color FifthColor = new("e58bd3");

  public static void Draw(CanvasItem target, Piece piece, Vector2 origin, float size,
    bool ghost = false, float flash = 0)
  {
    var baseTint = piece.Color == 4 ? FifthColor : Colors.White;
    var tint = new Color(baseTint.R, baseTint.G, baseTint.B, ghost ? 0.42f : 1);
    var tileRegion = piece.Color == 4 ? PixelUi.WhiteTileRegion : PixelUi.TileRegion(piece.Color);
    foreach (var cell in piece.Cells)
    {
      var position = origin + new Vector2(cell.X, cell.Y) * size;
      target.DrawTextureRectRegion(PixelUi.Sheet,
        new Rect2(position, Vector2.One * size), tileRegion, tint);
      if (flash > 0 && !ghost)
        target.DrawTextureRectRegion(PixelUi.Sheet,
          new Rect2(position, Vector2.One * size), PixelUi.WhiteTileRegion,
          new Color(1, 1, 1, flash * 0.7f));
      if (piece.IsPollution && !ghost)
      {
        var ink = new Color(PixelUi.Ink, 0.38f);
        DrawSlash(target, position, size, ink, false);
        DrawSlash(target, position, size, ink, true);
      }
    }
    if (ghost) return;
    // A single Kenney symbol identifies the whole shape as one game piece.
    var first = piece.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).First();
    var topLeft = origin + new Vector2(first.X, first.Y) * size;
    var markSize = size / 2;
    target.DrawTextureRectRegion(PixelUi.Sheet,
      new Rect2(topLeft + Vector2.One * (size - markSize) / 2, Vector2.One * markSize),
      Marks[piece.Color], PixelUi.Ink);
  }

  private static void DrawSlash(CanvasItem target, Vector2 position, float size, Color color, bool lower)
  {
    var offset = lower ? size * 0.56f : size * 0.18f;
    target.DrawLine(position + new Vector2(size * 0.08f, offset + size * 0.24f),
      position + new Vector2(size * 0.32f, offset), color, Math.Max(1, size / 18));
    target.DrawLine(position + new Vector2(size * 0.68f, offset + size * 0.24f),
      position + new Vector2(size * 0.92f, offset), color, Math.Max(1, size / 18));
  }
}
