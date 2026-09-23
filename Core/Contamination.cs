namespace ChromaDrop.Core;

public static class ContaminationRules
{
  public const int BandHeight = 3;
  public static int RiseInterval(int wave) => Math.Max(6, 10 - Math.Max(0, wave - 1) / 3);
}

public sealed class ContaminationGenerator
{
  private sealed record Slot(Shape Shape, int X, int Y);
  private static readonly Slot[][] SmallTemplates =
  [
    [new(Shape.O, 0, 1), new(Shape.O, 4, 1), new(Shape.O, 8, 1)],
    [new(Shape.I, 0, 2), new(Shape.O, 4, 1), new(Shape.T, 7, 1)],
    [new(Shape.L, 0, 1), new(Shape.S, 3, 1), new(Shape.J, 6, 1)],
    [new(Shape.T, 0, 1), new(Shape.Z, 3, 1), new(Shape.L, 6, 1)]
  ];
  private static readonly Slot[][] LargeTemplates =
  [
    [new(Shape.I, 0, 2), new(Shape.O, 4, 1), new(Shape.O, 6, 1), new(Shape.O, 8, 1)],
    [new(Shape.O, 0, 1), new(Shape.T, 2, 1), new(Shape.S, 5, 1), new(Shape.L, 7, 1)],
    [new(Shape.O, 0, 1), new(Shape.O, 2, 1), new(Shape.I, 4, 2), new(Shape.O, 8, 1)],
    [new(Shape.T, 0, 1), new(Shape.O, 3, 1), new(Shape.Z, 5, 1), new(Shape.O, 8, 1)]
  ];

  private readonly Random _random;
  private readonly Queue<int> _small = new();
  private readonly Queue<int> _large = new();
  private int _lastSmall = -1;
  private int _lastLarge = -1;
  private int _nextId = -1;

  public ContaminationGenerator(Random random) => _random = random;

  public IReadOnlyList<Piece> CreateBand(int wave)
  {
    var useLarge = wave >= 10 || (wave >= 4 && wave % 2 == 0);
    var templates = useLarge ? LargeTemplates : SmallTemplates;
    var order = useLarge ? _large : _small;
    if (order.Count == 0)
    {
      var indices = Enumerable.Range(0, templates.Length).ToArray();
      _random.Shuffle(indices);
      var last = useLarge ? _lastLarge : _lastSmall;
      if (indices.Length > 1 && indices[0] == last)
        (indices[0], indices[1]) = (indices[1], indices[0]);
      foreach (var index in indices) order.Enqueue(index);
    }

    var selected = order.Dequeue();
    if (useLarge) _lastLarge = selected;
    else _lastSmall = selected;
    var slots = templates[selected];
    var colors = Enumerable.Range(0, 4).ToArray();
    _random.Shuffle(colors);
    var top = Board.Height - ContaminationRules.BandHeight;
    return slots.Select((slot, index) =>
      Piece.Create(_nextId--, slot.Shape, colors[index], slot.X, top + slot.Y)).ToArray();
  }

  internal static IReadOnlyList<IReadOnlyList<Piece>> AllTemplates()
  {
    return SmallTemplates.Concat(LargeTemplates).Select((slots, template) =>
      (IReadOnlyList<Piece>)slots.Select((slot, index) =>
        Piece.Create(-(template * 10 + index + 1), slot.Shape, index, slot.X,
          Board.Height - ContaminationRules.BandHeight + slot.Y)).ToArray()).ToArray();
  }
}
