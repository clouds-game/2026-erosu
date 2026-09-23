using ChromaDrop.Core;
using Godot;

namespace ChromaDrop.Game;

public static class PiecePainter
{
  public static readonly Color Background = new("101b16");
  public static readonly Color BoardColor = new("17241d");
  public static readonly Color Text = PixelUi.Cream;
  public static readonly Color Muted = PixelUi.Muted;
  public static readonly Color[] Colors = [new("eb756b"), new("88d98d"), new("ffd15c"), new("a79bdc")];
  private static readonly Rect2[] Marks =
  [
    new Rect2(72, 90, 16, 16),  // circle
    new Rect2(54, 126, 16, 16), // square
    new Rect2(90, 108, 16, 16), // pointed mark
    new Rect2(72, 126, 16, 16)  // arrow
  ];

  public static void Draw(CanvasItem target, Piece piece, Vector2 origin, float size,
    bool ghost = false, float flash = 0)
  {
    var tint = Colors[piece.Color].Lerp(Godot.Colors.White, flash);
    if (ghost) tint.A = 0.38f;
    foreach (var cell in piece.Cells)
    {
      var position = origin + new Vector2(cell.X, cell.Y) * size;
      target.DrawTextureRectRegion(PixelUi.Sheet,
        new Rect2(position, Vector2.One * size), PixelUi.TileRegion, tint);
    }
    if (ghost) return;
    // A single Kenney symbol identifies the whole shape as one game piece.
    var first = piece.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).First();
    var topLeft = origin + new Vector2(first.X, first.Y) * size;
    var markSize = size / 2;
    target.DrawTextureRectRegion(PixelUi.Sheet,
      new Rect2(topLeft + Vector2.One * (size - markSize) / 2, Vector2.One * markSize),
      Marks[piece.Color], new Color("342522"));
  }
}
