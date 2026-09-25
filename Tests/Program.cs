using ChromaDrop.Core;
using ChromaDrop.Localization;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Contains("--analyze-challenge"))
{
  var argument = Array.IndexOf(args, "--analyze-challenge");
  return ChallengeAnalysis.Run(argument + 1 < args.Length && int.TryParse(args[argument + 1], out var number) ? number : null);
}

var tests = new (string Name, Action Run)[]
{
  ("Locale aliases and unsupported languages resolve consistently", () =>
  {
    foreach (var (input, expected) in new[] { ("en-US", "en"), ("zh_TW", "zh-CN"), ("cn", "zh-CN"), ("ja-JP", "ja"), ("fr", "en"), ("", "en") })
      Check(UiText.Normalize(input) == expected);
    Check(UiText.Normalize(null) == "en");
  }),
  ("All UI translations have complete keys and matching placeholders", () =>
  {
    using var stream = typeof(UiText).Assembly.GetManifestResourceStream("ChromaDrop.Localization.strings.json")!;
    var catalog = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
    var english = catalog["en"];
    foreach (var language in UiText.Languages)
    {
      Check(catalog[language].Keys.Order().SequenceEqual(english.Keys.Order()));
      var text = new UiText();
      text.SetLanguage(language);
      foreach (var (key, value) in catalog[language])
      {
        Check(!string.IsNullOrWhiteSpace(value));
        static string Slots(string template) => string.Join(",", Regex.Matches(template, @"\{\d+\}").Select(match => match.Value).Order());
        Check(Slots(value) == Slots(english[key]));
        Check(!text.Get(key, 3, 600, 2).Contains('{'));
      }
      Check(text.Get("match", 3, 600, 2).Contains("600"));
      Check(text.Get("match", 3, UiText.Number(2100), 2).Contains("2,100"));
    }
  }),
  ("Every challenge has a verified solution with its intended clear rhythm", () =>
  {
    var solutions = new[]
    {
      new ChallengeSolution(4, new[] { (0, 0), (1, 0), (5, 0), (2, 0), (0, 1) }, 20900, 3, new[] { "1", "0", "2" }),
      new ChallengeSolution(5, new[] { (8, 1), (4, 3), (2, 0), (7, 0), (8, 1) }, 10700, 2, new[] { "0", "1", "2" }),
      new ChallengeSolution(6, new[] { (5, 0), (2, 3), (4, 0), (6, 1), (1, 0), (8, 3) }, 16200, 2, new[] { "2", "01" })
    };
    foreach (var solution in solutions)
    {
      var level = PuzzleLevels.All.Single(puzzle => puzzle.Number == solution.Level);
      var game = Play(level, solution.Moves, out var waves);
      Check(game.Phase == GamePhase.Won && game.Locked == solution.Moves.Length && game.Board.Pieces.Count == 0);
      Check(game.Cleared == 9 && game.BestChain == solution.Chain && game.Score == solution.Score,
        $"Challenge {solution.Level - 3} scored {game.Score}, expected {solution.Score}.");
      Check(waves.SequenceEqual(solution.Waves));
    }
  }),
  ("Challenge color budgets require setup and every supplied piece", () =>
  {
    var challenges = PuzzleLevels.All.Where(puzzle => !puzzle.IsTutorial).ToArray();
    foreach (var level in challenges)
    {
      var colors = level.InitialPieces.Concat(level.Sequence).Select(piece => piece.Color).Distinct();
      foreach (var color in colors)
      {
        Check(level.InitialPieces.Concat(level.Sequence).Count(piece => piece.Color == color) == 3);
        Check(level.InitialPieces.Concat(level.Sequence.Take(level.Number == 6 ? 3 : 2)).Count(piece => piece.Color == color) < 3);
      }
      var finalColor = level.Sequence.Last().Color;
      Check(level.InitialPieces.Concat(level.Sequence.SkipLast(1)).Count(piece => piece.Color == finalColor) == 2);
    }
    Check(PuzzleLevels.DefaultLevel == 4 && PuzzleLevels.NextLevel(3) == 4);
    Check(PuzzleLevels.NextLevel(4) == 5 && PuzzleLevels.NextLevel(5) == 6 && PuzzleLevels.NextLevel(6) == 4);
    Check(PuzzleLevels.ContinueKey(challenges[0]) == "next_challenge");
    Check(PuzzleLevels.CompletionKey(challenges[^1]) == "challenges_done");
    Check(PuzzleLevels.ContinueKey(challenges[^1]) == "replay_challenges" && PuzzleLevels.ProgressMask == 63);
  }),
  ("Authored puzzles are stable, solvable through input, and replayable", () =>
  {
    var targets = new[] { 4, 0, 2 };
    for (var index = 0; index < 3; index++)
    {
      var level = PuzzleLevels.All[index];
      var game = new GameSession(level);
      Check(!game.Board.StepGravity() && game.Board.FindMatches().Count == 0);
      Check(game.Remaining == 1 && game.Next.Count == 0);
      var target = targets[index];
      while (game.Active!.Cells.Min(cell => cell.X) != target)
        Check(game.Move(Math.Sign(target - game.Active.Cells.Min(cell => cell.X)), 0));
      game.HardDrop();
      Check(game.Phase == GamePhase.Clearing && !game.IsFinished && game.Remaining == 0);
      for (var step = 0; step < 500 && !game.IsFinished; step++) game.Advance(0.05);
      Check(game.Phase == GamePhase.Won && game.Cleared == 3 && game.Active is null);
      Check(game.Board.Pieces.Count == (index == 1 ? 1 : 0));
      Check(!game.Move(1, 0) && !game.Rotate());
      game.HardDrop();
      game.SetPaused(true);
      Check(game.Locked == 1 && !game.Paused);
      var retry = new GameSession(level);
      Check(retry.Cleared == 0 && retry.Remaining == 1 && retry.Board.Pieces.Count == level.InitialPieces.Count);
    }
  }),
  ("Puzzle thinking time and floor contact never consume a piece", () =>
  {
    var game = new GameSession(PuzzleLevels.All[0]);
    var original = game.Active;
    game.Advance(100);
    Check(game.Active == original && game.Locked == 0);
    game.Advance(0.05, true);
    Check(game.Active!.Cells.Min(cell => cell.Y) == 1);
    while (game.Move(0, 1)) { }
    game.Advance(100, true);
    Check(game.Locked == 0 && game.Phase == GamePhase.Falling);
  }),
  ("Exhausted puzzle sequences fail without injecting random pieces", () =>
  {
    var game = new GameSession(PuzzleLevels.All[0]);
    while (game.Move(1, 0)) { }
    game.HardDrop();
    Check(game.Phase == GamePhase.Over && game.Remaining == 0 && game.Next.Count == 0 && game.Active is null);
  }),
  ("Final puzzle piece resolves every chain before declaring success", () =>
  {
    var level = new PuzzleLevel(99, "", "", PuzzleGoal.ClearBlocks, 6,
      new[] {
        Piece.Create(-1, Shape.O, 0, 0, 16), Piece.Create(-2, Shape.O, 0, 2, 16), Piece.Create(-3, Shape.O, 0, 4, 16),
        Piece.Create(-4, Shape.O, 1, 0, 14), Piece.Create(-5, Shape.O, 1, 2, 12), Piece.Create(-6, Shape.O, 1, 4, 10)
      }, new[] { Piece.Create(1, Shape.O, 2) });
    var game = new GameSession(level);
    while (game.Move(1, 0)) { }
    game.HardDrop();
    game.SetPaused(true);
    game.Advance(10);
    Check(game.Phase == GamePhase.Clearing && game.Score == 2100);
    game.SetPaused(false);
    for (var step = 0; step < 500 && !game.IsFinished; step++) game.Advance(0.05);
    Check(game.Phase == GamePhase.Won && game.Score == 9200 && game.BestChain == 2);
  }),
  ("A blocked puzzle spawn fails and finite previews preserve sequence order", () =>
  {
    var level = new PuzzleLevel(99, "", "", PuzzleGoal.ClearBlocks, 3,
      new[] { Piece.Create(-1, Shape.O, 1, 4, 0) },
      new[] { Piece.Create(1, Shape.O, 0), Piece.Create(2, Shape.T, 1), Piece.Create(3, Shape.I, 2) });
    var game = new GameSession(level);
    Check(game.Phase == GamePhase.Over && game.Next.Select(piece => piece.Shape).SequenceEqual(new[] { Shape.T, Shape.I }));
  }),
  ("Whole blocks are counted, not their four cells", () =>
  {
    var board = BoardOf(Piece.Create(1, Shape.O, 0, 0, 16), Piece.Create(2, Shape.O, 0, 2, 16));
    Check(board.FindMatches().Count == 0);
  }),
  ("Non-linear edge-connected groups match", () =>
  {
    var board = BoardOf(Piece.Create(1, Shape.O, 0, 0, 16), Piece.Create(2, Shape.O, 0, 2, 16), Piece.Create(3, Shape.T, 0, 0, 14));
    Check(board.FindMatches().Count == 3);
  }),
  ("Diagonal contact does not match", () =>
  {
    var board = BoardOf(Piece.Create(1, Shape.O, 0, 0, 16), Piece.Create(2, Shape.O, 0, 2, 14), Piece.Create(3, Shape.O, 0, 4, 12));
    Check(board.FindMatches().Count == 0);
  }),
  ("Different colors do not connect", () =>
  {
    var board = BoardOf(Piece.Create(1, Shape.O, 0, 0, 16), Piece.Create(2, Shape.O, 1, 2, 16), Piece.Create(3, Shape.O, 0, 4, 16));
    Check(board.FindMatches().Count == 0);
  }),
  ("A full row does not clear by itself", () =>
  {
    var board = BoardOf(Piece.Create(1, Shape.I, 0, 0, 17), Piece.Create(2, Shape.I, 1, 4, 17), Piece.Create(3, Shape.O, 2, 8, 16));
    Check(board.FindMatches().Count == 0);
  }),
  ("Partial support preserves an entire overhanging shape", () =>
  {
    var board = BoardOf(Piece.Create(1, Shape.O, 1, 0, 16), Piece.Create(2, Shape.I, 0, 1, 3));
    Settle(board);
    Check(board.Pieces.Single(piece => piece.Id == 2).Cells.SequenceEqual(new Cell[] { new(1, 15), new(2, 15), new(3, 15), new(4, 15) }));
  }),
  ("Unsupported stacks settle independently of list order", () =>
  {
    foreach (var reverse in new[] { false, true })
    {
      var pieces = new[] { Piece.Create(1, Shape.O, 0, 0, 8), Piece.Create(2, Shape.O, 1, 0, 6) };
      var board = BoardOf(reverse ? pieces.Reverse().ToArray() : pieces);
      Settle(board);
      Check(board.Pieces.Single(piece => piece.Id == 1).Cells.Min(cell => cell.Y) == 16);
      Check(board.Pieces.Single(piece => piece.Id == 2).Cells.Min(cell => cell.Y) == 14);
    }
  }),
  ("Removing support produces a second whole-block match", () =>
  {
    var board = BoardOf(
      Piece.Create(1, Shape.O, 0, 0, 16), Piece.Create(2, Shape.O, 0, 2, 16), Piece.Create(3, Shape.O, 0, 4, 16),
      Piece.Create(4, Shape.O, 1, 0, 14), Piece.Create(5, Shape.O, 1, 2, 12), Piece.Create(6, Shape.O, 1, 4, 10));
    var first = board.FindMatches();
    Check(first.Count == 3);
    board.Remove(first);
    Settle(board);
    var second = board.FindMatches();
    Check(second.Count == 3 && second.All(piece => piece.Color == 1));
  }),
  ("Separate matching groups clear in the same wave", () =>
  {
    var board = BoardOf(
      Piece.Create(1, Shape.O, 0, 0, 16), Piece.Create(2, Shape.O, 0, 2, 16), Piece.Create(3, Shape.O, 0, 4, 16),
      Piece.Create(4, Shape.O, 1, 0, 10), Piece.Create(5, Shape.O, 1, 2, 10), Piece.Create(6, Shape.O, 1, 4, 10));
    Check(board.FindMatches().Count == 6);
  }),
  ("Scoring rewards altitude, compactness, larger clears, and later combo steps", () =>
  {
    var floor = new[] { Piece.Create(1, Shape.O, 0, 0, 16), Piece.Create(2, Shape.O, 0, 2, 16), Piece.Create(3, Shape.O, 0, 4, 16) };
    var basic = ScoreRules.Calculate(floor, 1);
    Check(basic == new ScoreAward(1000, 0, 1100, 0, 0) && basic.Total == 2100);
    var high = ScoreRules.Calculate(floor.Select(piece => piece.Offset(0, -6)).ToArray(), 1);
    Check(high.Altitude == 600 && high.Total == basic.Total + 600);
    var wide = ScoreRules.Calculate(new[] { floor[0], floor[1].Offset(2, 0), floor[2].Offset(4, 0) }, 1);
    Check(wide.Compactness == 800 && wide.Total < basic.Total);
    var six = ScoreRules.Calculate(floor.Concat(floor.Select((piece, index) => piece with { Id = 4 + index }).Select(piece => piece.Offset(0, -2))).ToArray(), 1);
    Check(six.MultiClear == 7000 && six.Total == 9800);
    Check(ScoreRules.Calculate(floor, 2).Combo == 5000);
    Check(ScoreRules.Calculate(floor, 3).Combo == 10000);
  }),
  ("Demo scores through the new economy and advances to the next turn", () =>
  {
    var game = GameSession.CreateDemo();
    var observed = 0;
    game.Matched += wave => observed += wave.Pieces.Count;
    game.HardDrop();
    Check(game.Score == 2100 && game.Cleared == 3 && game.BestChain == 1 && observed == 3);
    Check(game.Phase == GamePhase.Clearing && game.Active is null);
    AdvanceUntil(game, GamePhase.Settling);
    AdvanceUntil(game, GamePhase.Falling);
    Check(game.Active is not null && game.Board.Pieces.Count == 0);
  }),
  ("Pause freezes input and clear animation timers", () =>
  {
    var game = GameSession.CreateDemo();
    game.SetPaused(true);
    var before = game.Active;
    game.HardDrop();
    game.Move(1, 0);
    game.Rotate();
    game.Advance(2);
    Check(game.Active == before && game.Score == 0);
    game.SetPaused(false);
    game.HardDrop();
    game.SetPaused(true);
    game.Advance(2);
    Check(game.Phase == GamePhase.Clearing && game.ClearProgress == 0);
    game.SetPaused(false);
    AdvanceUntil(game, GamePhase.Settling);
  }),
  ("Session adds five thousand points for the second combo step", () =>
  {
    var board = BoardOf(
      Piece.Create(-1, Shape.O, 0, 0, 16), Piece.Create(-2, Shape.O, 0, 2, 16), Piece.Create(-3, Shape.O, 0, 4, 16),
      Piece.Create(-4, Shape.O, 1, 0, 14), Piece.Create(-5, Shape.O, 1, 2, 12), Piece.Create(-6, Shape.O, 1, 4, 10));
    var game = Enumerable.Range(0, 100)
      .Select(seed => new GameSession(new Random(seed), board))
      .First(candidate => candidate.Active!.Color >= 2);
    for (var i = 0; i < 10; i++) game.Move(1, 0);
    game.HardDrop();
    for (var i = 0; i < 500 && game.Phase is GamePhase.Clearing or GamePhase.Settling; i++) game.Advance(0.05);
    Check(game.Phase == GamePhase.Falling);
    Check(game.Score == 9200 && game.BestChain == 2 && game.Cleared == 6);
  }),
  ("Soft drop moves the active piece and grounded pieces lock after the delay", () =>
  {
    var game = GameSession.CreateDemo();
    game.Advance(0.05, softDrop: true);
    Check(game.Active!.Cells.Min(cell => cell.Y) == 1);
    while (game.Move(0, 1)) { }
    game.Advance(0.2);
    Check(game.Locked == 0);
    game.Advance(0.19);
    Check(game.Locked == 1 && game.Score == 2100);
  }),
  ("Match resolution rejects new movement and hard drops", () =>
  {
    var game = GameSession.CreateDemo();
    game.HardDrop();
    Check(!game.Move(1, 0) && !game.Rotate());
    game.HardDrop();
    Check(game.Locked == 1 && game.Score == 2100);
  }),
  ("Ghost and movement respect the floor and walls", () =>
  {
    var game = new GameSession(new Random(7));
    for (var i = 0; i < 15; i++) game.Move(-1, 0);
    Check(game.Active!.Cells.Min(cell => cell.X) == 0);
    Check(!game.Move(-1, 0));
    Check(game.Ghost()!.Cells.Max(cell => cell.Y) == Board.Height - 1);
    game.Rotate();
    Check(game.Board.CanPlace(game.Active!));
  }),
  ("Blocked spawn ends the game and input stays disabled", () =>
  {
    var board = new Board();
    for (var x = 0; x < 10; x += 2) board.Add(Piece.Create(-1 - x, Shape.O, 0, x, 0));
    var game = new GameSession(new Random(1), board);
    Check(game.Phase == GamePhase.Over && game.Active is null);
    game.HardDrop();
    game.Advance(10);
    Check(!game.Move(1, 0) && game.Score == 0);
  }),
  ("Seven-bag generator contains every shape and unique identities", () =>
  {
    var bag = new PieceBag(new Random(5));
    var first = Enumerable.Range(0, 7).Select(_ => bag.Take()).ToArray();
    var second = Enumerable.Range(0, 7).Select(_ => bag.Take()).ToArray();
    Check(first.Select(piece => piece.Shape).Distinct().Count() == 7);
    Check(second.Select(piece => piece.Shape).Distinct().Count() == 7);
    Check(first.Concat(second).Select(piece => piece.Id).Distinct().Count() == 14);
    Check(first.Concat(second).All(piece => piece.Color is >= 0 and < PieceBag.ColorCount));
  }),
  ("Weighted color deck has five bounded frequencies", () =>
  {
    var bag = new PieceBag(new Random(5));
    var colors = Enumerable.Range(0, 40).Select(_ => bag.Take().Color).ToArray();
    var counts = colors.GroupBy(color => color).ToDictionary(group => group.Key, group => group.Count());
    Check(counts.Count == PieceBag.ColorCount);
    Check(Enumerable.Range(0, PieceBag.ColorCount).Select(color => counts[color]).SequenceEqual([10, 9, 8, 7, 6]));
  }),
  ("Seeded games keep four-cell shapes valid throughout play", () =>
  {
    for (var seed = 0; seed < 20; seed++)
    {
      var random = new Random(seed);
      var game = new GameSession(new Random(seed));
      for (var turn = 0; turn < 100 && game.Phase != GamePhase.Over; turn++)
      {
        for (var i = random.Next(4); i > 0; i--) game.Rotate();
        var distance = random.Next(-5, 6);
        for (var i = 0; i < Math.Abs(distance); i++) game.Move(Math.Sign(distance), 0);
        game.HardDrop();
        var limit = 0;
        while (game.Phase is GamePhase.Clearing or GamePhase.Settling)
        {
          game.Advance(0.05);
          Check(++limit < 1000);
        }
        var cells = game.Board.Pieces.SelectMany(piece => piece.Cells).ToArray();
        Check(cells.Distinct().Count() == cells.Length);
        Check(game.Board.Pieces.All(piece => piece.Cells.Count == 4));
        Check(cells.All(cell => cell.X is >= 0 and < Board.Width && cell.Y is >= 0 and < Board.Height));
      }
    }
  })
};

