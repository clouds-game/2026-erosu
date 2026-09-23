using ChromaDrop.Core;
using ChromaDrop.Localization;
using Godot;

namespace ChromaDrop.Game;

public partial class GameOverlay : Control
{
  [Export] public int Kind { get; set; } // 0 modes, 1 pause, 2 result
  public UiText Texts { get; set; } = new();
  public event Action<int>? LevelRequested;
  public event Action? CloseRequested;
  public event Action? RestartRequested;
  public event Action? ContinueRequested;
  public event Action? SoundRequested;
  public event Action? LanguageRequested;
  public event Action? HintRequested;

  private VBoxContainer _body = null!;

  public override void _Ready()
  {
    var shade = new ColorRect { Color = new Color(0.03f, 0.05f, 0.035f, 0.84f), MouseFilter = MouseFilterEnum.Stop };
    shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    AddChild(shade);
    var center = new CenterContainer { MouseFilter = MouseFilterEnum.Stop };
    center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    AddChild(center);
    var panel = new PanelContainer
    {
      CustomMinimumSize = new Vector2(Kind == 0 ? 740 : 500, 0),
      SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
      SizeFlagsVertical = SizeFlags.ShrinkCenter
    };
    center.AddChild(panel);
    _body = new VBoxContainer();
    _body.AddThemeConstantOverride("separation", 12);
    panel.AddChild(_body);
  }

  public void Render(GameSession game, int selectedLevel, int completedLevels, bool soundEnabled)
  {
    foreach (var child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
    switch (Kind)
    {
      case 0: Modes(selectedLevel, completedLevels); break;
      case 1: Pause(game, soundEnabled); break;
      case 2: Result(game); break;
    }
  }

  private void Modes(int selectedLevel, int completedLevels)
  {
    Header(Texts.Get("choose_mode"));
    var columns = new HBoxContainer();
    columns.AddThemeConstantOverride("separation", 24);
    _body.AddChild(columns);
    foreach (var tutorial in new[] { true, false })
    {
      var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
      column.AddThemeConstantOverride("separation", 10);
      columns.AddChild(column);
      AddLabel(column, Texts.Get(tutorial ? "learn" : "challenges"), 25, PixelUi.Gold);
      foreach (var level in PuzzleLevels.All.Where(level => level.IsTutorial == tutorial))
      {
        var done = (completedLevels & (1 << (level.Number - 1))) != 0;
        var text = PuzzleLevels.DisplayNumber(level).ToString("00") + "  " + Texts.Get(level.TitleKey) + (done ? "  ✓" : "");
        var button = AddButton(column, text, selectedLevel == level.Number);
        button.Pressed += () => LevelRequested?.Invoke(level.Number);
      }
    }
    Space(_body, 12);
    var free = AddButton(_body, Texts.Get("free_play"), selectedLevel == 0);
    free.Pressed += () => LevelRequested?.Invoke(0);
  }

  private void Pause(GameSession game, bool soundEnabled)
  {
    Header(Texts.Get("paused"));
    var resume = AddButton(_body, Texts.Get("resume"), true);
    resume.Pressed += () => CloseRequested?.Invoke();
    var restart = AddButton(_body, Texts.Get("restart"));
    restart.Pressed += () => RestartRequested?.Invoke();
    Space(_body, 6);
    var settings = new HBoxContainer();
    settings.AddThemeConstantOverride("separation", 10);
    _body.AddChild(settings);
    var sound = AddButton(settings, Texts.Get(soundEnabled ? "sound_on" : "sound_off"));
    sound.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    sound.Pressed += () => SoundRequested?.Invoke();
    var language = AddButton(settings, UiText.LanguageNames[Array.IndexOf(UiText.Languages, Texts.Language)]);
    language.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    language.Pressed += () => LanguageRequested?.Invoke();
    Space(_body, 5);
    AddLabel(_body, Texts.Get("controls_line_1"), 18, PixelUi.Muted);
    AddLabel(_body, Texts.Get("controls_line_2"), 18, PixelUi.Muted);
    if (game.Puzzle is not null)
    {
      var hint = AddButton(_body, Texts.Get("hint"));
      hint.Pressed += () => HintRequested?.Invoke();
    }
    Space(_body, 5);
    var modes = AddButton(_body, Texts.Get("choose_mode"));
    modes.Pressed += () => LevelRequested?.Invoke(-1);
  }

  private void Result(GameSession game)
  {
    var won = game.Phase == GamePhase.Won;
    Header(Texts.Get(won ? PuzzleLevels.CompletionKey(game.Puzzle!) : game.Puzzle is null ? "game_over" : "puzzle_failed"));
    AddLabel(_body, UiText.Number(game.Score), 56, PixelUi.Gold);
    AddLabel(_body, Texts.Get("chain") + " ×" + game.BestChain, 21, PixelUi.Muted);
    Space(_body, 14);
    var primary = AddButton(_body, Texts.Get(won ? PuzzleLevels.ContinueKey(game.Puzzle!) : "play_again"), true);
    primary.Pressed += () => ContinueRequested?.Invoke();
    if (won)
    {
      var retry = AddButton(_body, Texts.Get("restart"));
      retry.Pressed += () => RestartRequested?.Invoke();
    }
    var modes = AddButton(_body, Texts.Get("choose_mode"));
    modes.Pressed += () => LevelRequested?.Invoke(-1);
  }

  private void Header(string text)
  {
    var row = new HBoxContainer();
    _body.AddChild(row);
    AddLabel(row, text, 32, PixelUi.Cream).SizeFlagsHorizontal = SizeFlags.ExpandFill;
    if (Kind == 2) return;
    var close = AddButton(row, "×");
    close.CustomMinimumSize = new Vector2(54, 50);
    close.Pressed += () => CloseRequested?.Invoke();
  }

  private static Label AddLabel(Node parent, string text, int size, Color color)
  {
    var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    label.AddThemeFontSizeOverride("font_size", size);
    label.AddThemeColorOverride("font_color", color);
    parent.AddChild(label);
    return label;
  }

  private static Button AddButton(Node parent, string text, bool primary = false)
  {
    var button = new Button
    {
      Text = text,
      CustomMinimumSize = new Vector2(0, 52),
      FocusMode = FocusModeEnum.All,
      Alignment = HorizontalAlignment.Center
    };
    if (primary) button.ThemeTypeVariation = "PrimaryButton";
    parent.AddChild(button);
    return button;
  }

  private static void Space(BoxContainer parent, float height) => parent.AddChild(new Control
  {
    CustomMinimumSize = new Vector2(0, height),
    MouseFilter = MouseFilterEnum.Ignore
  });
}
