using ChromaDrop.Core;
using Godot;

namespace ChromaDrop.Game;

public partial class NextView : Control
{
  public IReadOnlyList<Piece> Pieces { get; set; } = [];

  public override void _Draw()
  {
    for (var i = 0; i < Pieces.Count; i++)
    {
      var piece = Pieces[i];
      var width = piece.Cells.Max(cell => cell.X) + 1;
      var height = piece.Cells.Max(cell => cell.Y) + 1;
      PiecePainter.Draw(this, piece, new Vector2((Size.X - width * 28) / 2, i * 64 + (2 - height) * 14), 28);
    }
  }
}
