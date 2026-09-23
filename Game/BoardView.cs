using ChromaDrop.Core;
using Godot;

namespace ChromaDrop.Game;

public partial class BoardView : Control
{
  public GameSession? Session { get; set; }
  public const float CellSize = 32;

  public override void _Draw()
  {
    DrawRect(new Rect2(Vector2.Zero, Size), PiecePainter.BoardColor);
    var alternating = new Color("2e527a");
    for (var y = 0; y < Board.Height; y++)
      for (var x = 0; x < Board.Width; x++)
        if ((x + y) % 2 == 1)
          DrawRect(new Rect2(x * CellSize, y * CellSize, CellSize, CellSize), alternating);
    var grid = new Color("52749b", 0.45f);
    for (var x = 1; x < Board.Width; x++)
      DrawLine(new Vector2(x * CellSize, 0), new Vector2(x * CellSize, Size.Y), grid, 1);
    for (var y = 1; y < Board.Height; y++)
      DrawLine(new Vector2(0, y * CellSize), new Vector2(Size.X, y * CellSize), grid, 1);

    var game = Session;
    if (game is null) return;
    foreach (var piece in game.Board.Pieces) PiecePainter.Draw(this, piece, Vector2.Zero, CellSize);
    if (game.Phase == GamePhase.Falling)
    {
      var ghost = game.Ghost();
      if (ghost is not null) PiecePainter.Draw(this, ghost, Vector2.Zero, CellSize, ghost: true);
      if (game.Active is not null) PiecePainter.Draw(this, game.Active, Vector2.Zero, CellSize);
    }
    if (game.Wave is not null)
    {
      var flash = 0.15f + 0.75f * Mathf.Sin((float)game.ClearProgress * Mathf.Pi);
      foreach (var piece in game.Wave.Pieces)
        PiecePainter.Draw(this, piece, Vector2.Zero, CellSize, flash: flash);
    }
  }
}
