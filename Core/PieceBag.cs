namespace ChromaDrop.Core;

internal interface IPieceSource
{
  Piece Take();
}

public sealed class PieceBag(Random random, int firstId = 1, bool pollution = false) : IPieceSource
{
  public const int ColorCount = 5;
  private static readonly int[] ColorWeights = [10, 9, 8, 7, 6];
  private readonly Stack<Shape> _shapes = new();
  private readonly Stack<int> _colors = new();
  private int _nextId = firstId;

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
      var colors = ColorWeights
        .SelectMany((count, color) => Enumerable.Repeat(color, count))
        .ToArray();
      random.Shuffle(colors);
      foreach (var color in colors) _colors.Push(color);
    }
    var piece = Piece.Create(_nextId, _shapes.Pop(), _colors.Pop()) with { IsPollution = pollution };
    _nextId += pollution ? -1 : 1;
    return piece;
  }
}

internal sealed class BlackWhitePieceSource(IPieceSource pieces, Random candidateRandom) : IPieceSource
{
  public const int BagSize = 5;
  private readonly Stack<bool> _candidates = new();

  public Piece Take()
  {
    if (_candidates.Count == 0)
    {
      var candidates = new[] { true, false, false, false, false };
      candidateRandom.Shuffle(candidates);
      foreach (var candidate in candidates) _candidates.Push(candidate);
    }
    var piece = pieces.Take();
    return !_candidates.Pop() ? piece : piece with { Color = -1, Kind = PieceKind.SpecialUnknown };
  }
}
