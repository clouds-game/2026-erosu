using ChromaDrop.Core;
using ChromaDrop.Localization;
using Godot;

namespace ChromaDrop.Game;

public partial class GameController : Control
{
  private GameSession _game = null!;
  private BoardView _board = null!;
  private GameHud _hud = null!;
  private GameOverlay _modes = null!;
  private GameOverlay _pauseMenu = null!;
  private GameOverlay _result = null!;
  private GameAudio _audio = null!;
  private readonly UiText _texts = new();
  private GameSelection _selection = GameSelection.Puzzle(1);
  private readonly HashSet<int> _completedLevels = new();
  private readonly Dictionary<string, int> _records = new();
  private double _noticeTimer;
  private int _heldDirection;
  private double _repeatTimer;
  private bool _repeating;
  private const string SavePath = "user://progress.cfg";

  public override void _Ready()
  {
    ConfigureWindow();
    RegisterInput();
    TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
    _board = GetNode<BoardView>("Shell/Hud/BoardFrame/Board");
    _hud = GetNode<GameHud>("Shell/Hud");
    _modes = GetNode<GameOverlay>("ModeSelect");
    _pauseMenu = GetNode<GameOverlay>("PauseMenu");
    _result = GetNode<GameOverlay>("Result");
    _audio = new GameAudio();
    AddChild(_audio);
    _texts.SetLanguage(OS.GetLocale());
    var save = new ConfigFile();
    if (save.Load(SavePath) == Error.Ok)
    {
      _texts.SetLanguage(save.GetValue("settings", "language", _texts.Language).AsString());
      LoadProgress(save);
      _audio.Enabled = save.GetValue("settings", "sound_enabled", true).AsBool();
    }
    var captureLanguage = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--capture-lang="));
    if (captureLanguage is not null) _texts.SetLanguage(captureLanguage["--capture-lang=".Length..]);
    _selection = GameSelection.Puzzle(PuzzleLevels.All.Where(level => level.IsTutorial)
      .FirstOrDefault(level => !_completedLevels.Contains(level.Number))?.Number
      ?? PuzzleLevels.DefaultLevel);
    ApplyLanguageFont();
    _hud.Texts = _texts;
    foreach (var overlay in new[] { _modes, _pauseMenu, _result })
    {
      overlay.Texts = _texts;
      overlay.SelectionRequested += selection => { _selection = selection; Start(); };
      overlay.ModesRequested += OpenModes;
      overlay.CloseRequested += CloseOverlay;
      overlay.RestartRequested += () => Start();
      overlay.ContinueRequested += Confirm;
      overlay.SoundRequested += ToggleSound;
      overlay.LanguageRequested += ChangeLanguage;
      overlay.HintRequested += ShowHintOrDemo;
    }
    _hud.LevelsRequested += OpenModes;
    _hud.PauseRequested += TogglePause;
    var args = OS.GetCmdlineUserArgs();
    var levelArg = args.FirstOrDefault(arg => arg.StartsWith("--capture-level="));
    if (levelArg is not null && int.TryParse(levelArg["--capture-level=".Length..], out var captureLevel))
      _selection = captureLevel switch
      {
        -2 => GameSelection.Pollution,
        0 => GameSelection.Free,
        _ when PuzzleLevels.All.Any(level => level.Number == captureLevel) => GameSelection.Puzzle(captureLevel),
        _ => _selection
      };
    var modeArg = args.FirstOrDefault(arg => arg.StartsWith("--capture-mode="));
    if (modeArg is not null && GameSelection.TryParse(modeArg["--capture-mode=".Length..], out var captureSelection))
      _selection = captureSelection;
    Start(args.Contains("--demo"));
    if (args.Contains("--capture-modes")) OpenModes();
    if (args.Contains("--capture-pause")) OpenPause();
    if (args.Contains("--capture-result"))
    {
      _game.HardDrop();
      for (var i = 0; i < 120 && !_game.IsFinished; i++) _game.Advance(0.05);
      if (_game.IsFinished) OpenResult();
    }
    var captureArg = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--capture="));
    if (captureArg is not null) CaptureAfterDraw(captureArg["--capture=".Length..]);
  }

