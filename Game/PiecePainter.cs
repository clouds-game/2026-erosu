using ChromaDrop.Core;
using Godot;

namespace ChromaDrop.Game;

public static class PiecePainter
{
  public static readonly Color Background = new("101319");
  public static readonly Color BoardColor = new("1b212c");
  public static readonly Color Text = new("e6edf3");
  public static readonly Color Muted = new("8391a5");
  public static readonly Color[] Colors = [new("ff8278"), new("8ee2c4"), new("f6ce71"), new("aaa2f7")];

  public static void Draw(CanvasItem target, Piece piece, Vector2 origin, float size,
    bool ghost = false, float flash = 0)
  {
    var cells = piece.Cells.ToHashSet();
    var color = Colors[piece.Color].Lerp(Godot.Colors.White, flash);
    var fill = ghost ? new Color(color, 0.07f) : color;
    var edge = ghost ? new Color(color, 0.6f) : Background;
    foreach (var cell in piece.Cells)
    {
      var position = origin + new Vector2(cell.X, cell.Y) * size;
      target.DrawRect(new Rect2(position, Vector2.One * size), fill);
    }
    foreach (var cell in piece.Cells)
    {
      var p = origin + new Vector2(cell.X, cell.Y) * size;
      var inset = ghost ? 1 : 1.5f;
      void Edge(int dx, int dy, Vector2 from, Vector2 to)
      {
        if (cells.Contains(cell.Offset(dx, dy))) return;
        if (ghost) target.DrawDashedLine(from, to, edge, 1, 4);
        else target.DrawLine(from, to, edge, 3, true);
      }
      Edge(0, -1, p + new Vector2(0, inset), p + new Vector2(size, inset));
      Edge(0, 1, p + new Vector2(0, size - inset), p + new Vector2(size, size - inset));
      Edge(-1, 0, p + new Vector2(inset, 0), p + new Vector2(inset, size));
      Edge(1, 0, p + new Vector2(size - inset, 0), p + new Vector2(size - inset, size));
      if (ghost) continue;
      var center = p + Vector2.One * size / 2;
      var mark = new Color(Background, 0.35f);
      switch (piece.Color)
      {
        case 0: target.DrawCircle(center, 2.2f, mark); break;
        case 1: target.DrawRect(new Rect2(center - Vector2.One * 2.5f, Vector2.One * 5), mark, false, 1.2f); break;
        case 2:
          target.DrawLine(center - Vector2.Right * 3, center + Vector2.Right * 3, mark, 1.2f);
          target.DrawLine(center - Vector2.Down * 3, center + Vector2.Down * 3, mark, 1.2f);
          break;
        case 3: target.DrawLine(center + new Vector2(-2, 3), center + new Vector2(2, -3), mark, 1.2f); break;
      }
    }
  }
}
