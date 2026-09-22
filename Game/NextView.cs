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
      var rowHeight = Size.Y / 3;
      var cellSize = Math.Min(28, (rowHeight - 6) / 2);
      PiecePainter.Draw(this, piece, new Vector2((Size.X - width * cellSize) / 2, i * rowHeight + (2 - height) * cellSize / 2), cellSize);
    }
  }
}
