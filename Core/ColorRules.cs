namespace ChromaDrop.Core;

public sealed record PieceColor(int Id, string Key, string Hex, string Mark, int ClearBonus);

/// <summary>Stable color identities shared by gameplay, clients and JSON observations.</summary>
public static class ColorRules
{
  public const int Count = 7;
  public static IReadOnlyList<PieceColor> All { get; } = Array.AsReadOnly<PieceColor>([
    new(0, "red", "#ff8278", "●", 0),
    new(1, "green", "#8ee2c4", "■", 0),
    new(2, "yellow", "#f6ce71", "+", 0),
    new(3, "purple", "#aaa2f7", "/", 0),
    new(4, "pink", "#e58bd3", "×", 0),
    new(5, "cyan", "#59cde8", "=", 500),
    new(6, "gold", "#d99b24", "◆", 3000)
  ]);
}

public sealed class ColorProfile
{
  public string Id { get; }
  public IReadOnlyList<int> Weights { get; }
  public int BagSize => Weights.Sum();

  private ColorProfile(string id, params int[] weights)
  {
    Id = id;
    Weights = Array.AsReadOnly(weights);
  }

  // Preserve the original bag size/order for historical five-color seeds.
  public static ColorProfile Classic { get; } = new("classic", 10, 9, 8, 7, 6, 0, 0);
  public static ColorProfile RareSix { get; } = new("rare_six", 25, 22, 19, 17, 14, 0, 3);
  public static ColorProfile RareSeven { get; } = new("rare_seven", 22, 20, 18, 16, 14, 7, 3);
  public static ColorProfile Default => RareSeven;
  public static IReadOnlyList<ColorProfile> All { get; } = Array.AsReadOnly([Classic, RareSix, RareSeven]);

  public static ColorProfile Find(string id) => All.FirstOrDefault(profile => profile.Id == id)
    ?? throw new ArgumentException($"Unknown color profile: {id}", nameof(id));
}
