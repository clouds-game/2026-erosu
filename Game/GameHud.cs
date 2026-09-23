using ChromaDrop.Core;
using ChromaDrop.Localization;
using Godot;

namespace ChromaDrop.Game;

// A centered board with two sparse information rails. The scene owns geometry;
// this class only constructs each rail and updates its state.
public partial class GameHud : HBoxContainer
{
  public UiText Texts { get; set; } = new();
  public event Action? LevelsRequested;
  public event Action? PauseRequested;

  private Label _stage = null!;
  private Label _instruction = null!;
  private Label _key = null!;
  private Label _feedback = null!;
  private Label _speed = null!;
  private Button _levels = null!;
  private VBoxContainer _progress = null!;
  private readonly TextureRect[] _progressTiles = new TextureRect[6];
  private PanelContainer _nextPanel = null!;
  private NextView _next = null!;
  private string _feedbackText = "";

  public override void _Ready()
  {
    GetNode<PanelContainer>("BoardFrame").AddThemeStyleboxOverride("panel",
      PixelUi.Style("res://Assets/PixelUI/Ancient/brown.png", 8, 12));
    var left = GetNode<VBoxContainer>("Left");
    var right = GetNode<VBoxContainer>("Right");
    left.AddThemeConstantOverride("separation", 12);
    right.AddThemeConstantOverride("separation", 12);

    _stage = AddLabel(left, "", 27, PixelUi.Cream);
    _stage.HorizontalAlignment = HorizontalAlignment.Center;
    Spacer(left);
    _progress = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
    _progress.AddThemeConstantOverride("separation", 12);
    left.AddChild(_progress);
    for (var i = 0; i < _progressTiles.Length; i++)
    {
      var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
      _progress.AddChild(center);
      _progressTiles[i] = PixelUi.Tile(PiecePainter.Colors[0], new Vector2(44, 44));
      center.AddChild(_progressTiles[i]);
    }
    Spacer(left);
    _speed = AddLabel(left, "", 21, PixelUi.Muted);
    _speed.HorizontalAlignment = HorizontalAlignment.Center;
    _levels = AddButton(left, "");
    _levels.Pressed += () => LevelsRequested?.Invoke();

    var pauseRow = new HBoxContainer();
    right.AddChild(pauseRow);
    pauseRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    var pause = AddButton(pauseRow, "Ⅱ");
    pause.CustomMinimumSize = new Vector2(64, 54);
    pause.AddThemeFontSizeOverride("font_size", 28);
    pause.Pressed += () => PauseRequested?.Invoke();
    Spacer(right);
    _instruction = AddLabel(right, "", 25, PixelUi.Cream, true);
    _instruction.HorizontalAlignment = HorizontalAlignment.Center;
    _key = AddLabel(right, "SPACE", 20, PixelUi.Gold);
    _key.HorizontalAlignment = HorizontalAlignment.Center;
    _feedback = AddLabel(right, "", 27, PixelUi.Gold, true);
    _feedback.HorizontalAlignment = HorizontalAlignment.Center;
    Spacer(right);
    _nextPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
    _nextPanel.AddThemeStyleboxOverride("panel",
      PixelUi.Style("res://Assets/PixelUI/Ancient/brown.png", 8, 12));
    right.AddChild(_nextPanel);
    _next = new NextView { CustomMinimumSize = new Vector2(104, 270), MouseFilter = MouseFilterEnum.Ignore };
    _nextPanel.AddChild(_next);
  }

  public void ShowFeedback(string text) => _feedbackText = text;
  public void ClearFeedback() => _feedbackText = "";

  public void Refresh(GameSession game, int best, int selectedLevel)
  {
    var puzzle = game.Puzzle;
    var tutorial = puzzle is { IsTutorial: true };
    _stage.Text = puzzle is null ? UiText.Number(game.Score)
      : PuzzleLevels.DisplayNumber(puzzle).ToString("00") + " / 03";
    _stage.AddThemeFontSizeOverride("font_size", puzzle is null ? 35 : 27);
    _levels.Text = Texts.Get("choose_mode");
    _speed.Visible = puzzle is null;
    _speed.Text = puzzle is null ? "LV " + game.Level.ToString("00") : "";
    _progress.Visible = puzzle is not null;
    if (puzzle is not null)
    {
      var total = tutorial ? 3 : Math.Min(_progressTiles.Length, puzzle.Sequence.Count);
      var remaining = tutorial ? (game.IsFinished ? 3 : 2) : game.Remaining;
      for (var i = 0; i < _progressTiles.Length; i++)
      {
        var icon = _progressTiles[i];
        icon.GetParent<Control>().Visible = i < total;
        icon.Modulate = tutorial ? new Color(PiecePainter.Colors[0], i < remaining ? 1 : 0.25f)
          : new Color(PixelUi.Gold, i < remaining ? 1 : 0.24f);
      }
    }
    _instruction.Visible = tutorial && !game.IsFinished && _feedbackText.Length == 0;
    _key.Visible = _instruction.Visible;
    _instruction.Text = _instruction.Visible ? Texts.Get(puzzle!.Number switch
    {
      1 => "first_instruction",
      2 => "lesson_instruction_2",
      _ => "lesson_instruction_3"
    }) : "";
    _feedback.Text = _feedbackText;
    _feedback.Visible = _feedbackText.Length > 0;
    _nextPanel.Visible = !tutorial && game.Next.Count > 0;
    _next.CustomMinimumSize = new Vector2(104, puzzle is null ? 270 : 420);
    _next.Pieces = game.Next;
    _next.QueueRedraw();
  }

  private static Label AddLabel(Node parent, string value, int size, Color color, bool wrap = false)
  {
    var label = new Label
    {
      Text = value,
      AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
      MouseFilter = MouseFilterEnum.Ignore
    };
    label.AddThemeFontSizeOverride("font_size", size);
    label.AddThemeColorOverride("font_color", color);
    parent.AddChild(label);
    return label;
  }

  private static Button AddButton(Node parent, string value)
  {
    var button = new Button { Text = value, CustomMinimumSize = new Vector2(0, 54), FocusMode = FocusModeEnum.All };
    parent.AddChild(button);
    return button;
  }

  private static void Spacer(BoxContainer parent) => parent.AddChild(new Control
  {
    SizeFlagsVertical = SizeFlags.ExpandFill,
    MouseFilter = MouseFilterEnum.Ignore
  });
}
