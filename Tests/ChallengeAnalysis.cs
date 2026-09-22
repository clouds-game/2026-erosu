using ChromaDrop.Core;

// Offline authoring check: enumerate top-down placements through the actual
// session rules. Tucks under overhangs are not enumerated; this is not a claim
// of uniqueness over every possible input sequence.
static class ChallengeAnalysis
{
  public static int Run()
  {
    var level = PuzzleLevels.All.Single(puzzle => !puzzle.IsTutorial);
    var cache = new Dictionary<string, long>();
    var nodes = 0;
    var earlyClears = 0;
    var losingClears = 0;
    long Search(IReadOnlyList<Piece> board, int step)
    {
      if (step == level.Sequence.Count) return board.Count == 0 ? 1 : 0;
      var key = step + ":" + string.Join(";", board.OrderBy(piece => piece.Id).Select(piece =>
        $"{piece.Id},{piece.Color}:" + string.Join(",", piece.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).Select(cell => $"{cell.X}/{cell.Y}"))));
      if (cache.TryGetValue(key, out var known)) return known;
      nodes++;
      long wins = 0;
      var seen = new HashSet<string>();
      for (var rotation = 0; rotation < 4; rotation++)
      {
        for (var column = 0; column < Board.Width; column++)
        {
          var game = new GameSession(level with { InitialPieces = board, Sequence = new[] { level.Sequence[step] } });
          if (game.IsFinished) continue;
          var valid = true;
          for (var turn = 0; turn < rotation; turn++) valid &= game.Rotate();
          if (!valid) continue;
          while (game.Active!.Cells.Min(cell => cell.X) != column)
          {
            if (!game.Move(Math.Sign(column - game.Active.Cells.Min(cell => cell.X)), 0)) { valid = false; break; }
          }
          if (!valid) continue;
          var ghostKey = string.Join(",", game.Ghost()!.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).Select(cell => $"{cell.X}/{cell.Y}"));
          if (!seen.Add(ghostKey)) continue;
          game.HardDrop();
          for (var tick = 0; tick < 500 && !game.IsFinished; tick++) game.Advance(0.05);
          if (!game.IsFinished) throw new Exception("Resolution exceeded limit");
          var descendants = Search(game.Board.Pieces, step + 1);
          if (step < 4 && game.Cleared > 0)
          {
            earlyClears++;
            if (descendants == 0) losingClears++;
          }
          if (step == 0) Console.WriteLine($"First placement x={column} rotation={rotation}: {descendants} winning continuations");
          wins += descendants;
        }
      }
      return cache[key] = wins;
    }
    var total = Search(level.InitialPieces, 0);
    Console.WriteLine($"Winning drop sequences: {total}; states: {nodes}; early-clear branches: {earlyClears}; losing early-clear branches: {losingClears}");
    return total > 0 ? 0 : 1;
  }
}
