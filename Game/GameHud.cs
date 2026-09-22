using ChromaDrop.Core;
using Godot;

namespace ChromaDrop.Game;

public partial class GameHud : Control
{
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
    AddText("SCORE", 0, 14, PiecePainter.Muted);
    _score = AddText("0", 23, 36, PiecePainter.Text);
    _best = AddText("Best  0", 72, 14, PiecePainter.Muted);
    AddText("NEXT", 120, 14, PiecePainter.Muted);
    _next = new NextView { Position = new Vector2(0, 153), Size = new Vector2(228, 220), MouseFilter = MouseFilterEnum.Ignore };
    AddChild(_next);
    _stats = AddText("", 383, 15, PiecePainter.Muted);
    _stats.AddThemeConstantOverride("line_spacing", 7);
    AddText("← →   Move\n↑        Rotate\n↓        Soft drop\nSpace  Drop", 473, 15, PiecePainter.Muted).AddThemeConstantOverride("line_spacing", 8);
    _pause = AddButton("Pause  [P]", 590, () => PauseRequested?.Invoke());
    AddButton("Restart  [R]", 631, () => RestartRequested?.Invoke());
    AddButton("Match demo  [F2]", 672, () => DemoRequested?.Invoke());
    _sound = AddButton("Sound off  [M]", 713, () => SoundRequested?.Invoke());
  }

  public void Refresh(GameSession game, int best, bool soundEnabled)
  {
    _score.Text = game.Score.ToString("N0");
    _best.Text = $"Best  {best:N0}";
    _stats.Text = $"Level                 {game.Level}\nBlocks cleared    {game.Cleared}\nBest chain          ×{game.BestChain}";
    _pause.Text = game.Paused ? "Resume  [P]" : "Pause  [P]";
    _pause.Disabled = game.Phase == GamePhase.Over;
    _sound.Text = soundEnabled ? "Sound on  [M]" : "Sound off  [M]";
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
