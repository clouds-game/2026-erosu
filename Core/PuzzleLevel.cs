namespace ChromaDrop.Core;

public enum PuzzleGoal { ClearBlocks, ClearBoard }

public sealed record PuzzleLevel(
  int Number,
  string TitleKey,
  string HintKey,
  PuzzleGoal Goal,
  int Target,
  IReadOnlyList<Piece> InitialPieces,
  IReadOnlyList<Piece> Sequence,
  bool IsTutorial = false)
{
  public string GoalKey => Goal == PuzzleGoal.ClearBoard ? "goal_board" : "goal_blocks";
  public bool IsComplete(GameSession game) => Goal == PuzzleGoal.ClearBoard
    ? game.Board.Pieces.Count == 0
    : game.Cleared >= Target;
}

public static class PuzzleLevels
{
  public const int DefaultLevel = 4;
  public static int ProgressMask => (1 << All.Count) - 1;
  public static int DisplayNumber(PuzzleLevel puzzle) => All.Where(level => level.IsTutorial == puzzle.IsTutorial)
    .TakeWhile(level => level.Number != puzzle.Number).Count() + 1;
  public static bool IsLastTutorial(PuzzleLevel puzzle) => puzzle.IsTutorial && puzzle.Number == All.Last(level => level.IsTutorial).Number;
  public static int NextLevel(int number)
  {
    var current = All.Single(level => level.Number == number);
    return All.Where(level => level.IsTutorial == current.IsTutorial)
      .SkipWhile(level => level.Number != number).Skip(1).FirstOrDefault()?.Number ?? DefaultLevel;
  }
  public static string CompletionKey(PuzzleLevel puzzle) => puzzle.IsTutorial
    ? (IsLastTutorial(puzzle) ? "learn_done" : "puzzle_won") : "challenge_done";
  public static string ContinueKey(PuzzleLevel puzzle) => puzzle.IsTutorial
    ? (IsLastTutorial(puzzle) ? "start_challenge" : "next_level") : "replay_challenge";

  public static IReadOnlyList<PuzzleLevel> All { get; } = Array.AsReadOnly(new[]
  {
    new PuzzleLevel(1, "puzzle_1", "hint_1", PuzzleGoal.ClearBlocks, 3,
      new[] { Piece.Create(-1, Shape.O, 0, 0, 16), Piece.Create(-2, Shape.O, 0, 2, 16) },
      new[] { Piece.Create(1, Shape.O, 0) }, IsTutorial: true),
    new PuzzleLevel(2, "puzzle_2", "hint_2", PuzzleGoal.ClearBlocks, 3,
      new[] { Piece.Create(-1, Shape.O, 0, 0, 16), Piece.Create(-2, Shape.O, 1, 2, 16), Piece.Create(-3, Shape.O, 0, 2, 14) },
      new[] { Piece.Create(1, Shape.O, 0) }, IsTutorial: true),
    new PuzzleLevel(3, "puzzle_3", "hint_3", PuzzleGoal.ClearBoard, 0,
      new[] { Piece.Create(-1, Shape.O, 0, 0, 16), Piece.Create(-2, Shape.O, 0, 6, 16) },
      new[] { Piece.Create(1, Shape.I, 0) }, IsTutorial: true),
    new PuzzleLevel(4, "challenge_1", "challenge_hint_1", PuzzleGoal.ClearBoard, 0,
      new[] {
        Piece.Create(-1, Shape.I, 2, 1, 17),
        Piece.Create(-2, Shape.O, 1, 3, 15),
        new Piece(-3, Shape.T, 2, new Cell[] { new(7, 16), new(8, 16), new(9, 16), new(8, 17) }),
        Piece.Create(-4, Shape.O, 0, 4, 13)
      },
      new[] {
        Piece.Create(1, Shape.S, 0), Piece.Create(2, Shape.L, 1), Piece.Create(3, Shape.O, 2),
        Piece.Create(4, Shape.J, 0), Piece.Create(5, Shape.J, 1)
      })
  });
}
