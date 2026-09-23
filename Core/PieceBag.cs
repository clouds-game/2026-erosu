namespace ChromaDrop.Core;

public sealed class PieceBag
{
  private readonly Random _random;
  private readonly bool _balancedColors;
  private readonly Stack<Shape> _shapes = new();
  private readonly Stack<int> _colors = new();
  private int _nextId = 1;

  public PieceBag(Random random, bool balancedColors = false)
  {
    _random = random;
    _balancedColors = balancedColors;
  }

  public Piece Take()
  {
    if (_shapes.Count == 0)
    {
      var shapes = Enum.GetValues<Shape>();
      _random.Shuffle(shapes);
      foreach (var shape in shapes) _shapes.Push(shape);
    }
    return Piece.Create(_nextId++, _shapes.Pop(), TakeColor());
  }

  private int TakeColor()
  {
    if (!_balancedColors) return _random.Next(4);
    if (_colors.Count == 0)
    {
      int[] colors = [0, 0, 1, 1, 2, 2, 3, 3];
      _random.Shuffle(colors);
      foreach (var color in colors) _colors.Push(color);
    }
    return _colors.Pop();
  }
}
