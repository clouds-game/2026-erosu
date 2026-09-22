using ChromaDrop.Core;
using Godot;

namespace ChromaDrop.Game;

public partial class GameController : Control
{
  private GameSession _game = null!;
  private BoardView _board = null!;
  private GameHud _hud = null!;
  private GameAudio _audio = null!;
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
    var save = new ConfigFile();
    if (save.Load(SavePath) == Error.Ok)
    {
      _best = Math.Max(0, save.GetValue("progress", "best_score", 0).AsInt32());
      _audio.Enabled = save.GetValue("settings", "sound_enabled", false).AsBool();
    }
    _hud.PauseRequested += TogglePause;
    _hud.RestartRequested += () => Start();
    _hud.DemoRequested += () => Start(true);
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
      if (_noticeTimer <= 0) _board.Notice = "";
    }
    _hud.Refresh(_game, _best, _audio.Enabled);
    _board.QueueRedraw();
  }

  public override void _UnhandledInput(InputEvent input)
  {
    if (input is not InputEventKey { Pressed: true, Echo: false }) return;
    if (input.IsActionPressed("pause")) TogglePause();
    else if (input.IsActionPressed("restart")) Start();
    else if (input.IsActionPressed("demo")) Start(true);
    else if (input.IsActionPressed("sound")) ToggleSound();
    else if (input.IsActionPressed("confirm"))
    {
      if (_game.Phase == GamePhase.Over) Start();
      else if (_game.Paused) TogglePause();
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
    _game = demo ? GameSession.CreateDemo() : new GameSession();
    _board.Session = _game;
    ResetRepeat();
    _game.PieceLocked += () => _audio.Tone(160, 0.08f);
    _game.Matched += wave =>
    {
      ShowNotice($"{wave.Pieces.Count} blocks   +{wave.Points}   ×{wave.Chain}");
      _audio.Tone(360 + wave.Chain * 140, 0.25f);
      if (_game.Score <= _best) return;
      _best = _game.Score;
      SaveProgress();
    };
    ShowNotice(demo ? "Space: connect 3 whole blocks" : "Connect 3 same-color blocks, edge to edge");
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

  private void ShowNotice(string message)
  {
    _board.Notice = message;
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
    save.SetValue("progress", "best_score", _best);
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
