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
  private static readonly Color BlackColor = new("171c27");

  public static void Draw(CanvasItem target, Piece piece, Vector2 origin, float size,
    bool ghost = false, float flash = 0, int? turnsRemaining = null, float opacity = 1)
  {
    var baseTint = piece.Kind switch
    {
      PieceKind.Black => BlackColor,
      PieceKind.White or PieceKind.SpecialUnknown => Colors.White,
      _ => piece.Color == 4 ? FifthColor : Colors.White
    };
    var alpha = ghost ? 0.42f : opacity;
    var tint = new Color(baseTint.R, baseTint.G, baseTint.B, alpha);
    var tileRegion = piece.Kind != PieceKind.Normal || piece.Color == 4
      ? PixelUi.WhiteTileRegion
      : PixelUi.TileRegion(piece.Color);
    foreach (var cell in piece.Cells)
    {
      var position = origin + new Vector2(cell.X, cell.Y) * size;
      target.DrawTextureRectRegion(PixelUi.Sheet,
        new Rect2(position, Vector2.One * size), tileRegion, tint);
      if (piece.Kind == PieceKind.SpecialUnknown)
        target.DrawRect(new Rect2(position, new Vector2(size / 2, size)), new Color(BlackColor, alpha));
      if (piece.Kind == PieceKind.Black)
        target.DrawRect(new Rect2(position + Vector2.One, Vector2.One * (size - 2)), new Color(PixelUi.Cream, alpha), false,
          Math.Max(1, size / 16));
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
    var markCenter = topLeft + Vector2.One * size / 2;
    if (piece.Kind == PieceKind.Normal)
      target.DrawTextureRectRegion(PixelUi.Sheet,
        new Rect2(topLeft + Vector2.One * (size - markSize) / 2, Vector2.One * markSize),
        Marks[piece.Color], new Color(PixelUi.Ink, opacity));
    else if (piece.Kind == PieceKind.SpecialUnknown)
    {
      target.DrawCircle(markCenter + Vector2.Left * size * 0.1f, size * 0.07f, new Color(PixelUi.Cream, opacity));
      target.DrawCircle(markCenter + Vector2.Right * size * 0.1f, size * 0.07f, new Color(PixelUi.Ink, opacity));
    }
    else if (piece.Kind == PieceKind.White)
      target.DrawCircle(markCenter, size * 0.11f, new Color(PixelUi.Ink, opacity), false, Math.Max(1, size / 16));
    else if (piece.Kind == PieceKind.Black && turnsRemaining is > 0 and <= 3)
    {
      var count = turnsRemaining.Value;
      for (var i = 0; i < count; i++)
      {
        var x = (i - (count - 1) / 2f) * size * 0.18f;
        target.DrawCircle(markCenter + Vector2.Right * x, size * 0.055f, new Color(PixelUi.Cream, opacity));
      }
    }
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
