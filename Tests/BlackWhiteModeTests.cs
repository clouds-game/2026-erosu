using ChromaDrop.Core;

internal static class BlackWhiteModeTests
{
  public static IEnumerable<(string Name, Action Run)> Cases => new (string, Action)[]
  {
    ("Black-white candidate bags contain one concealed special in every five pieces", CandidateBag),
    ("Black-white previews conceal the reveal without changing the shape stream", ConcealedPreview),
    ("White pieces match every color and overlapping groups clear once", WildcardMatching),
    ("Black pieces never match and expire after three later locks", BlackLifetime),
    ("Matches caused by black expiration continue the current chain", ExpirationChain)
  };

  private static void CandidateBag()
  {
    var first = Source(77);
    var second = Source(77);
    var firstPieces = Enumerable.Range(0, 25).Select(_ => first.Take()).ToArray();
    var secondPieces = Enumerable.Range(0, 25).Select(_ => second.Take()).ToArray();
    for (var offset = 0; offset < firstPieces.Length; offset += BlackWhitePieceSource.BagSize)
      Assert(firstPieces.Skip(offset).Take(BlackWhitePieceSource.BagSize)
        .Count(piece => piece.Kind == PieceKind.SpecialUnknown) == 1);
    Assert(firstPieces.Select(Signature).SequenceEqual(secondPieces.Select(Signature)));
  }

  private static void ConcealedPreview()
  {
    var sawBlack = false;
    var sawWhite = false;
    var verifiedLockedBlack = false;
    for (var seed = 0; seed < 200 && (!sawBlack || !sawWhite || !verifiedLockedBlack); seed++)
    {
      var free = GameSessionFactory.Create(GameSelection.Free, seed);
      var special = GameSessionFactory.Create(GameSelection.BlackWhite, seed);
      Assert(free.Active!.Shape == special.Active!.Shape);
      Assert(free.Next.Select(piece => piece.Shape).SequenceEqual(special.Next.Select(piece => piece.Shape)));
      Assert(special.Active.Kind != PieceKind.SpecialUnknown);
      Assert(special.Next.All(piece => piece.Kind is PieceKind.Normal or PieceKind.SpecialUnknown));
      sawBlack |= special.Active.Kind == PieceKind.Black;
      sawWhite |= special.Active.Kind == PieceKind.White;
      if (special.Active.Kind == PieceKind.Black && !verifiedLockedBlack)
      {
        var id = special.Active.Id;
        Assert(special.TurnsRemaining(special.Active) is null);
        special.HardDrop();
        AdvanceUntilStable(special);
        var locked = special.Board.Pieces.Single(piece => piece.Id == id);
        Assert(special.TurnsRemaining(locked) == 3);
        verifiedLockedBlack = true;
      }
    }
    Assert(sawBlack && sawWhite && verifiedLockedBlack);
  }

  private static void WildcardMatching()
  {
    var rules = new BlackWhiteModeRules(1);
    Assert(rules.FindMatches(BoardOf(
      Normal(1, 0, 0), Normal(2, 0, 2), Special(3, PieceKind.White, 4))).Count == 3);
    Assert(rules.FindMatches(BoardOf(
      Normal(1, 0, 0), Special(2, PieceKind.White, 2), Special(3, PieceKind.White, 4))).Count == 3);
    Assert(rules.FindMatches(BoardOf(
      Special(1, PieceKind.White, 0), Special(2, PieceKind.White, 2), Special(3, PieceKind.White, 4))).Count == 3);

    var overlapping = rules.FindMatches(BoardOf(
      Normal(1, 0, 0), Normal(2, 0, 2), Special(3, PieceKind.White, 4),
      Normal(4, 1, 6), Normal(5, 1, 8)));
    Assert(overlapping.Count == 5 && overlapping.Select(piece => piece.Id).Distinct().Count() == 5);

    Assert(rules.FindMatches(BoardOf(
      Special(1, PieceKind.Black, 0), Special(2, PieceKind.Black, 2), Special(3, PieceKind.Black, 4))).Count == 0);
    Assert(rules.FindMatches(BoardOf(
      Normal(1, 0, 0, 16), Normal(2, 0, 2, 14), Special(3, PieceKind.White, 4, 12))).Count == 0);
  }