var failures = 0;
foreach (var test in tests)
{
  try { test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
  catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL  {test.Name}: {error}"); }
}
Console.WriteLine($"\n{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string? message = null)
{
  if (!condition) throw new InvalidOperationException(message ?? "Assertion failed.");
}

static void AdvanceUntil(GameSession game, GamePhase phase)
{
  for (var step = 0; step < 500 && game.Phase != phase; step++) game.Advance(0.02);
  Check(game.Phase == phase, $"Expected {phase}, got {game.Phase}.");
}

static Board BoardOf(params Piece[] pieces)
{
  var board = new Board();
  foreach (var piece in pieces) board.Add(piece);
  return board;
}

static void Settle(Board board)
{
  var steps = 0;
  while (board.StepGravity()) Check(++steps <= Board.Height);
}

static GameSession Play(PuzzleLevel level, IReadOnlyList<(int X, int Rotations)> moves, out IReadOnlyList<string> waves)
{
  var game = new GameSession(level);
  Check(!game.Board.StepGravity() && game.Board.FindMatches().Count == 0);
  Check(game.Remaining == moves.Count && game.Next.Count == moves.Count - 1);
  var observed = new List<string>();
  game.Matched += wave => observed.Add(string.Concat(wave.Pieces.Select(piece => piece.Color).Distinct().Order()));
  foreach (var (x, rotations) in moves)
  {
    for (var turn = 0; turn < rotations; turn++) Check(game.Rotate());
    while (game.Active!.Cells.Min(cell => cell.X) != x)
      Check(game.Move(Math.Sign(x - game.Active.Cells.Min(cell => cell.X)), 0));
    game.HardDrop();
    for (var tick = 0; tick < 500 && game.Phase is GamePhase.Clearing or GamePhase.Settling; tick++) game.Advance(0.05);
  }
  waves = observed;
  return game;
}

sealed record ChallengeSolution(int Level, (int X, int Rotations)[] Moves, int Score, int Chain, string[] Waves);
