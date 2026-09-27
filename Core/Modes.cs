namespace ChromaDrop.Core;

public enum ModeId { Free = 0, Pollution = 1, Puzzle = 2, BlackWhite = 3 }
public enum HelpAction { Demo, Rules, PuzzleHint }
public enum RecordPolicy { None, Max }
public enum MetricDisplay { Hidden, Counter, Progress }
public enum ContinueAction { Restart, NextPuzzle }
public enum ModeTransitionKind { Rising, Expiring }

public sealed record ModeHelp(string LabelKey, string ContentKey, HelpAction Action);
public sealed record ModePresentation(string NameKey, string IntroKey, string ResultSummaryKey, ModeHelp Help);
public sealed record MetricDefinition(string Key, string LabelKey, RecordPolicy RecordPolicy, MetricDisplay Display,
  int? Maximum = null, string? RecordLabelKey = null);
public sealed record RunMetric(string Key, int Value, MetricDefinition Definition);
public sealed record ModeTransition(
  ModeTransitionKind Kind,
  int Rows,
  IReadOnlyList<Piece> AddedPieces,
  IReadOnlyList<Piece> RemovedPieces,
  bool EndsGame = false)
{
  public ModeTransition(int rows, IReadOnlyList<Piece> addedPieces, bool endsGame = false)
    : this(ModeTransitionKind.Rising, rows, addedPieces, Array.Empty<Piece>(), endsGame) { }
}
public sealed record ModeCapabilities(bool PuzzleObjectives, bool ShowLevel, bool ShowFullSequence);
public sealed record ModeDefinition(ModeId Id, string Token, ModePresentation Presentation,
  ModeCapabilities Capabilities, IReadOnlyList<MetricDefinition> Metrics);

public readonly record struct GameSelection(ModeId Mode, int? PuzzleNumber = null)
{
  public static GameSelection Free => new(ModeId.Free);
  public static GameSelection Pollution => new(ModeId.Pollution);
  public static GameSelection BlackWhite => new(ModeId.BlackWhite);
  public static GameSelection Puzzle(int number) => new(ModeId.Puzzle, number);
  public string Token => Mode == ModeId.Puzzle ? $"puzzle:{PuzzleNumber}" : ModeCatalog.Get(Mode).Token;

  public static bool TryParse(string? token, out GameSelection selection)
  {
    selection = Free;
    var standalone = ModeCatalog.Standalone.FirstOrDefault(mode =>
      string.Equals(token, mode.Token, StringComparison.OrdinalIgnoreCase));
    if (standalone is not null)
    {
      selection = new GameSelection(standalone.Id);
      return true;
    }
    if (token?.StartsWith("puzzle:", StringComparison.OrdinalIgnoreCase) == true
      && int.TryParse(token[7..], out var number)
      && PuzzleLevels.All.Any(level => level.Number == number))
    {
      selection = Puzzle(number);
      return true;
    }
    return false;
  }
}

public static class ModeCatalog
{
  private static readonly MetricDefinition Score = new("score", "score", RecordPolicy.Max, MetricDisplay.Hidden);
  private static readonly IReadOnlyDictionary<ModeId, ModeDefinition> Definitions = new Dictionary<ModeId, ModeDefinition>
  {
    [ModeId.Free] = new(ModeId.Free, "free",
      new("free_play", "intro", "score_value", new("demo", "demo_hint", HelpAction.Demo)),
      new(PuzzleObjectives: false, ShowLevel: true, ShowFullSequence: false),
      new[] { Score }),
    [ModeId.Pollution] = new(ModeId.Pollution, "pollution",
      new("pollution_mode", "pollution_intro", "pollution_result", new("rules", "pollution_rules", HelpAction.Rules)),
      new(PuzzleObjectives: false, ShowLevel: true, ShowFullSequence: false),
      new[] {
        Score,
        new MetricDefinition("purified", "purified", RecordPolicy.Max, MetricDisplay.Counter, RecordLabelKey: "best_purified"),
        new MetricDefinition("next_rise", "pollution_tide", RecordPolicy.None, MetricDisplay.Progress, PollutionModeRules.Interval)
      }),
    [ModeId.BlackWhite] = new(ModeId.BlackWhite, "black_white",
      new("black_white_mode", "black_white_intro", "score_value", new("rules", "black_white_rules", HelpAction.Rules)),
      new(PuzzleObjectives: false, ShowLevel: true, ShowFullSequence: false),
      new[] { Score }),
    [ModeId.Puzzle] = new(ModeId.Puzzle, "puzzle",
      new("challenges", "challenge_intro", "score_value", new("hint", "", HelpAction.PuzzleHint)),
      new(PuzzleObjectives: true, ShowLevel: false, ShowFullSequence: true),
      Array.Empty<MetricDefinition>())
  };