  private static void BlackLifetime()
  {
    GameSession? game = null;
    for (var seed = 0; seed < 200; seed++)
    {
      var board = BoardOf(
        Special(-10, PieceKind.Black, 0, 16) with { ExpiresAtLock = 3 },
        Special(-11, PieceKind.Black, 2, 16) with { ExpiresAtLock = 3 });
      var candidate = GameSessionFactory.Create(GameSelection.BlackWhite, seed, board);
      var valid = true;
      for (var turn = 0; turn < 3; turn++)
      {
        candidate.HardDrop();
        AdvanceUntilStable(candidate);
        if (candidate.IsFinished || candidate.Score != 0) { valid = false; break; }
        if (turn < 2 && candidate.Phase != GamePhase.Falling) { valid = false; break; }
      }
      if (valid && candidate.Phase == GamePhase.Expiring) { game = candidate; break; }
    }
    Assert(game is not null);
    Assert(game!.Board.Pieces.All(piece => piece.Id is not -10 and not -11));
    Assert(game.Transition?.RemovedPieces.Count == 2);
    Assert(game.Score == 0 && game.Cleared == 0 && !game.AcceptsInput);
    game.SetPaused(true);
    game.Advance(2);
    Assert(game.Phase == GamePhase.Expiring && game.TransitionProgress == 0);
    game.SetPaused(false);
    AdvanceUntil(game, GamePhase.Falling);
  }

  private static void ExpirationChain()
  {
    GameSession? game = null;
    for (var seed = 0; seed < 1000; seed++)
    {
      var board = BoardOf(
        Normal(-1, 0, 0, 16), Normal(-2, 0, 2, 16),
        Normal(-3, 1, 6, 16), Normal(-4, 1, 8, 16),
        Special(-5, PieceKind.Black, 6, 14) with { ExpiresAtLock = 1 },
        Normal(-6, 1, 6, 12));
      var candidate = GameSessionFactory.Create(GameSelection.BlackWhite, seed, board);
      if (candidate.Active is { Kind: PieceKind.Normal, Shape: Shape.O, Color: 0 }) { game = candidate; break; }
    }
    Assert(game is not null);
    var chains = new List<int>();
    game!.Matched += wave => chains.Add(wave.Chain);
    game.HardDrop();
    for (var tick = 0; tick < 1000 && chains.Count < 2; tick++) game.Advance(0.05);
    Assert(chains.SequenceEqual(new[] { 1, 2 }));
    Assert(game.BestChain == 2 && game.Cleared == 6);
  }

  private static BlackWhitePieceSource Source(int seed) => new(
    new PieceBag(SeedStreams.Create(seed, "player.pieces")),
    SeedStreams.Create(seed, "black_white.candidates"));

  private static Piece Normal(int id, int color, int x, int y = 16) => Piece.Create(id, Shape.O, color, x, y);
  private static Piece Special(int id, PieceKind kind, int x, int y = 16) =>
    Piece.Create(id, Shape.O, -1, x, y) with { Kind = kind };

  private static Board BoardOf(params Piece[] pieces)
  {
    var board = new Board();
    foreach (var piece in pieces) board.Add(piece);
    return board;
  }

  private static void AdvanceUntilStable(GameSession game)
  {
    for (var tick = 0; tick < 500 && game.Phase is GamePhase.Clearing or GamePhase.Settling; tick++)
      game.Advance(0.05);
  }

  private static void AdvanceUntil(GameSession game, GamePhase phase)
  {
    for (var tick = 0; tick < 1000 && game.Phase != phase && !game.IsFinished; tick++) game.Advance(0.05);
    Assert(game.Phase == phase);
  }

  private static string Signature(Piece piece) => $"{piece.Id}:{piece.Shape}:{piece.Color}:{piece.Kind}";
  private static void Assert(bool condition)
  {
    if (!condition) throw new InvalidOperationException("Black-white mode contract failed.");
  }
}
