using ChromaDrop.Core;
using ChromaDrop.Localization;
using Godot;

namespace ChromaDrop.Game;

public partial class GameController : Control
{
  private GameSession _game = null!;
  private BoardView _board = null!;
  private GameHud _hud = null!;
  private GameAudio _audio = null!;
  private readonly UiText _texts = new();
  private int _selectedLevel = PuzzleLevels.DefaultLevel;
  private int _completedLevels;
  private int _best;
  private double _noticeTimer;
  private int _heldDirection;
  private double _repeatTimer;
  private bool _repeating;
  private const string SavePath = "user://progress.cfg";

  public override void _Ready()
  {
    ConfigureWindow();
    RegisterInput();
    _board = GetNode<BoardView>("Board");
    _hud = GetNode<GameHud>("Hud");
    _audio = new GameAudio();
    AddChild(_audio);
    _texts.SetLanguage(OS.GetLocale());
    var save = new ConfigFile();
    if (save.Load(SavePath) == Error.Ok)
    {
      _texts.SetLanguage(save.GetValue("settings", "language", _texts.Language).AsString());
      _completedLevels = save.GetValue("progress", "completed_levels", 0).AsInt32() & PuzzleLevels.ProgressMask;
      _best = Math.Max(0, save.GetValue("progress", "best_score", 0).AsInt32());
      _audio.Enabled = save.GetValue("settings", "sound_enabled", false).AsBool();
    }
    ApplyLanguageFont();
    _hud.Texts = _texts;
    _board.Texts = _texts;
    _hud.LanguageRequested += () =>
    {
      var index = Array.IndexOf(UiText.Languages, _texts.Language);
      _texts.SetLanguage(UiText.Languages[(index + 1) % UiText.Languages.Length]);
      ApplyLanguageFont();
      SaveProgress();
    };
    _hud.LevelRequested += number => { _selectedLevel = number; Start(); };
    _hud.PauseRequested += () => { if (_game.IsFinished) Confirm(); else TogglePause(); };
    _hud.RestartRequested += () => Start();
    _hud.DemoRequested += ShowHintOrDemo;
    _hud.SoundRequested += ToggleSound;
    Start(OS.GetCmdlineUserArgs().Contains("--demo"));
  }

  public override void _Process(double delta)
  {
    delta = Math.Min(delta, 0.05);
    if (_game.AcceptsInput)
    {
      var direction = (Input.IsActionPressed("move_right") ? 1 : 0) - (Input.IsActionPressed("move_left") ? 1 : 0);
      if (direction != _heldDirection)
      {
        _heldDirection = direction;
        _repeatTimer = 0;
        _repeating = false;
        if (direction != 0) _game.Move(direction, 0);
      }
      else if (direction != 0)
      {
        _repeatTimer += delta;
        if (_repeatTimer >= (_repeating ? 0.075 : 0.18))
        {
          _game.Move(direction, 0);
          _repeatTimer = 0;
          _repeating = true;
        }
      }
    }
    else ResetRepeat();
    _game.Advance(delta, Input.IsActionPressed("soft_drop"));
    if (!_game.Paused)
    {
      _noticeTimer -= delta;
      if (_noticeTimer <= 0) _board.NoticeKey = "";
    }
    if (_game.Phase == GamePhase.Won && (_completedLevels & (1 << (_selectedLevel - 1))) == 0)
    {
      _completedLevels |= 1 << (_selectedLevel - 1);
      SaveProgress();
    }
    _hud.Refresh(_game, _best, _audio.Enabled, _selectedLevel, _completedLevels);
    _board.QueueRedraw();
  }

  public override void _UnhandledInput(InputEvent input)
  {
    if (input is not InputEventKey { Pressed: true, Echo: false }) return;
    if (input.IsActionPressed("pause")) TogglePause();
    else if (input.IsActionPressed("restart")) Start();
    else if (input.IsActionPressed("demo")) ShowHintOrDemo();
    else if (input.IsActionPressed("sound")) ToggleSound();
    else if (input.IsActionPressed("confirm"))
    {
      Confirm();
    }
    else if (input.IsActionPressed("rotate"))
    {
      if (_game.Rotate()) _audio.Tone(240, 0.04f);
    }
    else if (input.IsActionPressed("hard_drop")) _game.HardDrop();
    else return;
    GetViewport().SetInputAsHandled();
  }

