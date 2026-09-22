namespace ChromaDrop.Core;

public sealed record ScoreAward(
  int Base,
  int Altitude,
  int Compactness,
  int MultiClear,
  int Combo)
{
  public int Total => Base + Altitude + Compactness + MultiClear + Combo;
}

public static class ScoreRules
{
  public const int BasePoints = 1000;
  public const int AltitudePerRow = 100;
  public const int ComboPerStep = 5000;
  public const int MultiClearExponentCap = 3;

  public static ScoreAward Calculate(IReadOnlyList<Piece> pieces, int chain)
  {
    if (pieces.Count < 3) throw new ArgumentException("A score requires at least three matching blocks.", nameof(pieces));
    if (chain < 1) throw new ArgumentOutOfRangeException(nameof(chain));

    var cells = pieces.SelectMany(piece => piece.Cells).ToArray();
    var altitudeRows = Board.Height - 1 - cells.Max(cell => cell.Y);
    var width = cells.Max(cell => cell.X) - cells.Min(cell => cell.X) + 1;
    var height = cells.Max(cell => cell.Y) - cells.Min(cell => cell.Y) + 1;
    var compactness = RoundToHundred(3000d * pieces.Count / (width + height));
    var exponent = Math.Min(Math.Max(0, pieces.Count - 3), MultiClearExponentCap);
    var multiClear = BasePoints * ((1 << exponent) - 1);
    return new ScoreAward(
      BasePoints,
      altitudeRows * AltitudePerRow,
      compactness,
      multiClear,
      (chain - 1) * ComboPerStep);
  }

  private static int RoundToHundred(double value) =>
    (int)Math.Round(value / 100, MidpointRounding.AwayFromZero) * 100;
}
