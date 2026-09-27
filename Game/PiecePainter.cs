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
    if (piece.Kind == PieceKind.Black && turnsRemaining is > 0 and <= BlackWhiteModeRules.BlackLifetime)
    {
      var badgeCell = BadgeCell(piece);
      DrawCountdown(target, origin + new Vector2(badgeCell.X, badgeCell.Y) * size,
        size, turnsRemaining.Value, opacity);
    }
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
  }

  private static void DrawCountdown(CanvasItem target, Vector2 position, float size, int digit, float opacity)
  {
    target.DrawRect(new Rect2(position + new Vector2(size * 0.225f, size * 0.175f),
      new Vector2(size * 0.55f, size * 0.65f)), new Color(PixelUi.Cream, opacity));

    const int top = 1, upperRight = 2, lowerRight = 4, bottom = 8;
    const int lowerLeft = 16, upperLeft = 32, middle = 64;
    var mask = digit switch
    {
      1 => upperRight | lowerRight,
      2 => top | upperRight | middle | lowerLeft | bottom,
      3 => top | upperRight | middle | lowerRight | bottom,
      4 => upperLeft | middle | upperRight | lowerRight,
      _ => 0
    };
    var left = position.X + size * 0.35f;
    var right = position.X + size * 0.65f;
    var upper = position.Y + size * 0.29f;
    var center = position.Y + size * 0.5f;
    var lower = position.Y + size * 0.71f;
    var ink = new Color(BlackColor, opacity);
    var width = Math.Max(2, size * 0.07f);
    if ((mask & top) != 0) target.DrawLine(new(left, upper), new(right, upper), ink, width);
    if ((mask & upperRight) != 0) target.DrawLine(new(right, upper), new(right, center), ink, width);
    if ((mask & lowerRight) != 0) target.DrawLine(new(right, center), new(right, lower), ink, width);
    if ((mask & bottom) != 0) target.DrawLine(new(left, lower), new(right, lower), ink, width);
    if ((mask & lowerLeft) != 0) target.DrawLine(new(left, center), new(left, lower), ink, width);
    if ((mask & upperLeft) != 0) target.DrawLine(new(left, upper), new(left, center), ink, width);
    if ((mask & middle) != 0) target.DrawLine(new(left, center), new(right, center), ink, width);
  }

  private static Cell BadgeCell(Piece piece)
  {
    var centerX = piece.Cells.Average(cell => cell.X);
    var centerY = piece.Cells.Average(cell => cell.Y);
    return piece.Cells
      .OrderBy(cell => Math.Pow(cell.X - centerX, 2) + Math.Pow(cell.Y - centerY, 2))
      .ThenBy(cell => cell.Y)
      .ThenBy(cell => cell.X)
      .First();
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
