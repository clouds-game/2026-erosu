using ChromaDrop.Core;
using ChromaDrop.Localization;
using Godot;

namespace ChromaDrop.Game;

public partial class GameHud : Control
{
  public UiText Texts { get; set; } = new();
  private OptionButton _mode = null!;
  private Label _goal = null!;
  public event Action<int>? LevelRequested;
  private Label _scoreTitle = null!;
  private Label _nextTitle = null!;
  private Label _keys = null!;
  private Button _restart = null!;
  private Button _demo = null!;
  private Button _language = null!;
  public event Action? LanguageRequested;
  private Label _score = null!;
  private Label _best = null!;
  private Label _stats = null!;
  private NextView _next = null!;
  private Button _pause = null!;
  private Button _sound = null!;
  public event Action? PauseRequested;
  public event Action? RestartRequested;
  public event Action? DemoRequested;
  public event Action? SoundRequested;

  public override void _Ready()
  {
    _mode = new OptionButton { Position = Vector2.Zero, Size = new Vector2(228, 32) };
    _mode.AddItem("");
    foreach (var level in PuzzleLevels.All) _mode.AddItem("");
    _mode.ItemSelected += index => LevelRequested?.Invoke((int)index);
    _mode.FocusMode = FocusModeEnum.None;
    AddChild(_mode);
    _goal = AddText("", 190, 15, PiecePainter.Text);
    _goal.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    _goal.Size = new Vector2(228, 130);
    _scoreTitle = AddText("", 44, 14, PiecePainter.Muted);
    _score = AddText("0", 67, 36, PiecePainter.Text);
    _best = AddText("", 116, 14, PiecePainter.Muted);
    _nextTitle = AddText("", 158, 14, PiecePainter.Muted);
    _next = new NextView { Position = new Vector2(0, 190), Size = new Vector2(228, 135), MouseFilter = MouseFilterEnum.Ignore };
    AddChild(_next);
    _stats = AddText("", 342, 14, PiecePainter.Muted);
    _stats.AddThemeConstantOverride("line_spacing", 4);
    _keys = AddText("", 430, 14, PiecePainter.Muted);
    _keys.AddThemeConstantOverride("line_spacing", 4);
    _pause = AddButton("", 558, () => PauseRequested?.Invoke());
    _restart = AddButton("", 597, () => RestartRequested?.Invoke());
    _demo = AddButton("", 636, () => DemoRequested?.Invoke());
    _sound = AddButton("", 675, () => SoundRequested?.Invoke());
    _language = AddButton("", 714, () => LanguageRequested?.Invoke());
  }

  public void Refresh(GameSession game, int best, bool soundEnabled, int selectedLevel, int completedLevels)
  {
    _mode.SetItemText(0, Texts.Get("free_play"));
    foreach (var level in PuzzleLevels.All)
      _mode.SetItemText(level.Number, $"{level.Number}. {Texts.Get(level.TitleKey)}" + ((completedLevels & (1 << (level.Number - 1))) != 0 ? " *" : ""));
    _mode.Select(selectedLevel);
    _mode.TooltipText = Texts.Get("mode");
    _next.Visible = game.Puzzle is null;
    _goal.Visible = game.Puzzle is not null;
    _goal.Text = game.Puzzle is { } puzzle ? Texts.Get(puzzle.GoalKey, puzzle.Target) + "\n\n" + Texts.Get("remaining") + $"  {game.Remaining}" : "";
    _scoreTitle.Text = Texts.Get("score");
    _nextTitle.Text = game.Puzzle is null ? Texts.Get("next") : "";
    _score.Text = UiText.Number(game.Score);
    _best.Text = game.Puzzle is null ? Texts.Get("best", UiText.Number(best)) : Texts.Get("puzzle_intro");
    _best.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    _best.AddThemeFontSizeOverride("font_size", game.Puzzle is null ? 14 : 11);
    _stats.Text = $"{Texts.Get("level")}  {game.Level}\n{Texts.Get("cleared")}  {game.Cleared}\n{Texts.Get("chain")}  ×{game.BestChain}";
    if (game.Puzzle is not null) _stats.Text = $"{Texts.Get("cleared")}  {game.Cleared}";
    _keys.Text = $"← →  {Texts.Get("move")}\n↑  {Texts.Get("rotate")}\n↓  {Texts.Get("soft_drop")}\nSpace  {Texts.Get("drop")}";
    _pause.Text = Texts.Get(game.Paused ? "resume" : "pause") + "  [P]";
    if (game.Phase == GamePhase.Won) _pause.Text = Texts.Get(selectedLevel == PuzzleLevels.All.Count ? "chapter_replay" : "next_level") + " [Enter]";
    else if (game.Phase == GamePhase.Over) _pause.Text = Texts.Get("play_again") + " [Enter]";
    _restart.Text = Texts.Get("restart") + "  [R]";
    _demo.Disabled = game.Puzzle is not null && game.IsFinished;
    _demo.Text = Texts.Get(game.Puzzle is null ? "demo" : "hint") + "  [F2]";
    _sound.Text = Texts.Get(soundEnabled ? "sound_on" : "sound_off") + "  [M]";
    _language.Text = UiText.LanguageNames[Array.IndexOf(UiText.Languages, Texts.Language)] + "  >";
    _language.TooltipText = Texts.Get("language");
    _next.Pieces = game.Next;
    _next.QueueRedraw();
  }

  private Label AddText(string text, float y, int fontSize, Color color)
  {
    var label = new Label { Text = text, Position = new Vector2(0, y), Size = new Vector2(228, 25), MouseFilter = MouseFilterEnum.Ignore };
    label.AddThemeFontSizeOverride("font_size", fontSize);
    label.AddThemeColorOverride("font_color", color);
    AddChild(label);
    return label;
  }

  private Button AddButton(string text, float y, Action pressed)
  {
    var button = new Button
    {
      Text = text, Position = new Vector2(0, y), Size = new Vector2(228, 34),
      FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand
    };
    button.AddThemeFontSizeOverride("font_size", 14);
    button.AddThemeColorOverride("font_color", PiecePainter.Text);
    button.AddThemeStyleboxOverride("normal", ButtonStyle(new Color("222a35")));
    button.AddThemeStyleboxOverride("hover", ButtonStyle(new Color("303e4d")));
    button.AddThemeStyleboxOverride("pressed", ButtonStyle(new Color("3b5061")));
    button.AddThemeStyleboxOverride("disabled", ButtonStyle(new Color("181e27")));
    button.Pressed += pressed;
    AddChild(button);
    return button;
  }

  private static StyleBoxFlat ButtonStyle(Color color) => new()
  {
    BgColor = color,
    CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
    CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
  };
}
