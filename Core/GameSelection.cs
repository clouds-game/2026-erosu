namespace ChromaDrop.Core;

public readonly record struct GameSelection
{
  private GameSelection(GameMode mode, int? puzzleNumber)
  {
    Mode = mode;
    PuzzleNumber = puzzleNumber;
  }

  public GameMode Mode { get; }
  public int? PuzzleNumber { get; }

  public static GameSelection FreePlay { get; } = new(GameMode.FreePlay, null);
  public static GameSelection Contamination { get; } = new(GameMode.Contamination, null);

  public static GameSelection Puzzle(int number)
  {
    if (!PuzzleLevels.All.Any(level => level.Number == number))
      throw new ArgumentOutOfRangeException(nameof(number), number, "Unknown puzzle level.");
    return new GameSelection(GameMode.Puzzle, number);
  }
}
