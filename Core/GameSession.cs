namespace ChromaDrop.Core;

public enum SessionMode { Free, Pollution, Puzzle }
public enum GamePhase { Falling, Clearing, Settling, Rising, Over, Won }
public sealed record ClearWave(IReadOnlyList<Piece> Pieces, int Chain, ScoreAward Award)
{
  public int Points => Award.Total;
}

public sealed class GameSession
{
  private readonly PieceBag? _bag;
  private readonly PieceBag? _pollutionBag;
  private readonly Random? _pollutionRandom;
  private readonly Queue<Piece> _next = new();
  private double _timer;
  private double _lockTimer;
  private int _chain;
  private int _lastPollutionRise;

  public const int PollutionInterval = 6;
  public const int PollutionRiseRows = 2;

  public Board Board { get; }
  public SessionMode Mode { get; }
  public Piece? Active { get; private set; }
  public IReadOnlyList<Piece> Next => _next.Take(Puzzle is null ? 3 : _next.Count).ToArray();
  public PuzzleLevel? Puzzle { get; }
  public int Remaining => _next.Count + (Active is null ? 0 : 1);
  public bool IsFinished => Phase is GamePhase.Over or GamePhase.Won;
  public GamePhase Phase { get; private set; } = GamePhase.Falling;
  public bool Paused { get; private set; }
  public ClearWave? Wave { get; private set; }
  public int Score { get; private set; }
  public int Cleared { get; private set; }
  public int BestChain { get; private set; }
  public int Locked { get; private set; }
  public int PollutionCleared { get; private set; }
  public int PollutionCountdown => Mode == SessionMode.Pollution
	? PollutionInterval - (Locked - _lastPollutionRise)
	: 0;
  public int Level => Math.Min(11, 1 + Locked / 15);
  public double ClearProgress => Math.Clamp(_timer / 0.42, 0, 1);
  public double RiseProgress => Phase == GamePhase.Rising ? Math.Clamp(_timer / 0.22, 0, 1) : 1;
  public bool AcceptsInput => !Paused && Phase == GamePhase.Falling;
  public event Action<ClearWave>? Matched;
  public event Action? PieceLocked;
  public event Action? PollutionRose;

  public GameSession(Random? random = null, Board? board = null)
	: this(SessionMode.Free, random, board) { }

  public GameSession(SessionMode mode, Random? random = null, Board? board = null)
  {
	if (mode == SessionMode.Puzzle) throw new ArgumentException("Use the puzzle constructor for puzzle sessions.");
	Mode = mode;
	Board = board ?? new Board();
	random ??= new Random();
	_bag = new PieceBag(random);
	if (mode == SessionMode.Pollution)
	{
	  _pollutionRandom = new Random(random.Next());
	  _pollutionBag = new PieceBag(_pollutionRandom, -1, pollution: true);
	}
	for (var i = 0; i < 3; i++) _next.Enqueue(_bag.Take());
	Spawn();
  }

  public GameSession(PuzzleLevel puzzle)
  {
	Mode = SessionMode.Puzzle;
	Puzzle = puzzle;
	Board = new Board();
	foreach (var piece in puzzle.InitialPieces) Board.Add(piece);
	foreach (var piece in puzzle.Sequence) _next.Enqueue(piece);
	Spawn();
  }

  public static GameSession CreateDemo()
  {
	var game = new GameSession();
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
	  case GamePhase.Rising:
		if (_timer >= 0.22)
		{
		  _timer = 0;
		  _chain = 0;
		  CheckMatches();
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
	  else if (Mode == SessionMode.Pollution && Locked > 0 && Locked - _lastPollutionRise >= PollutionInterval)
		StartPollutionRise();
	  else Spawn();
	  return;
	}
	_chain++;
	var award = ScoreRules.Calculate(matches, _chain);
	Board.Remove(matches);
	Score += award.Total;
	Cleared += matches.Count;
	PollutionCleared += matches.Count(piece => piece.IsPollution);
	BestChain = Math.Max(BestChain, _chain);
	Wave = new ClearWave(matches, _chain, award);
	Phase = GamePhase.Clearing;
	_timer = 0;
	Matched?.Invoke(Wave);
  }

  private void StartPollutionRise()
  {
	_lastPollutionRise = Locked;
	if (!Board.TryRaise(PollutionRiseRows))
	{
	  Phase = GamePhase.Over;
	  Active = null;
	  return;
	}

	var first = _pollutionBag!.Take();
	var second = _pollutionBag.Take();
	var placements = PollutionPlacements(first, second).ToArray();
	_pollutionRandom!.Shuffle(placements);
	var chosen = placements.FirstOrDefault(pair => !CreatesMatch(pair.First, pair.Second));
	if (chosen.First is null) chosen = placements[0];
	Board.Add(chosen.First);
	Board.Add(chosen.Second);
	Phase = GamePhase.Rising;
	_timer = 0;
	PollutionRose?.Invoke();
  }

  private static IEnumerable<(Piece First, Piece Second)> PollutionPlacements(Piece first, Piece second)
  {
	foreach (var placement in PlacePair(first, second)) yield return placement;
	if (first.Color != second.Color)
	  foreach (var placement in PlacePair(first with { Color = second.Color }, second with { Color = first.Color }))
		yield return placement;
  }

  private static IEnumerable<(Piece First, Piece Second)> PlacePair(Piece first, Piece second)
  {
	var firstWidth = first.Cells.Max(cell => cell.X) + 1;
	var secondWidth = second.Cells.Max(cell => cell.X) + 1;
	var firstY = Board.Height - 1 - first.Cells.Max(cell => cell.Y);
	var secondY = Board.Height - 1 - second.Cells.Max(cell => cell.Y);
	for (var firstX = 0; firstX <= Board.Width - firstWidth; firstX++)
	  for (var secondX = 0; secondX <= Board.Width - secondWidth; secondX++)
	  {
		var separate = firstX + firstWidth <= secondX || secondX + secondWidth <= firstX;
		if (separate) yield return (first.Offset(firstX, firstY), second.Offset(secondX, secondY));
	  }
  }

  private bool CreatesMatch(Piece first, Piece second)
  {
	var trial = new Board();
	foreach (var piece in Board.Pieces) trial.Add(piece);
	trial.Add(first);
	trial.Add(second);
	return trial.FindMatches().Count > 0;
  }
}
