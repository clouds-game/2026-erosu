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
        "start" => new[] { "mode", "seed", "level", "color_profile", "anchored_blocks", "enclosed_fill" },
        "tick" => new[] { "count", "soft_drop" },
        "move" => new[] { "direction" },
        "place" => new[] { "action" },
        "pause" => new[] { "paused" },
        "afterstates" or "placements" or "state" or "rotate" or "hard_drop" or "levels" => Array.Empty<string>(),
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
        if (mode == "endless")
        {
          Require(!seen.Contains("level"), "Endless mode does not accept level.");
          var colors = request.TryGetProperty("color_profile", out var profile)
            ? ColorProfile.Find(profile.GetString() ?? "") : ColorProfile.Default;
          next = new GameSession(new Random(request.GetProperty("seed").GetInt32()), colors: colors,
            anchoredBlocks: request.TryGetProperty("anchored_blocks", out var anchored) && anchored.GetBoolean(),
            enclosedFill: request.TryGetProperty("enclosed_fill", out var fill) && fill.GetBoolean());
        }
        else
        {
          Require(mode == "puzzle" && !seen.Contains("seed") && !seen.Contains("color_profile") && !seen.Contains("anchored_blocks") && !seen.Contains("enclosed_fill"), "Use endless with seed or puzzle with level.");
          var level = request.GetProperty("level").GetInt32();
          var puzzle = PuzzleLevels.All.FirstOrDefault(item => item.Number == level);
          Require(puzzle is not null, "Unknown puzzle level.");
          next = new GameSession(puzzle!);
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
          case "afterstates":
            return Reply(id, true, null, null, events, actions: Enumerable.Range(0, 40)
              .Select(action => new { Action = action, State = Snapshot(game.PreviewPlacement(action)) }).ToArray());
          case "placements":
            return Reply(id, true, null, null, events, actions: Enumerable.Range(0, 40)
              .Select(action => new { Action = action, Piece = game.Placement(action) }).ToArray());
          case "place":
            var action = request.GetProperty("action").GetInt32();
            Require(action is >= 0 and < 40, "action must be between 0 and 39.");
            game.Matched += events.Add;
            try { applied = game.Place(action); }
            finally { game.Matched -= events.Add; }
            break;
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
    List<ClearWave> events, object? levels = null, object? actions = null) => JsonSerializer.Serialize(new
    {
      ProtocolVersion = 1, Id = id, Ok = ok, Applied = applied, Error = error,
      TickRate, Ticks = _ticks, State = Snapshot(), Events = events, Levels = levels, Actions = actions
    }, Json);

  private object? Snapshot() => Snapshot(_game);

  private static object? Snapshot(GameSession? game) => game is null ? null : new
  {
    Width = Board.Width, Height = Board.Height,
    ColorProfile = game.Colors?.Id, ColorWeights = game.Colors?.Weights, Colors = ColorRules.All,
    Mode = game.Puzzle is null ? "endless" : "puzzle", Puzzle = game.Puzzle,
    game.AnchoredBlocks, game.AnchoredSpawnPending, game.EnclosedFill, game.EnclosedFillPending, game.IncomingAnchor, game.Forecast, game.Phase, game.Paused, game.AcceptsInput, game.IsFinished,
    game.Score, game.Cleared, game.BestChain, game.Locked, game.Level,
    game.Remaining, game.ClearProgress, game.Active, Ghost = game.Ghost(),
    game.Next, Pieces = game.Board.Pieces, game.Wave
  };

  private static void Require(bool condition, string message)
  {
    if (!condition) throw new ArgumentException(message);
  }
}
