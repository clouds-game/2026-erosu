namespace ChromaDrop.Core;

public sealed class PieceBag(Random random, ColorProfile? profile = null)
{
  public const int ColorCount = ColorRules.Count;
  private readonly ColorProfile _profile = profile ?? ColorProfile.Default;
  private readonly Stack<Shape> _shapes = new();
  private readonly Stack<int> _colors = new();
  private int _nextId = 1;

  public Piece Take()
  {
    if (_shapes.Count == 0)
    {
      var shapes = Enum.GetValues<Shape>();
      random.Shuffle(shapes);
      foreach (var shape in shapes) _shapes.Push(shape);
    }
    if (_colors.Count == 0)
    {
      var colors = _profile.Weights
        .SelectMany((count, color) => Enumerable.Repeat(color, count))
        .ToArray();
      random.Shuffle(colors);
      foreach (var color in colors) _colors.Push(color);
    }
    return Piece.Create(_nextId++, _shapes.Pop(), _colors.Pop());
  }
}
