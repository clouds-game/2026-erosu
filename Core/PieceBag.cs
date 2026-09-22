namespace ChromaDrop.Core;

public sealed class PieceBag(Random random)
{
  private readonly Stack<Shape> _shapes = new();
  private int _nextId = 1;

  public Piece Take()
  {
    if (_shapes.Count == 0)
    {
      var shapes = Enum.GetValues<Shape>();
      random.Shuffle(shapes);
      foreach (var shape in shapes) _shapes.Push(shape);
    }
    return Piece.Create(_nextId++, _shapes.Pop(), random.Next(4));
  }
}
