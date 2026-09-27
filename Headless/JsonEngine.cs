using System.Text.Json;
using System.Text.Json.Serialization;
using ChromaDrop.Core;

namespace ChromaDrop.Headless;

public sealed class JsonEngine
{
  public const int TickRate = 60;
  private static readonly JsonSerializerOptions Json = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
  };
  private GameSession? _game;
  private long _ticks;

  public string Handle(string line)
  {
    JsonElement? id = null;
    var events = new List<ClearWave>();
    try
    {
      using var document = JsonDocument.Parse(line);
      var request = document.RootElement;
      Require(request.ValueKind == JsonValueKind.Object, "Request must be an object.");
      if (request.TryGetProperty("id", out var value)) id = value.Clone();
      var command = request.GetProperty("command").GetString();
      var fields = command switch
      {
        "start" => new[] { "mode", "seed", "level" },
        "tick" => new[] { "count", "soft_drop" },
        "move" => new[] { "direction" },
        "pause" => new[] { "paused" },
        "state" or "rotate" or "hard_drop" or "levels" => Array.Empty<string>(),
        _ => throw new ArgumentException("Unknown command.")
      };
      var seen = new HashSet<string>();
      foreach (var field in request.EnumerateObject())
        Require(seen.Add(field.Name) && (field.Name is "id" or "command" || fields.Contains(field.Name)),
          $"Unexpected or duplicate field: {field.Name}");

      bool? applied = null;
      if (command == "levels")
        return Reply(id, true, null, null, events, PuzzleLevels.All);
      if (command == "start")
      {
        var mode = request.GetProperty("mode").GetString();
        GameSession next;
        var standalone = mode == "endless" ? GameSelection.Free
          : GameSelection.TryParse(mode, out var parsed) && parsed.Mode != ModeId.Puzzle ? parsed
          : (GameSelection?)null;
        if (standalone is not null)
        {
          Require(!seen.Contains("level"), "Standalone modes do not accept level.");
          next = GameSessionFactory.Create(standalone.Value, request.GetProperty("seed").GetInt32());
        }
        else
        {
          Require(mode == "puzzle" && !seen.Contains("seed"), "Use endless/pollution with seed or puzzle with level.");
          var level = request.GetProperty("level").GetInt32();
          var puzzle = PuzzleLevels.All.FirstOrDefault(item => item.Number == level);
          Require(puzzle is not null, "Unknown puzzle level.");
          next = GameSessionFactory.Create(puzzle!);
        }
        _game = next;
        _ticks = 0;
      }
      else
      {
        Require(_game is not null, "Start a game first.");
        var game = _game!;
        switch (command)
        {
          case "tick":
            var count = request.GetProperty("count").GetInt32();
            Require(count is >= 0 and <= 3600, "count must be between 0 and 3600.");
            var softDrop = request.TryGetProperty("soft_drop", out var soft) && soft.GetBoolean();
            game.Matched += events.Add;
            try
            {
              for (var i = 0; i < count; i++) game.Advance(1.0 / TickRate, softDrop);
              _ticks += count;
            }
            finally { game.Matched -= events.Add; }
            break;
          case "move":
            var direction = request.GetProperty("direction").GetString();
            Require(direction is "left" or "right" or "down", "direction must be left, right or down.");
            applied = direction switch
            {
              "left" => game.Move(-1, 0),
              "right" => game.Move(1, 0),
              _ => game.Move(0, 1)
            };
            break;
          case "rotate": applied = game.Rotate(); break;
          case "hard_drop":
            applied = game.AcceptsInput;
            game.Matched += events.Add;
            try { game.HardDrop(); }
            finally { game.Matched -= events.Add; }
            break;
          case "pause": game.SetPaused(request.GetProperty("paused").GetBoolean()); break;
        }
      }
      return Reply(id, true, applied, null, events);
    }
    catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException
      or KeyNotFoundException or FormatException or OverflowException)
    {
      return Reply(id, false, null, error.Message, events);
    }
  }

  private string Reply(JsonElement? id, bool ok, bool? applied, string? error,
    List<ClearWave> events, object? levels = null) => JsonSerializer.Serialize(new
    {
      ProtocolVersion = 1, Id = id, Ok = ok, Applied = applied, Error = error,
      TickRate, Ticks = _ticks, State = Snapshot(), Events = events, Levels = levels
    }, Json);

  private object? Snapshot() => _game is not { } game ? null : new
  {
    Width = Board.Width, Height = Board.Height,
    Mode = game.Mode switch { ModeId.Free => "endless", ModeId.Puzzle => "puzzle", _ => ModeCatalog.Get(game.Mode).Token },
    Puzzle = game.Puzzle,
    game.Phase, game.Paused, game.AcceptsInput, game.IsFinished,
    game.Score, game.Cleared, game.BestChain, game.Locked, game.Level,
    PollutionCleared = game.Metric("purified")?.Value ?? 0,
    PollutionCountdown = game.Metric("next_rise")?.Value ?? 0,
    game.Metrics,
    game.Remaining, game.ClearProgress, RiseProgress = game.TransitionProgress,
    Active = PieceSnapshot(game, game.Active), Ghost = PieceSnapshot(game, game.Ghost()),
    Next = game.Next.Select(piece => PieceSnapshot(game, piece)),
    Pieces = game.Board.Pieces.Select(piece => PieceSnapshot(game, piece)), game.Wave
  };

  private static object? PieceSnapshot(GameSession game, Piece? piece) => piece is null ? null : new
  {
    piece.Id, piece.Shape, piece.Color, piece.Cells, piece.IsPollution, piece.Kind, piece.ExpiresAtLock,
    TurnsRemaining = game.TurnsRemaining(piece)
  };

  private static void Require(bool condition, string message)
  {
    if (!condition) throw new ArgumentException(message);
  }
}