  public static IReadOnlyList<ModeDefinition> Standalone { get; } = Definitions.Values
    .Where(mode => !mode.Capabilities.PuzzleObjectives).OrderBy(mode => mode.Id).ToArray();
  public static ModeDefinition Get(ModeId id) => Definitions[id];
  public static string RecordKey(ModeId mode, string metric) => $"{Get(mode).Token}.{metric}";
}

public static class SeedStreams
{
  public static Random Create(int seed, string name)
  {
    unchecked
    {
      uint hash = (uint)seed ^ 2166136261u;
      foreach (var character in name)
      {
        hash ^= character;
        hash *= 16777619u;
      }
      return new Random((int)(hash & 0x7fffffff));
    }
  }
}

internal interface IGameModeRules
{
  ModeId Id { get; }
  PuzzleLevel? Puzzle { get; }
  bool UsesAutomaticFall { get; }
  int PreviewCount { get; }
  ModeDefinition Definition { get; }
  ModePresentation Presentation { get; }
  bool IsComplete(GameSession game);
  ModeTransition? AfterBoardSettled(GameSession game);
  void OnPiecesCleared(IReadOnlyList<Piece> pieces);
  IReadOnlyList<RunMetric> GetMetrics(GameSession game);
  string ResultTitleKey(GameSession game);
  string ContinueKey(GameSession game);
  ContinueAction GetContinueAction(GameSession game);
  Piece ActivatePiece(GameSession game, Piece piece);
  IReadOnlyList<Piece> FindMatches(Board board);
}

internal abstract class GameModeRulesBase : IGameModeRules
{
  public abstract ModeId Id { get; }
  public virtual PuzzleLevel? Puzzle => null;
  public virtual bool UsesAutomaticFall => true;
  public virtual int PreviewCount => 3;
  public ModeDefinition Definition => ModeCatalog.Get(Id);
  public virtual ModePresentation Presentation => Definition.Presentation;
  public virtual bool IsComplete(GameSession game) => false;
  public virtual ModeTransition? AfterBoardSettled(GameSession game) => null;
  public virtual void OnPiecesCleared(IReadOnlyList<Piece> pieces) { }
  public virtual IReadOnlyList<RunMetric> GetMetrics(GameSession game) => Definition.Metrics
    .Select(metric => new RunMetric(metric.Key, metric.Key == "score" ? game.Score : 0, metric)).ToArray();
  public virtual string ResultTitleKey(GameSession game) => "game_over";
  public virtual string ContinueKey(GameSession game) => "play_again";
  public virtual ContinueAction GetContinueAction(GameSession game) => ContinueAction.Restart;
  public virtual Piece ActivatePiece(GameSession game, Piece piece) => piece;
  public virtual IReadOnlyList<Piece> FindMatches(Board board) => board.FindMatches();
}

internal sealed class FreeModeRules : GameModeRulesBase
{
  public override ModeId Id => ModeId.Free;
}