  private async void CaptureAfterDraw(string path)
  {
    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    var result = GetViewport().GetTexture().GetImage().SavePng(path);
    if (result != Error.Ok) GD.PushError($"Could not save screenshot: {result}");
    GetTree().Quit(result == Error.Ok ? 0 : 1);
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
      if (_noticeTimer <= 0) _hud.ClearFeedback();
    }
    if (_game.Phase == GamePhase.Won && _selection.PuzzleNumber is int completed && _completedLevels.Add(completed))
    {
      SaveProgress();
    }
    if (UpdateRecords()) SaveProgress();
    if (_game.IsFinished && !_result.Visible && !_modes.Visible) OpenResult();
    _hud.Refresh(_game, _records);
    _board.QueueRedraw();
  }

  public override void _UnhandledInput(InputEvent input)
  {
    if (input is not InputEventKey { Pressed: true, Echo: false }) return;
    if (_modes.Visible || _pauseMenu.Visible)
    {
      if (input.IsActionPressed("pause") || input.IsActionPressed("confirm")) CloseOverlay();
      GetViewport().SetInputAsHandled();
      return;
    }
    if (_result.Visible)
    {
      if (input.IsActionPressed("confirm")) Confirm();
      else if (input.IsActionPressed("restart")) Start();
      GetViewport().SetInputAsHandled();
      return;
    }
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
      if (_game.Rotate()) _audio.Select();
    }
    else if (input.IsActionPressed("hard_drop")) _game.HardDrop();
    else return;
    GetViewport().SetInputAsHandled();
  }

  public override void _Notification(int what)
  {
    if (what == NotificationApplicationFocusOut && _game is not null && !OS.GetCmdlineUserArgs().Any(arg => arg.StartsWith("--capture")))
    {
      if (!_game.IsFinished) OpenPause();
      ResetRepeat();
    }
  }

  private void Start(bool demo = false)
  {
    if (demo) _selection = GameSelection.Free;
    _game = demo ? GameSession.CreateDemo() : GameSessionFactory.Create(_selection, Random.Shared.Next());
    _board.Session = _game;
    _hud.ClearFeedback();
    _modes.Visible = _pauseMenu.Visible = _result.Visible = false;
    ResetRepeat();
    _game.PieceLocked += _audio.Lock;
    _game.Matched += wave =>
    {
      _hud.ShowFeedback("+" + UiText.Number(wave.Points) + "  ×" + wave.Chain);
      _noticeTimer = 2.5;
      _audio.Match(wave.Chain);
    };

  }

  private void Confirm()
  {
    if (_game.IsFinished && _game.ContinueAction == ContinueAction.NextPuzzle)
    {
      if (_selection.PuzzleNumber is not int levelNumber) return;
      _completedLevels.Add(levelNumber);
      SaveProgress();
      _selection = GameSelection.Puzzle(PuzzleLevels.NextLevel(levelNumber));
      Start();
    }
    else if (_game.IsFinished) Start();
    else if (_game.Paused) TogglePause();
  }

  private void ShowHintOrDemo()
  {
    switch (_game.Presentation.Help.Action)
    {
      case HelpAction.Demo:
        Start(true);
        break;
      case HelpAction.Rules:
        CloseOverlay();
        ShowNotice(_game.Presentation.Help.ContentKey);
        break;
      case HelpAction.PuzzleHint:
        CloseOverlay();
        ShowNotice(_game.Puzzle!.HintKey);
        _noticeTimer = 12;
        break;
    }
  }

  private void ApplyLanguageFont()
  {
    var theme = (Theme)GD.Load<Theme>("res://Themes/pixel_theme.tres").Duplicate();
    theme.DefaultFont = GD.Load<FontFile>(_texts.Language == "ja"
      ? "res://Assets/Fonts/Fusion/ja.ttf"
      : "res://Assets/Fonts/Fusion/zh_hans.ttf");
    Theme = theme;
  }

  private void OpenModes()
  {
    _game.SetPaused(true);
    _pauseMenu.Visible = _result.Visible = false;
    _modes.Render(_game, _selection, _completedLevels, _audio.Enabled);
    _modes.Visible = true;
    ResetRepeat();
  }

  private void OpenPause()
  {
    if (_game.IsFinished) return;
    _game.SetPaused(true);
    _modes.Visible = _result.Visible = false;
    _pauseMenu.Render(_game, _selection, _completedLevels, _audio.Enabled);
    _pauseMenu.Visible = true;
    ResetRepeat();
  }

  private void OpenResult()
  {
    _modes.Visible = _pauseMenu.Visible = false;
    _result.Render(_game, _selection, _completedLevels, _audio.Enabled);
    _result.Visible = true;
    ResetRepeat();
  }

  private void CloseOverlay()
  {
    _modes.Visible = _pauseMenu.Visible = false;
    if (_game.IsFinished) OpenResult();
    else _game.SetPaused(false);
    ResetRepeat();
  }

  private void RefreshOverlay()
  {
    if (_modes.Visible) _modes.Render(_game, _selection, _completedLevels, _audio.Enabled);
    if (_pauseMenu.Visible) _pauseMenu.Render(_game, _selection, _completedLevels, _audio.Enabled);
    if (_result.Visible) _result.Render(_game, _selection, _completedLevels, _audio.Enabled);
  }

  private void ChangeLanguage()
  {
    var index = Array.IndexOf(UiText.Languages, _texts.Language);
    _texts.SetLanguage(UiText.Languages[(index + 1) % UiText.Languages.Length]);
    ApplyLanguageFont();
    RefreshOverlay();
    SaveProgress();
  }

  private void TogglePause()
  {
    if (_pauseMenu.Visible) CloseOverlay();
    else OpenPause();
  }

  private void ToggleSound()
  {
    _audio.Enabled = !_audio.Enabled;
    _audio.Select();
    SaveProgress();
    RefreshOverlay();
  }

  private void ShowNotice(string key, params object[] arguments)
  {
    _hud.ShowFeedback(_texts.Get(key, arguments));
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
    foreach (var record in _records) save.SetValue("records", record.Key, record.Value);
    save.SetValue("progress", "completed_level_ids", string.Join(',', _completedLevels.Order()));
    var legacyMask = _completedLevels.Where(number => number is > 0 and <= 31)
      .Aggregate(0, (mask, number) => mask | (1 << (number - 1)));
    save.SetValue("progress", "completed_levels", legacyMask);
    save.SetValue("settings", "sound_enabled", _audio.Enabled);
    var result = save.Save(SavePath);
    if (result != Error.Ok) GD.PushWarning($"Could not save local progress: {result}");
  }

  private void LoadProgress(ConfigFile save)
  {
    var ids = save.GetValue("progress", "completed_level_ids", "").AsString();
    if (ids.Length > 0)
    {
      foreach (var value in ids.Split(',', StringSplitOptions.RemoveEmptyEntries))
        if (int.TryParse(value, out var number) && PuzzleLevels.All.Any(level => level.Number == number))
          _completedLevels.Add(number);
    }
    else
    {
      var legacyMask = save.GetValue("progress", "completed_levels", 0).AsInt32();
      foreach (var level in PuzzleLevels.All)
        if (level.Number <= 31 && (legacyMask & (1 << (level.Number - 1))) != 0)
          _completedLevels.Add(level.Number);
    }

    foreach (var mode in ModeCatalog.Standalone)
      foreach (var metric in mode.Metrics.Where(metric => metric.RecordPolicy == RecordPolicy.Max))
      {
        var key = ModeCatalog.RecordKey(mode.Id, metric.Key);
        var legacyKey = (mode.Id, metric.Key) switch
        {
          (ModeId.Free, "score") => "best_score",
          (ModeId.Pollution, "score") => "pollution_best_score",
          (ModeId.Pollution, "purified") => "pollution_best_cleared",
          _ => ""
        };
        var value = save.HasSectionKey("records", key)
          ? save.GetValue("records", key, 0).AsInt32()
          : legacyKey.Length > 0 ? save.GetValue("progress", legacyKey, 0).AsInt32() : 0;
        _records[key] = Math.Max(0, value);
      }
  }

  private bool UpdateRecords()
  {
    var changed = false;
    foreach (var metric in _game.Metrics.Where(metric => metric.Definition.RecordPolicy == RecordPolicy.Max))
    {
      var key = ModeCatalog.RecordKey(_game.Mode, metric.Key);
      if (metric.Value <= _records.GetValueOrDefault(key)) continue;
      _records[key] = metric.Value;
      changed = true;
    }
    return changed;
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
    var scale = Math.Min(density, Math.Min(usable.Size.X * 0.9f / 1040, usable.Size.Y * 0.9f / 800));
    var window = GetWindow();
    window.MinSize = (Vector2I)(new Vector2(780, 600) * scale);
    window.Size = (Vector2I)(new Vector2(1040, 800) * scale);
    if (OS.GetCmdlineUserArgs().Contains("--capture-min")) window.Size = window.MinSize;
    window.Position = usable.Position + (usable.Size - window.Size) / 2;
  }
}
