namespace ChromaDrop.Core;

[Obsolete("Use ModeId and GameSessionFactory for new code.")]
public enum SessionMode { Free, Pollution, Puzzle }
public enum GamePhase { Falling = 0, Clearing = 1, Settling = 2, Rising = 3, Over = 4, Won = 5, Expiring = 6 }
public sealed record ClearWave(IReadOnlyList<Piece> Pieces, int Chain, ScoreAward Award)
{
  public int Points => Award.Total;
}

#pragma warning disable CS0618 // Compatibility constructors intentionally reference the legacy enum.
public sealed class GameSession
{
  private sealed record LegacySetup(IGameModeRules Rules, Board Board, IPieceSource Bag);
  private readonly IGameModeRules _rules;
  private readonly IPieceSource? _bag;
  private readonly Queue<Piece> _next = new();
  private double _timer;
  private double _lockTimer;
  private int _chain;

  internal GameSession(IGameModeRules rules, Board board, IPieceSource? bag, IEnumerable<Piece>? sequence)
  {
	_rules = rules;
	_bag = bag;
	Board = board;
	if (sequence is not null)
	  foreach (var piece in sequence) _next.Enqueue(piece);
	if (_bag is not null)
	  for (var i = 0; i < 3; i++) _next.Enqueue(_bag.Take());
	Spawn();
  }

  [Obsolete("Use GameSessionFactory.Create(GameSelection, seed, board) for deterministic mode sessions.")]
  public GameSession(Random? random = null, Board? board = null)
	: this(CreateLegacySetup(SessionMode.Free, random, board)) { }

  [Obsolete("Use GameSessionFactory.Create(GameSelection, seed, board) for deterministic mode sessions.")]
  public GameSession(SessionMode mode, Random? random = null, Board? board = null)
	: this(CreateLegacySetup(mode, random, board)) { }

  [Obsolete("Use GameSessionFactory.Create(PuzzleLevel) for puzzle sessions.")]
  public GameSession(PuzzleLevel puzzle)
	: this(new PuzzleModeRules(puzzle), CreatePuzzleBoard(puzzle), null, puzzle.Sequence) { }

  private GameSession(LegacySetup setup) : this(setup.Rules, setup.Board, setup.Bag, null) { }

  private static LegacySetup CreateLegacySetup(SessionMode mode, Random? random, Board? board)
  {
	if (mode == SessionMode.Puzzle) throw new ArgumentException("Use the puzzle constructor for puzzle sessions.");
	var source = random ?? new Random();
	IGameModeRules rules = mode == SessionMode.Pollution
	  ? new PollutionModeRules(source.Next())
	  : new FreeModeRules();
	return new LegacySetup(rules, board ?? new Board(), new PieceBag(source));
  }

  private static Board CreatePuzzleBoard(PuzzleLevel puzzle)
  {
	var board = new Board();
	foreach (var piece in puzzle.InitialPieces) board.Add(piece);
	return board;
  }

  public Board Board { get; }
  public ModeId Mode => _rules.Id;
  public ModeDefinition ModeDefinition => _rules.Definition;
  public ModePresentation Presentation => _rules.Presentation;
  public ModeCapabilities Capabilities => _rules.Definition.Capabilities;
  public Piece? Active { get; private set; }
  public IReadOnlyList<Piece> Next => _next.Take(Math.Min(_rules.PreviewCount, _next.Count)).ToArray();
  public PuzzleLevel? Puzzle => _rules.Puzzle;
  public int Remaining => _next.Count + (Active is null ? 0 : 1);
  public bool IsFinished => Phase is GamePhase.Over or GamePhase.Won;
  public GamePhase Phase { get; private set; } = GamePhase.Falling;
  public bool Paused { get; private set; }
  public ClearWave? Wave { get; private set; }
  public ModeTransition? Transition { get; private set; }
  public int Score { get; private set; }
  public int Cleared { get; private set; }
  public int BestChain { get; private set; }
  public int Locked { get; private set; }
  public int Level => Math.Min(11, 1 + Locked / 15);
  public double ClearProgress => Math.Clamp(_timer / 0.42, 0, 1);
  public double TransitionProgress => Phase switch
  {
	GamePhase.Rising => Math.Clamp(_timer / 0.22, 0, 1),
	GamePhase.Expiring => Math.Clamp(_timer / 0.28, 0, 1),
	_ => 1
  };
  public bool AcceptsInput => !Paused && Phase == GamePhase.Falling;
  public IReadOnlyList<RunMetric> Metrics => _rules.GetMetrics(this);
  public string ResultTitleKey => _rules.ResultTitleKey(this);
  public string ContinueKey => _rules.ContinueKey(this);
  public ContinueAction ContinueAction => _rules.GetContinueAction(this);
  public event Action<ClearWave>? Matched;
  public event Action? PieceLocked;
  public event Action<ModeTransition>? TransitionStarted;

  public RunMetric? Metric(string key) => Metrics.FirstOrDefault(metric => metric.Key == key);
  public int? TurnsRemaining(Piece piece) => Active?.Id != piece.Id
	&& piece is { Kind: PieceKind.Black, ExpiresAtLock: int expires }
	? Math.Clamp(expires - Locked, 0, BlackWhiteModeRules.BlackLifetime)
	: null;