internal sealed class PuzzleModeRules(PuzzleLevel puzzle) : GameModeRulesBase
{
  public override ModeId Id => ModeId.Puzzle;
  public override PuzzleLevel Puzzle => puzzle;
  public override bool UsesAutomaticFall => false;
  public override int PreviewCount => int.MaxValue;
  public override ModePresentation Presentation => Definition.Presentation with
  {
    IntroKey = puzzle.IsTutorial ? "puzzle_intro" : "challenge_intro"
  };
  public override bool IsComplete(GameSession game) => puzzle.IsComplete(game);
  public override string ResultTitleKey(GameSession game) => game.Phase == GamePhase.Won
    ? PuzzleLevels.CompletionKey(puzzle)
    : "puzzle_failed";
  public override string ContinueKey(GameSession game) => game.Phase == GamePhase.Won
    ? PuzzleLevels.ContinueKey(puzzle)
    : "play_again";
  public override ContinueAction GetContinueAction(GameSession game) => game.Phase == GamePhase.Won
    ? ContinueAction.NextPuzzle
    : ContinueAction.Restart;
}

internal sealed class PollutionModeRules : GameModeRulesBase
{
  public const int Interval = 6;
  public const int RiseRows = 2;
  private readonly PieceBag _bag;
  private readonly Random _placementRandom;
  private int _lastRise;
  private int _purified;

  public PollutionModeRules(int seed)
  {
    _bag = new PieceBag(SeedStreams.Create(seed, "pollution.pieces"), -1, pollution: true);
    _placementRandom = SeedStreams.Create(seed, "pollution.placement");
  }

  public override ModeId Id => ModeId.Pollution;

  public override void OnPiecesCleared(IReadOnlyList<Piece> pieces) =>
    _purified += pieces.Count(piece => piece.IsPollution);

  public override IReadOnlyList<RunMetric> GetMetrics(GameSession game) => Definition.Metrics.Select(metric =>
    new RunMetric(metric.Key, metric.Key switch
    {
      "score" => game.Score,
      "purified" => _purified,
      "next_rise" => Interval - (game.Locked - _lastRise),
      _ => 0
    }, metric)).ToArray();

  public override ModeTransition? AfterBoardSettled(GameSession game)
  {
    if (game.Locked == 0 || game.Locked - _lastRise < Interval) return null;
    _lastRise = game.Locked;
    if (!game.Board.TryRaise(RiseRows)) return new ModeTransition(
      ModeTransitionKind.Rising, RiseRows, Array.Empty<Piece>(), Array.Empty<Piece>(), EndsGame: true);

    var first = _bag.Take();
    var second = _bag.Take();
    var placements = Placements(first, second).ToArray();
    _placementRandom.Shuffle(placements);
    var chosen = placements.FirstOrDefault(pair => !CreatesMatch(game.Board, pair.First, pair.Second));
    if (chosen.First is null) chosen = placements[0];
    game.Board.Add(chosen.First);
    game.Board.Add(chosen.Second);
    return new ModeTransition(
      ModeTransitionKind.Rising, RiseRows, new[] { chosen.First, chosen.Second }, Array.Empty<Piece>());
  }

  private static IEnumerable<(Piece First, Piece Second)> Placements(Piece first, Piece second)
  {
    foreach (var placement in PlacePair(first, second)) yield return placement;
    if (first.Color != second.Color)
      foreach (var placement in PlacePair(first with { Color = second.Color }, second with { Color = first.Color }))
        yield return placement;
  }

  private static IEnumerable<(Piece First, Piece Second)> PlacePair(Piece first, Piece second)
  {
    var firstWidth = first.Cells.Max(cell => cell.X) + 1;
    var secondWidth = second.Cells.Max(cell => cell.X) + 1;
    var firstY = Board.Height - 1 - first.Cells.Max(cell => cell.Y);
    var secondY = Board.Height - 1 - second.Cells.Max(cell => cell.Y);
    for (var firstX = 0; firstX <= Board.Width - firstWidth; firstX++)
      for (var secondX = 0; secondX <= Board.Width - secondWidth; secondX++)
      {
        var separate = firstX + firstWidth <= secondX || secondX + secondWidth <= firstX;
        if (separate) yield return (first.Offset(firstX, firstY), second.Offset(secondX, secondY));
      }
  }

  private static bool CreatesMatch(Board board, Piece first, Piece second)
  {
    var trial = new Board();
    foreach (var piece in board.Pieces) trial.Add(piece);
    trial.Add(first);
    trial.Add(second);
    return trial.FindMatches().Count > 0;
  }
}

