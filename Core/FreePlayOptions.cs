namespace ChromaDrop.Core;

public sealed record FreePlayOptions(string ColorProfileId = "rare_seven", bool AnchoredBlocks = false, bool EnclosedFill = false)
{
  public ColorProfile Colors => ColorProfile.Find(ColorProfileId);
  public string ScoreKey => ColorProfileId == "rare_seven" ? (AnchoredBlocks, EnclosedFill) switch
  {
    (true, true) => "fill_anchored_v1",
    (false, true) => "fill_v1",
    (true, false) => "anchored_v2",
    _ => "rare_seven_v1"
  } : $"{Colors.Id}_{(AnchoredBlocks ? "anchored" : "plain")}_{(EnclosedFill ? "fill" : "open")}_v1";

  public GameSession CreateSession(Random? random = null) =>
    new(random, colors: Colors, anchoredBlocks: AnchoredBlocks, enclosedFill: EnclosedFill);
}
