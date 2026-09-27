using ChromaDrop.Core;
using Godot;

namespace ChromaDrop.Game;

public partial class NextView : Control
{
  public IReadOnlyList<Piece> Pieces { get; set; } = [];

  public override void _Draw()
  {
    var count = Pieces.Count;
    if (count == 0) return;
    var slotHeight = Size.Y / count;
    for (var i = 0; i < count; i++)
    {
      var piece = Pieces[i];
      var minX = piece.Cells.Min(cell => cell.X);
      var minY = piece.Cells.Min(cell => cell.Y);
      var width = piece.Cells.Max(cell => cell.X) - minX + 1;
      var height = piece.Cells.Max(cell => cell.Y) - minY + 1;
      var unit = Math.Min(16f, Math.Min((Size.X - 12) / width, (slotHeight - 12) / height));
      var origin = new Vector2((Size.X - width * unit) / 2 - minX * unit,
        i * slotHeight + (slotHeight - height * unit) / 2 - minY * unit);
      PiecePainter.Draw(this, piece, origin, unit);
    }
  }
}
