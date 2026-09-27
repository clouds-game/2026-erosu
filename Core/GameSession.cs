namespace ChromaDrop.Core;

public enum GamePhase { Falling, Clearing, Settling, Over, Won }
public sealed record ClearWave(IReadOnlyList<Piece> Pieces, int Chain, ScoreAward Award)
{
  public int Points => Award.Total;
}

public sealed class GameSession
{
  private readonly PieceBag? _bag;
  private readonly Queue<Piece> _next = new();
  private double _timer;
  private double _lockTimer;
  private int _chain;

  public Board Board { get; }
  public Piece? Active { get; private set; }
  public IReadOnlyList<Piece> Next => _next.Take(Puzzle is null ? 3 : _next.Count).ToArray();
  public PuzzleLevel? Puzzle { get; }
  public ColorProfile? Colors { get; }
  public bool AnchoredBlocks { get; }
  public int Remaining => _next.Count + (Active is null ? 0 : 1);
  public bool IsFinished => Phase is GamePhase.Over or GamePhase.Won;
  public GamePhase Phase { get; private set; } = GamePhase.Falling;
  public bool Paused { get; private set; }
  public ClearWave? Wave { get; private set; }
  public int Score { get; private set; }
  public int Cleared { get; private set; }
  public int BestChain { get; private set; }
  public int Locked { get; private set; }
  public int Level => Math.Min(11, 1 + Locked / 15);
  public double ClearProgress => Math.Clamp(_timer / 0.42, 0, 1);
  public bool AcceptsInput => !Paused && Phase == GamePhase.Falling;
  public event Action<ClearWave>? Matched;
  public event Action? PieceLocked;

  public GameSession(Random? random = null, Board? board = null, ColorProfile? colors = null, bool anchoredBlocks = false)
  {
    Board = board ?? new Board();
    Colors = colors ?? ColorProfile.Default;
    random ??= new Random();
    AnchoredBlocks = anchoredBlocks;
    if (anchoredBlocks) AnchoredObstacles.Populate(Board, random, Colors);
    _bag = new PieceBag(random, Colors);
    for (var i = 0; i < 3; i++) _next.Enqueue(_bag.Take());
    Spawn();
  }

  public GameSession(PuzzleLevel puzzle)
  {
    Puzzle = puzzle;
    Board = new Board();
    foreach (var piece in puzzle.InitialPieces) Board.Add(piece);
    foreach (var piece in puzzle.Sequence) _next.Enqueue(piece);
    Spawn();
  }

  // Copy only visible state: the preview branch has no bag or random source.
  private GameSession(GameSession source)
  {
    Board = new Board();
    foreach (var piece in source.Board.Pieces) Board.Add(piece);
    foreach (var piece in source.Next) _next.Enqueue(piece);
    Active = source.Active;
    Colors = source.Colors;
    Puzzle = source.Puzzle;
    AnchoredBlocks = source.AnchoredBlocks;
    Score = source.Score;
    Cleared = source.Cleared;
    BestChain = source.BestChain;
    Locked = source.Locked;
  }

  public GameSession? PreviewPlacement(int action)
  {
    if (Placement(action) is null) return null;
    var preview = new GameSession(this);
    preview.Place(action);
    for (var tick = 0; tick < 3600; tick++)
    {
      if (preview.Phase is not (GamePhase.Clearing or GamePhase.Settling)) return preview;
      preview.Advance(1.0 / 60);
    }
    throw new InvalidOperationException("Placement preview exceeded resolution budget.");
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
    if (!AcceptsInput || Active is null || Active.Shape is Shape.O or Shape.Single) return false;
    var rotated = Rotated(Active);
    if (rotated is null) return false;
    Active = rotated;
    return true;
  }

  private Piece? Rotated(Piece piece)
  {
    if (piece.Shape is Shape.O or Shape.Single) return null;
    var left = piece.Cells.Min(cell => cell.X);
    var top = piece.Cells.Min(cell => cell.Y);
    var height = piece.Cells.Max(cell => cell.Y) - top + 1;
    var rotated = piece with
    {
      Cells = piece.Cells.Select(cell =>
        new Cell(left + height - 1 - (cell.Y - top), top + cell.X - left)).ToArray()
    };
    Cell[] kicks = [new(0, 0), new(-1, 0), new(1, 0), new(-2, 0), new(2, 0), new(0, -1), new(0, -2)];
    foreach (var kick in kicks)
    {
      var candidate = rotated.Offset(kick.X, kick.Y);
      if (Board.CanPlace(candidate)) return candidate;
    }
    return null;
  }

  // Rotation first, then horizontal movement, then hard drop. No timing or RNG is consumed.
  public Piece? Placement(int action)
  {
    if (action is < 0 or >= 40 || !AcceptsInput || Active is null) return null;
    var candidate = Active;
    for (var turn = 0; turn < action / Board.Width; turn++)
    {
      candidate = Rotated(candidate);
      if (candidate is null) return null;
    }
    var target = action % Board.Width;
    while (candidate.Cells.Min(cell => cell.X) != target)
    {
      var moved = candidate.Offset(Math.Sign(target - candidate.Cells.Min(cell => cell.X)), 0);
      if (!Board.CanPlace(moved)) return null;
      candidate = moved;
    }
    while (Board.CanPlace(candidate.Offset(0, 1))) candidate = candidate.Offset(0, 1);
    return candidate;
  }

  public bool Place(int action)
  {
    var candidate = Placement(action);
    if (candidate is null) return false;
    Active = candidate;
    Lock();
    return true;
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
      else Spawn();
      return;
    }
    _chain++;
    var award = ScoreRules.Calculate(matches, _chain);
    Board.Remove(matches);
    Score += award.Total;
    Cleared += matches.Count;
    BestChain = Math.Max(BestChain, _chain);
    Wave = new ClearWave(matches, _chain, award);
    Phase = GamePhase.Clearing;
    _timer = 0;
    Matched?.Invoke(Wave);
  }
}
