using ChromaDrop.Core;

internal static class ModeArchitectureTests
{
  public static IEnumerable<(string Name, Action Run)> Cases => new (string, Action)[]
  {
    ("Mode catalog exposes stable unique selections and metrics", CatalogContract),
    ("Session factory applies each mode contract", FactoryContract),
    ("Named random streams keep player pieces independent from mode internals", RandomStreamContract),
    ("Legacy session constructors remain compatible", LegacyConstructorContract)
  };

  private static void CatalogContract()
  {
    var tokens = ModeCatalog.Standalone.Select(mode => mode.Token).ToArray();
    Assert(tokens.Distinct().Count() == tokens.Length);
    foreach (var mode in ModeCatalog.Standalone)
    {
      Assert(GameSelection.TryParse(mode.Token, out var selection));
      Assert(selection.Mode == mode.Id && selection.Token == mode.Token);
      Assert(mode.Metrics.Select(metric => metric.Key).Distinct().Count() == mode.Metrics.Count);
    }
    foreach (var level in PuzzleLevels.All)
    {
      var expected = GameSelection.Puzzle(level.Number);
      Assert(GameSelection.TryParse(expected.Token, out var parsed) && parsed == expected);
    }
    Assert(!GameSelection.TryParse("puzzle:999", out _));
  }

  private static void FactoryContract()
  {
    var free = GameSessionFactory.Create(GameSelection.Free, 10);
    var pollution = GameSessionFactory.Create(GameSelection.Pollution, 10);
    var blackWhite = GameSessionFactory.Create(GameSelection.BlackWhite, 10);
    var puzzle = GameSessionFactory.Create(GameSelection.Puzzle(PuzzleLevels.All[0].Number), 10);
    Assert(free.Mode == ModeId.Free && free.Puzzle is null && free.Metrics.Any(metric => metric.Key == "score"));
    Assert(pollution.Mode == ModeId.Pollution && pollution.Metric("next_rise")?.Value == 6);
    Assert(blackWhite.Mode == ModeId.BlackWhite && blackWhite.Presentation.Help.Action == HelpAction.Rules);
    Assert(puzzle.Mode == ModeId.Puzzle && puzzle.Puzzle == PuzzleLevels.All[0] && puzzle.Next.Count == puzzle.Remaining - 1);
    Assert(free.Presentation.Help.Action == HelpAction.Demo);
    Assert(pollution.Presentation.Help.Action == HelpAction.Rules);
    Assert(puzzle.Presentation.Help.Action == HelpAction.PuzzleHint);
    Assert(free.ContinueAction == ContinueAction.Restart);
  }

  private static void RandomStreamContract()
  {
    var free = GameSessionFactory.Create(GameSelection.Free, 12345);
    var pollution = GameSessionFactory.Create(GameSelection.Pollution, 12345);
    Assert(Signature(free.Active!) == Signature(pollution.Active!));
    Assert(free.Next.Select(Signature).SequenceEqual(pollution.Next.Select(Signature)));

    var first = SeedStreams.Create(99, "player.pieces").Next();
    _ = SeedStreams.Create(99, "pollution.pieces").Next();
    var repeated = SeedStreams.Create(99, "player.pieces").Next();
    Assert(first == repeated);
  }

  private static void LegacyConstructorContract()
  {
#pragma warning disable CS0618
    var free = new GameSession(new Random(5));
    var pollution = new GameSession(SessionMode.Pollution, new Random(5));
    var puzzle = new GameSession(PuzzleLevels.All[0]);
#pragma warning restore CS0618
    Assert(free.Mode == ModeId.Free);
    Assert(pollution.Mode == ModeId.Pollution);
    Assert(puzzle.Mode == ModeId.Puzzle);
  }

  private static string Signature(Piece piece) => $"{piece.Shape}:{piece.Color}:" +
    string.Join(',', piece.Cells.Select(cell => $"{cell.X}:{cell.Y}"));

  private static void Assert(bool condition)
  {
    if (!condition) throw new InvalidOperationException("Mode architecture contract failed.");
  }
}
