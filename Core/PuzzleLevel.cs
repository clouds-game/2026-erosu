namespace ChromaDrop.Core;

public enum PuzzleGoal { ClearBlocks, ClearBoard }

public sealed record PuzzleLevel(
  int Number,
  string TitleKey,
  string HintKey,
  PuzzleGoal Goal,
  int Target,
  IReadOnlyList<Piece> InitialPieces,
  IReadOnlyList<Piece> Sequence)
{
  public string GoalKey => Goal == PuzzleGoal.ClearBoard ? "goal_board" : "goal_blocks";
  public bool IsComplete(GameSession game) => Goal == PuzzleGoal.ClearBoard
    ? game.Board.Pieces.Count == 0
    : game.Cleared >= Target;
}

public static class PuzzleLevels
{
  public static IReadOnlyList<PuzzleLevel> All { get; } = Array.AsReadOnly(new[]
  {
    new PuzzleLevel(1, "puzzle_1", "hint_1", PuzzleGoal.ClearBlocks, 3,
      new[] { Piece.Create(-1, Shape.O, 0, 0, 16), Piece.Create(-2, Shape.O, 0, 2, 16) },
      new[] { Piece.Create(1, Shape.O, 0) }),
    new PuzzleLevel(2, "puzzle_2", "hint_2", PuzzleGoal.ClearBlocks, 3,
      new[] { Piece.Create(-1, Shape.O, 0, 0, 16), Piece.Create(-2, Shape.O, 1, 2, 16), Piece.Create(-3, Shape.O, 0, 2, 14) },
      new[] { Piece.Create(1, Shape.O, 0) }),
    new PuzzleLevel(3, "puzzle_3", "hint_3", PuzzleGoal.ClearBoard, 0,
      new[] { Piece.Create(-1, Shape.O, 0, 0, 16), Piece.Create(-2, Shape.O, 0, 6, 16) },
      new[] { Piece.Create(1, Shape.I, 0) })
  });
}