internal sealed class BlackWhiteModeRules : GameModeRulesBase
{
  public const int BlackLifetime = 3;
  private readonly Random _revealRandom;

  public BlackWhiteModeRules(int seed) =>
    _revealRandom = SeedStreams.Create(seed, "black_white.reveal");

  public override ModeId Id => ModeId.BlackWhite;

  public override Piece ActivatePiece(GameSession game, Piece piece)
  {
    if (piece.Kind != PieceKind.SpecialUnknown) return piece;
    var kind = _revealRandom.Next(2) == 0 ? PieceKind.Black : PieceKind.White;
    return piece with
    {
      Kind = kind,
      ExpiresAtLock = kind == PieceKind.Black ? game.Locked + BlackLifetime + 1 : null
    };
  }

  public override IReadOnlyList<Piece> FindMatches(Board board) => WildcardMatches(board);

  public override ModeTransition? AfterBoardSettled(GameSession game)
  {
    var expired = game.Board.Pieces
      .Where(piece => piece.Kind == PieceKind.Black && piece.ExpiresAtLock <= game.Locked)
      .ToArray();
    if (expired.Length == 0) return null;
    game.Board.Remove(expired);
    return new ModeTransition(
      ModeTransitionKind.Expiring, 0, Array.Empty<Piece>(), expired);
  }

  private static IReadOnlyList<Piece> WildcardMatches(Board board)
  {
    var matches = new HashSet<int>();
    for (var color = 0; color < PieceBag.ColorCount; color++)
    {
      var candidates = board.Pieces
        .Where(piece => piece.Kind == PieceKind.White ||
          piece.Kind == PieceKind.Normal && piece.Color == color)
        .ToArray();
      var occupied = candidates.SelectMany(piece => piece.Cells.Select(cell => (cell, piece)))
        .ToDictionary(item => item.cell, item => item.piece);
      var visited = new HashSet<int>();
      foreach (var piece in candidates)
      {
        if (!visited.Add(piece.Id)) continue;
        var group = new List<Piece> { piece };
        for (var index = 0; index < group.Count; index++)
          foreach (var cell in group[index].Cells)
            foreach (var neighbor in MatchNeighbors)
              if (occupied.TryGetValue(cell.Offset(neighbor.X, neighbor.Y), out var other) && visited.Add(other.Id))
                group.Add(other);
        if (group.Count >= 3)
          foreach (var match in group) matches.Add(match.Id);
      }
    }
    return board.Pieces.Where(piece => matches.Contains(piece.Id)).ToArray();
  }

  private static readonly Cell[] MatchNeighbors =
    [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
}

public static class GameSessionFactory
{
  public static GameSession Create(GameSelection selection, int seed = 0, Board? board = null)
  {
    IGameModeRules rules = selection.Mode switch
    {
      ModeId.Free => new FreeModeRules(),
      ModeId.Pollution => new PollutionModeRules(seed),
      ModeId.BlackWhite => new BlackWhiteModeRules(seed),
      ModeId.Puzzle => new PuzzleModeRules(PuzzleLevels.All.Single(level => level.Number == selection.PuzzleNumber)),
      _ => throw new ArgumentOutOfRangeException(nameof(selection))
    };
    return Create(rules, seed, board);
  }

  public static GameSession Create(PuzzleLevel puzzle, int seed = 0) => Create(new PuzzleModeRules(puzzle), seed, null);

  private static GameSession Create(IGameModeRules rules, int seed, Board? board)
  {
    var actualBoard = board ?? new Board();
    IPieceSource? bag = null;
    IEnumerable<Piece>? sequence = null;
    if (rules.Puzzle is not null)
    {
      foreach (var piece in rules.Puzzle.InitialPieces) actualBoard.Add(piece);
      sequence = rules.Puzzle.Sequence;
    }
    else
    {
      bag = new PieceBag(SeedStreams.Create(seed, "player.pieces"));
      if (rules.Id == ModeId.BlackWhite)
        bag = new BlackWhitePieceSource(bag, SeedStreams.Create(seed, "black_white.candidates"));
    }
    return new GameSession(rules, actualBoard, bag, sequence);
  }
}
