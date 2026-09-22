namespace ChromaDrop.Core;

public enum Shape { I, O, T, L, J, S, Z }

public readonly record struct Cell(int X, int Y)
{
  public Cell Offset(int x, int y) => new(X + x, Y + y);
}

public sealed record Piece(int Id, Shape Shape, int Color, IReadOnlyList<Cell> Cells)
{
  public Piece Offset(int x, int y) => this with
  {
    Cells = Cells.Select(cell => cell.Offset(x, y)).ToArray()
  };

  public static Piece Create(int id, Shape shape, int color, int x = 0, int y = 0)
  {
    Cell[] cells = shape switch
    {
      Shape.I => [new(0, 0), new(1, 0), new(2, 0), new(3, 0)],
      Shape.O => [new(0, 0), new(1, 0), new(0, 1), new(1, 1)],
      Shape.T => [new(1, 0), new(0, 1), new(1, 1), new(2, 1)],
      Shape.L => [new(2, 0), new(0, 1), new(1, 1), new(2, 1)],
      Shape.J => [new(0, 0), new(0, 1), new(1, 1), new(2, 1)],
      Shape.S => [new(1, 0), new(2, 0), new(0, 1), new(1, 1)],
      Shape.Z => [new(0, 0), new(1, 0), new(1, 1), new(2, 1)],
      _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };
    return new Piece(id, shape, color, cells).Offset(x, y);
  }
}