  public override void _Notification(int what)
  {
    if (what == NotificationApplicationFocusOut && _game is not null)
    {
      _game.SetPaused(true);
      ResetRepeat();
    }
  }

  private void Start(bool demo = false)
  {
    if (demo) _selectedLevel = 0;
    _game = demo ? GameSession.CreateDemo() : _selectedLevel == 0 ? new GameSession() : new GameSession(PuzzleLevels.All[_selectedLevel - 1]);
    _board.Session = _game;
    ResetRepeat();
    _game.PieceLocked += () => _audio.Tone(160, 0.08f);
    _game.Matched += wave =>
    {
      ShowNotice("match", wave.Pieces.Count, wave.Points, wave.Chain);
      _audio.Tone(360 + wave.Chain * 140, 0.25f);
      if (_game.Puzzle is not null || _game.Score <= _best) return;
      _best = _game.Score;
      SaveProgress();
    };
    ShowNotice(_game.Puzzle is { IsTutorial: false } ? "challenge_intro" : _game.Puzzle is not null ? "puzzle_intro" : demo ? "demo_hint" : "intro");
  }

  private void Confirm()
  {
    if (_game.Phase == GamePhase.Won)
    {
      _completedLevels |= 1 << (_selectedLevel - 1);
      SaveProgress();
      _selectedLevel = PuzzleLevels.NextLevel(_selectedLevel);
      Start();
    }
    else if (_game.Phase == GamePhase.Over) Start();
    else if (_game.Paused) TogglePause();
  }

  private void ShowHintOrDemo()
  {
    if (_game.Puzzle is null) { Start(true); return; }
    _game.SetPaused(false);
    ResetRepeat();
    ShowNotice(_game.Puzzle.HintKey);
    _noticeTimer = 12;
  }

  private void ApplyLanguageFont()
  {
    var suffix = _texts.Language == "ja" ? "JP" : "SC";
    Theme = new Theme { DefaultFont = GD.Load<FontFile>($"res://Assets/Fonts/ChromaUI-{suffix}.otf") };
  }

  private void TogglePause()
  {
    _game.SetPaused(!_game.Paused);
    ResetRepeat();
  }

  private void ToggleSound()
  {
    _audio.Enabled = !_audio.Enabled;
    _audio.Tone(500);
    SaveProgress();
  }

  private void ShowNotice(string key, params object[] arguments)
  {
    _board.NoticeKey = key;
    _board.NoticeArguments = arguments;
    _noticeTimer = 2.5;
  }

  private void ResetRepeat()
  {
    _heldDirection = 0;
    _repeatTimer = 0;
    _repeating = false;
  }

  private void SaveProgress()
  {
    var save = new ConfigFile();
    save.SetValue("settings", "language", _texts.Language);
    save.SetValue("progress", "best_score", _best);
    save.SetValue("progress", "completed_levels", _completedLevels);
    save.SetValue("settings", "sound_enabled", _audio.Enabled);
    var result = save.Save(SavePath);
    if (result != Error.Ok) GD.PushWarning($"Could not save local progress: {result}");
  }

  private static void RegisterInput()
  {
    static void Bind(string action, params Key[] keys)
    {
      if (InputMap.HasAction(action)) return;
      InputMap.AddAction(action);
      foreach (var key in keys) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
    Bind("move_left", Key.Left, Key.A);
    Bind("move_right", Key.Right, Key.D);
    Bind("soft_drop", Key.Down, Key.S);
    Bind("rotate", Key.Up, Key.W);
    Bind("hard_drop", Key.Space);
    Bind("pause", Key.P, Key.Escape);
    Bind("restart", Key.R);
    Bind("demo", Key.F2);
    Bind("sound", Key.M);
    Bind("confirm", Key.Enter, Key.KpEnter);
  }

  private void ConfigureWindow()
  {
    if (DisplayServer.GetName() == "headless") return;
    var usable = DisplayServer.ScreenGetUsableRect();
    var density = DisplayServer.ScreenGetScale();
    var scale = Math.Min(density, Math.Min(usable.Size.X * 0.9f / 720, usable.Size.Y * 0.9f / 804));
    var window = GetWindow();
    window.MinSize = (Vector2I)(new Vector2(540, 603) * scale);
    window.Size = (Vector2I)(new Vector2(720, 804) * scale);
    window.Position = usable.Position + (usable.Size - window.Size) / 2;
  }
}
