using ChromaDrop.Core;
using ChromaDrop.Localization;
using Godot;

namespace ChromaDrop.Game;

public partial class BoardView : Control
{
  public GameSession? Session { get; set; }
  public UiText Texts { get; set; } = new();
  public string NoticeKey { get; set; } = "";
  public object[] NoticeArguments { get; set; } = [];
  private string Notice => NoticeKey.Length == 0 ? "" : Texts.Get(NoticeKey, NoticeArguments);
  public const float CellSize = 42;

  public override void _Draw()
  {
    DrawRect(new Rect2(Vector2.Zero, Size), PiecePainter.BoardColor);
    for (var x = 0; x < Board.Width; x++)
      for (var y = 0; y < Board.Height; y++)
        DrawCircle(new Vector2(x + 0.5f, y + 0.5f) * CellSize, 1, new Color("303946"));
    var game = Session;
    if (game is null) return;
    foreach (var piece in game.Board.Pieces) PiecePainter.Draw(this, piece, Vector2.Zero, CellSize);
    if (game.Phase == GamePhase.Falling)
    {
      var ghost = game.Ghost();
      if (ghost is not null) PiecePainter.Draw(this, ghost, Vector2.Zero, CellSize, ghost: true);
      if (game.Active is not null) PiecePainter.Draw(this, game.Active, Vector2.Zero, CellSize);
    }
    if (game.Wave is not null)
    {
      var flash = 0.3f + 0.5f * Mathf.Sin((float)game.ClearProgress * Mathf.Pi);
      foreach (var piece in game.Wave.Pieces) PiecePainter.Draw(this, piece, Vector2.Zero, CellSize, flash: flash);
    }
    DrawRect(new Rect2(Vector2.Zero, Size), new Color("394452"), false, 1);
    if (game.Paused || game.IsFinished)
    {
      DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.05f, 0.07f, 0.85f));
      CenterText(Texts.Get(game.Paused ? "paused" : game.Phase == GamePhase.Won ? (game.Puzzle!.Number == PuzzleLevels.All.Count ? "chapter_done" : "puzzle_won") : game.Puzzle is not null ? "puzzle_failed" : "game_over"), Size.Y / 2 - 20, 30, PiecePainter.Text);
      CenterText(game.Paused ? Texts.Get("resume_hint") : Texts.Get("score_value", UiText.Number(game.Score)), Size.Y / 2 + 20, 17, PiecePainter.Muted);
      if (!game.Paused) CenterText(Texts.Get(game.Phase == GamePhase.Won ? "continue_hint" : "restart_hint"), Size.Y / 2 + 55, 17, PiecePainter.Muted);
    }
    else if (Notice.Length > 0)
    {
      var font = GetThemeDefaultFont();
      var textHeight = font.GetMultilineStringSize(Notice, width: Size.X - 48, fontSize: 16).Y;
      DrawRect(new Rect2(12, 120, Size.X - 24, textHeight + 20), new Color(0.06f, 0.08f, 0.1f, 0.93f));
      DrawMultilineString(font, new Vector2(24, 130), Notice, alignment: HorizontalAlignment.Center,
        width: Size.X - 48, fontSize: 16, modulate: PiecePainter.Text);
    }
  }

  private void CenterText(string text, float y, int fontSize, Color color)
  {
    var font = GetThemeDefaultFont();
    var width = font.GetStringSize(text, fontSize: fontSize).X;
    if (width > Size.X - 32)
    {
      fontSize = Math.Max(10, (int)(fontSize * (Size.X - 32) / width));
      width = font.GetStringSize(text, fontSize: fontSize).X;
    }
    DrawString(font, new Vector2((Size.X - width) / 2, y), text, fontSize: fontSize, modulate: color);
  }
}