  public static GameSession CreateDemo()
  {
	var game = GameSessionFactory.Create(GameSelection.Free);
	game.Board.Add(Piece.Create(-1, Shape.O, 0, 0, 16));
	game.Board.Add(Piece.Create(-2, Shape.O, 0, 2, 16));
	game.Active = Piece.Create(-3, Shape.O, 0, 4, 0);
	return game;
  }

  public void SetPaused(bool paused)
  {
	if (!IsFinished) Paused = paused;
  }

  public bool Move(int dx, int dy)
  {
	if (!AcceptsInput || Active is null) return false;
	var candidate = Active.Offset(dx, dy);
	if (!Board.CanPlace(candidate)) return false;
	Active = candidate;
	return true;
  }

  public bool Rotate()
  {
	if (!AcceptsInput || Active is null || Active.Shape == Shape.O) return false;
	var left = Active.Cells.Min(cell => cell.X);
	var top = Active.Cells.Min(cell => cell.Y);
	var height = Active.Cells.Max(cell => cell.Y) - top + 1;
	var rotated = Active with
	{
	  Cells = Active.Cells.Select(cell =>
		new Cell(left + height - 1 - (cell.Y - top), top + cell.X - left)).ToArray()
	};
	Cell[] kicks = [new(0, 0), new(-1, 0), new(1, 0), new(-2, 0), new(2, 0), new(0, -1), new(0, -2)];
	foreach (var kick in kicks)
	{
	  var candidate = rotated.Offset(kick.X, kick.Y);
	  if (!Board.CanPlace(candidate)) continue;
	  Active = candidate;
	  return true;
	}
	return false;
  }

  public Piece? Ghost()
  {
	if (Active is null) return null;
	var ghost = Active;
	while (Board.CanPlace(ghost.Offset(0, 1))) ghost = ghost.Offset(0, 1);
	return ghost;
  }

  public void HardDrop()
  {
	if (!AcceptsInput || Active is null) return;
	Active = Ghost();
	Lock();
  }

  public void Advance(double delta, bool softDrop = false)
  {
	if (Paused || IsFinished || delta <= 0) return;
	_timer += delta;
	switch (Phase)
	{
	  case GamePhase.Falling:
		if (!_rules.UsesAutomaticFall)
		{
		  if (softDrop && _timer >= 0.045) { Move(0, 1); _timer = 0; }
		  if (!softDrop) _timer = 0;
		  break;
		}
		var interval = softDrop ? 0.045 : Math.Max(0.18, 0.85 - (Locked / 15) * 0.07);
		if (_timer >= interval)
		{
		  Move(0, 1);
		  _timer = 0;
		}
		if (Active is not null && !Board.CanPlace(Active.Offset(0, 1)))
		{
		  _lockTimer += delta;
		  if (_lockTimer >= 0.38) Lock();
		}
		else _lockTimer = 0;
		break;
	  case GamePhase.Clearing:
		if (_timer >= 0.42)
		{
		  Phase = GamePhase.Settling;
		  _timer = 0;
		  Wave = null;
		}
		break;
	  case GamePhase.Settling:
		if (_timer >= 0.06)
		{
		  _timer = 0;
		  if (!Board.StepGravity()) CheckMatches();
		}
		break;
	  case GamePhase.Rising:
		if (_timer >= 0.22)
		{
		  _timer = 0;
		  _chain = 0;
		  Transition = null;
		  CheckMatches();
		}
		break;
	  case GamePhase.Expiring:
		if (_timer >= 0.28)
		{
		  _timer = 0;
		  Transition = null;
		  Phase = GamePhase.Settling;
		}
		break;
	}
  }

  private void Spawn()
  {
	if (_next.Count == 0)
	{
	  Active = null;
	  Phase = GamePhase.Over;
	  return;
	}
	var piece = _rules.ActivatePiece(this, _next.Dequeue());
	if (_bag is not null) _next.Enqueue(_bag.Take());
	var width = piece.Cells.Max(cell => cell.X) + 1;
	Active = piece.Offset((Board.Width - width) / 2, 0);
	Phase = GamePhase.Falling;
	_timer = 0;
	_lockTimer = 0;
	if (!Board.CanPlace(Active))
	{
	  Active = null;
	  Phase = GamePhase.Over;
	}
  }

  private void Lock()
  {
	Board.Add(Active!);
	Active = null;
	Locked++;
	_chain = 0;
	PieceLocked?.Invoke();
	CheckMatches();
  }

  private void CheckMatches()
  {
	var matches = _rules.FindMatches(Board);
	if (matches.Count == 0)
	{
	  if (_rules.IsComplete(this))
	  {
		Phase = GamePhase.Won;
		return;
	  }
	  var transition = _rules.AfterBoardSettled(this);
	  if (transition is not null)
	  {
		Transition = transition;
		if (transition.EndsGame)
		{
		  Phase = GamePhase.Over;
		  Active = null;
		  return;
		}
		Phase = transition.Kind == ModeTransitionKind.Rising ? GamePhase.Rising : GamePhase.Expiring;
		_timer = 0;
		TransitionStarted?.Invoke(transition);
		return;
	  }
	  Spawn();
	  return;
	}
	_chain++;
	var award = ScoreRules.Calculate(matches, _chain);
	Board.Remove(matches);
	Score += award.Total;
	Cleared += matches.Count;
	_rules.OnPiecesCleared(matches);
	BestChain = Math.Max(BestChain, _chain);
	Wave = new ClearWave(matches, _chain, award);
	Phase = GamePhase.Clearing;
	_timer = 0;
	Matched?.Invoke(Wave);
  }
}
#pragma warning restore CS0618
