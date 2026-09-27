using ChromaDrop.Core;
using ChromaDrop.Core.Game0;

internal static class Game0Tests
{
  public static void RunAll()
  {
    BasicDropsAndRotation();
    DropTrajectoryStopsAtStructure();
    HandLockAndCats();
    RingAwards();
    CatLastChance();
  }

  private static void BasicDropsAndRotation()
  {
    var game = new Game0Session(7);
    Require(game.Hand.Where(shape => shape.HasValue).Select(shape => shape!.Value).Order()
      .SequenceEqual(Enum.GetValues<Shape>().Order()), "A hand must contain all seven shapes once.");
    var i = IndexOf(game, Shape.I);
    Require(game.Preview(PieceSource.Hand, i, EntrySide.Top, 0, 1) is null,
      "A block that misses the center must leave the board.");
    Require(!game.Place(PieceSource.Hand, i, EntrySide.Top, 0, 1).Accepted && game.Hand[i] == Shape.I,
      "A failed drop must return to its slot.");
    var drop = game.Preview(PieceSource.Hand, i, EntrySide.Top, 6, 1);
    Require(drop is not null && drop.Cells.SequenceEqual(new[]
      { new Cell(6, 2), new Cell(6, 3), new Cell(6, 4), new Cell(6, 5) }),
      "The I should stop immediately before the core.");
    Require(game.Place(PieceSource.Hand, i, EntrySide.Top, 6, 1).Accepted &&
      game.Tiles.Count == 4 && game.UntilRotation == 3, "A valid drop should consume the slot and rotation count.");
    for (var turn = 0; turn < 3; turn++)
    {
      var move = Enumerable.Range(0, Game0Session.HandSize)
        .SelectMany(index => Enumerable.Range(0, 4).SelectMany(rotation =>
          Enumerable.Range(0, Game0Session.Size).Select(lane => (index, rotation, lane))))
        .FirstOrDefault(candidate => game.Preview(PieceSource.Hand, candidate.index,
          EntrySide.Top, candidate.lane, candidate.rotation) is not null);
      Require(game.Place(PieceSource.Hand, move.index, EntrySide.Top, move.lane, move.rotation).Accepted,
        "The next placement should be legal.");
    }
    Require(game.UntilRotation == Game0Session.RotationPeriod && !game.ClockwiseNext,
      "The fourth successful placement should rotate the board and announce the opposite direction.");
  }

  private static void HandLockAndCats()
  {
    var cats = new Shape?[] { Shape.O };
    var game = new Game0Session(3, initialCats: cats);
    foreach (var side in Enum.GetValues<EntrySide>())
    {
      var lane = side is EntrySide.Top or EntrySide.Bottom ? 5 : 5;
      Require(game.Preview(PieceSource.Cat, 0, side, lane, 0) is not null,
        $"A cat should be able to enter from {side}.");
    }
    game.Advance(Game0Session.HandSeconds);
    Require(game.Locked && game.HandTimeLeft == 0 && game.CanSelect(PieceSource.Hand, 0),
      "Timeout should lock the first remaining hand slot.");
    Require(!game.CanSelect(PieceSource.Hand, 1) && game.CanSelect(PieceSource.Cat, 0),
      "Lock should preserve cat access while restricting hand order.");
  }

  private static void DropTrajectoryStopsAtStructure()
  {
    var roof = Enumerable.Range(0, Game0Session.Center).ToDictionary(
      y => new Cell(Game0Session.Center, y), _ => new PlacedCell(Shape.I, false));
    var blocked = new Game0Session(12, roof);
    var i = IndexOf(blocked, Shape.I);
    var hit = blocked.Trace(PieceSource.Hand, i, EntrySide.Top, Game0Session.Center, 1);
    Require(hit is { HitStructure: true, Landing: null } && hit.EndOrigin.Y == -4,
      "A blocked top entry must stop at the roof, not animate through it.");
    var empty = blocked.Trace(PieceSource.Hand, i, EntrySide.Top, 0, 1);
    Require(empty is { HitStructure: false, Landing: null } && empty.EndOrigin.Y == Game0Session.Size,
      "Only an empty path may animate through the far edge and return to Hand.");

    var shelf = new Game0Session(13,
      new Dictionary<Cell, PlacedCell> { [new Cell(0, 3)] = new(Shape.O, false) });
    var o = IndexOf(shelf, Shape.O);
    var landing = shelf.Trace(PieceSource.Hand, o, EntrySide.Top, 0, 0);
    Require(landing is { HitStructure: true, Landing: not null } && landing.EndOrigin.Y == 1,
      "The visible fall must stop immediately before the first occupied cell.");
  }

  private static void RingAwards()
  {
    var oneRing = RingScenario(1);
    var i = IndexOf(oneRing, Shape.I);
    Require(oneRing.Preview(PieceSource.Hand, i, EntrySide.Top, 6, 1)?.Rings.SequenceEqual([1]) == true,
      "The preview should identify a directly completed ring.");
    var single = oneRing.Place(PieceSource.Hand, i, EntrySide.Top, 6, 1);
    Require(single.Accepted && single.Rings == 1 && single.Points == 100 && !single.CatEarned,
      "One ring must award 100 and no cat.");

    var twoRings = RingScenario(2);
    i = IndexOf(twoRings, Shape.I);
    Require(twoRings.Preview(PieceSource.Hand, i, EntrySide.Top, 6, 1)?.Rings.SequenceEqual([1, 2]) == true,
      "The preview should identify two simultaneous rings.");
    var doubleClear = twoRings.Place(PieceSource.Hand, i, EntrySide.Top, 6, 1);
    Require(doubleClear.Accepted && doubleClear.Rings == 2 && doubleClear.Points == 300 &&
      doubleClear.CatEarned && twoRings.Cats.Count(cat => cat.HasValue) == 1,
      "Two rings must award 300 and exactly one cat.");
  }

  private static Game0Session RingScenario(int ringCount)
  {
    var occupied = new Dictionary<Cell, PlacedCell>();
    for (var radius = 1; radius <= ringCount; radius++)
      for (var y = 6 - radius; y <= 6 + radius; y++)
        for (var x = 6 - radius; x <= 6 + radius; x++)
          if (Math.Max(Math.Abs(x - 6), Math.Abs(y - 6)) == radius && !(x == 6 && y == 6 - radius))
            occupied.Add(new Cell(x, y), new PlacedCell(Shape.O, false));
    return new Game0Session(11, occupied);
  }

  private static void CatLastChance()
  {
    var roof = Enumerable.Range(0, Game0Session.Size).ToDictionary(
      x => new Cell(x, 0), _ => new PlacedCell(Shape.O, false));
    var game = new Game0Session(1, roof, new Shape?[] { Shape.O });
    game.Advance(Game0Session.HandSeconds);
    Require(game.Locked && !game.IsOver && game.Cats[0] == Shape.O,
      "A playable cat must keep a blocked locked hand alive.");
    Require(game.Place(PieceSource.Cat, 0, EntrySide.Bottom, 5, 0).Accepted,
      "The cat should be able to enter from the open bottom edge.");
    Require(game.IsOver, "After the last cat is spent, a still-blocked hand should end the run.");
  }

  private static int IndexOf(Game0Session game, Shape shape)
  {
    for (var i = 0; i < game.Hand.Count; i++) if (game.Hand[i] == shape) return i;
    throw new InvalidOperationException($"Missing {shape} in hand.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition) throw new Exception(message);
  }
}
