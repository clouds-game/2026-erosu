namespace ChromaDrop.Core;

public enum GameMode { FreePlay, Puzzle, Contamination }
public enum GamePhase { Falling, Clearing, Settling, Over, Won }
public sealed record ClearWave(
  IReadOnlyList<Piece> Pieces,
  int Chain,
  ScoreAward Award,
  IReadOnlySet<int> PollutedPieceIds)
{
  public int Points => Award.Total;
  public int PollutionCleared => PollutedPieceIds.Count;
}

internal readonly record struct SessionRandomStreams(Random Pieces, Random Mode)
{
  public static SessionRandomStreams Split(Random source) =>
	new(new Random(source.Next()), new Random(source.Next()));
}

public sealed class GameSession
{
  private readonly PieceBag? _bag;
  private readonly ContaminationGenerator? _contamination;
  private readonly Queue<Piece> _next = new();
  private readonly HashSet<int> _polluted = [];
  private double _timer;
  private double _lockTimer;
  private int _chain;
  private int _locksSinceRise;
  private bool _risePending;

  public Board Board { get; }
  public Piece? Active { get; private set; }
  public IReadOnlyList<Piece> Next => _next.Take(Mode == GameMode.Puzzle ? _next.Count : 3).ToArray();
  public PuzzleLevel? Puzzle { get; }
  public GameMode Mode { get; }
  public int Remaining => _next.Count + (Active is null ? 0 : 1);
  public bool IsFinished => Phase is GamePhase.Over or GamePhase.Won;
  public GamePhase Phase { get; private set; } = GamePhase.Falling;
  public bool Paused { get; private set; }
  public ClearWave? Wave { get; private set; }
  public int Score { get; private set; }
  public int Cleared { get; private set; }
  public int BestChain { get; private set; }
  public int Locked { get; private set; }
  public int PollutionRemaining => _polluted.Count;
  public int PollutionCleared { get; private set; }
  public int ContaminationWave { get; private set; }
  public int LocksUntilRise => Mode == GameMode.Contamination
	? Math.Max(0, ContaminationRules.RiseInterval(ContaminationWave) - _locksSinceRise)
	: 0;
  public int Level => Math.Min(11, 1 + Locked / 15);
  public double ClearProgress => Math.Clamp(_timer / 0.42, 0, 1);
  public bool AcceptsInput => !Paused && Phase == GamePhase.Falling;
  public event Action<ClearWave>? Matched;
  public event Action? PieceLocked;
  public event Action? ContaminationRose;

  public GameSession(Random? random = null, Board? board = null)
  {
	Mode = GameMode.FreePlay;
	Board = board ?? new Board();
	_bag = new PieceBag(random ?? new Random());
	for (var i = 0; i < 3; i++) _next.Enqueue(_bag.Take());
	Spawn();
  }

  public GameSession(PuzzleLevel puzzle)
  {
	Mode = GameMode.Puzzle;
	Puzzle = puzzle;
	Board = new Board();
	foreach (var piece in puzzle.InitialPieces) Board.Add(piece);
	foreach (var piece in puzzle.Sequence) _next.Enqueue(piece);
	Spawn();
  }

  private GameSession(PieceBag bag, ContaminationGenerator contamination)
  {
	Mode = GameMode.Contamination;
	Board = new Board();
	_bag = bag;
	_contamination = contamination;
	for (var i = 0; i < 3; i++) _next.Enqueue(_bag.Take());
	AddContaminationBand(initial: true);
	Spawn();
  }

  public static GameSession Create(GameSelection selection, Random? random = null) => selection.Mode switch
  {
	GameMode.FreePlay => new GameSession(random),
	GameMode.Puzzle => new GameSession(PuzzleLevels.All.Single(level => level.Number == selection.PuzzleNumber)),
	GameMode.Contamination => CreateContamination(random),
	_ => throw new ArgumentOutOfRangeException(nameof(selection))
  };

  public static GameSession CreateContamination(Random? random = null)
  {
	var streams = SessionRandomStreams.Split(random ?? new Random());
	return new GameSession(
	  new PieceBag(streams.Pieces, balancedColors: true),
	  new ContaminationGenerator(streams.Mode));
  }

  public static GameSession CreateDemo()
  {
	var game = new GameSession();
	game.Board.Add(Piece.Create(-1, Shape.O, 0, 0, 16));
	game.Board.Add(Piece.Create(-2, Shape.O, 0, 2, 16));
	game.Active = Piece.Create(-3, Shape.O, 0, 4, 0);
	return game;
  }

  public bool IsPolluted(int pieceId) => _polluted.Contains(pieceId) ||
	(Wave?.PollutedPieceIds.Contains(pieceId) ?? false);

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
		// Puzzles advance only through player input; touching the floor never
		// consumes a piece until the player commits it with hard drop.
		if (Puzzle is not null)
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
	var piece = _next.Dequeue();
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
	if (Mode == GameMode.Contamination)
	{
	  _locksSinceRise++;
	  if (_locksSinceRise >= ContaminationRules.RiseInterval(ContaminationWave)) _risePending = true;
	}
	_chain = 0;
	PieceLocked?.Invoke();
	CheckMatches();
  }

  private void CheckMatches()
  {
	var matches = Board.FindMatches();
	if (matches.Count == 0)
	{
	  // Judge only after all waves and gravity have settled, including the
	  // final supplied piece. An exhausted queue must not hide a victory.
	  if (Puzzle?.IsComplete(this) == true) Phase = GamePhase.Won;
	  else if (Mode == GameMode.Contamination && _risePending) AddContaminationBand(initial: false);
	  else Spawn();
	  return;
	}
	_chain++;
	var award = ScoreRules.Calculate(matches, _chain);
	var polluted = matches.Select(piece => piece.Id).Where(_polluted.Contains).ToHashSet();
	foreach (var id in polluted) _polluted.Remove(id);
	Board.Remove(matches);
	Score += award.Total;
	Cleared += matches.Count;
	PollutionCleared += polluted.Count;
	BestChain = Math.Max(BestChain, _chain);
	Wave = new ClearWave(matches, _chain, award, polluted);
	Phase = GamePhase.Clearing;
	_timer = 0;
	Matched?.Invoke(Wave);
  }

  private void AddContaminationBand(bool initial)
  {
	var nextWave = ContaminationWave + 1;
	var incoming = _contamination!.CreateBand(nextWave);
	if (Board.TryRaiseAndAdd(ContaminationRules.BandHeight, incoming) == RaiseResult.Overflow)
	{
	  Active = null;
	  _risePending = false;
	  Phase = GamePhase.Over;
	  return;
	}
	foreach (var piece in incoming) _polluted.Add(piece.Id);
	ContaminationWave = nextWave;
	_locksSinceRise = 0;
	_risePending = false;
	_chain = 0;
	if (initial) return;
	Phase = GamePhase.Settling;
	_timer = 0;
	ContaminationRose?.Invoke();
  }
}
