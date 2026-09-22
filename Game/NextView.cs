using ChromaDrop.Core;
using Godot;

namespace ChromaDrop.Game;

public partial class NextView : Control
{
  public bool Horizontal { get; set; }
  public IReadOnlyList<Piece> Pieces { get; set; } = [];

  public override void _Draw()
  {
    for (var i = 0; i < Pieces.Count; i++)
    {
      var piece = Pieces[i];
      var width = piece.Cells.Max(cell => cell.X) + 1;
      var height = piece.Cells.Max(cell => cell.Y) + 1;
      if (Horizontal)
      {
        var columnWidth = Size.X / Math.Max(1, Pieces.Count);
        var unit = Math.Min(24, columnWidth / 4);
        PiecePainter.Draw(this, piece, new Vector2(i * columnWidth + (columnWidth - width * unit) / 2, (Size.Y - height * unit) / 2), unit);
        continue;
      }
      var rowHeight = Size.Y / 3;
      var cellSize = Math.Min(28, (rowHeight - 6) / 2);
      PiecePainter.Draw(this, piece, new Vector2((Size.X - width * cellSize) / 2, i * rowHeight + (2 - height) * cellSize / 2), cellSize);
    }
  }
}
