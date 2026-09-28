using ChromaDrop.Core;
using ChromaDrop.Core.Game0;
using ChromaDrop.Web;

internal static class Game0BrowserTests
{
  public static void RunAll()
  {
    SuccessfulDropUsesTheNativeTrace();
    FailedDropReturnsTheSameHandPiece();
    BlockedEntryNeverPassesThroughTheRoof();
    PauseAndBlurPreserveAnimationAndCancelDrag();
    TimeoutCancelsAnUnavailableSelection();
    CatsEnterFromEverySide();
  }

  private static void SuccessfulDropUsesTheNativeTrace()
  {
    var browser = new Game0BrowserSession(7);
    var slot = IndexOf(browser.Session, Shape.I);
    BeginHandDrag(browser, slot);
    browser.KeyChanged("KeyR", true);
    Require(browser.Orientation == 1, "R rotates the held piece, rather than restarting.");
    MoveToLane(browser, 6);
    Require(browser.CanDrop && browser.Side == EntrySide.Top && browser.Lane == 6,
      "The hand pointer should select the top entry at lane six.");
    var expected = browser.Session.Preview(PieceSource.Hand, slot, EntrySide.Top, 6, 1)!;
    Release(browser);
    Require(browser.Falling is { Lands: true } && !browser.Dragging,
      "A valid release must start a fall without placing immediately.");
    var remaining = browser.Session.HandTimeLeft;
    var duration = browser.Falling!.Duration;
    browser.Advance(duration / 2);
    Require(browser.Session.Tiles.Count == 0 && browser.Session.HandTimeLeft == remaining,
      "Falling freezes the timer and does not place early.");
    browser.Advance(duration);
    Require(browser.Falling is null && browser.Session.Hand[slot] is null,
      "Completing the fall consumes its hand slot exactly once.");
    Require(browser.Session.Tiles.Keys.ToHashSet().SetEquals(expected.Cells),
      "Browser placement must use the same trace as the native rules.");
    Require(browser.Session.UntilRotation == 3 && browser.Session.HandTimeLeft == remaining,
      "The completed animation increments the rotation rhythm and preserves hand time.");
    browser.Advance(0.25);
    Require(Math.Abs(browser.Session.HandTimeLeft - (remaining - 0.25)) < 0.0001,
      "The timer resumes on the next non-animation tick.");
  }

  private static void FailedDropReturnsTheSameHandPiece()
  {
    var browser = new Game0BrowserSession(7);
    var slot = IndexOf(browser.Session, Shape.I);
    BeginHandDrag(browser, slot);
    browser.KeyChanged("ArrowUp", true);
    MoveToLane(browser, 0);
    Release(browser);
    Require(browser.Falling is { Lands: false, Blocked: false },
      "An empty column should animate through the board and return.");
    var remaining = browser.Session.HandTimeLeft;
    browser.Advance(browser.Falling!.Duration + 1);
    Require(browser.Session.Hand[slot] == Shape.I && browser.Session.Tiles.Count == 0 &&
      browser.Session.SuccessfulPlacements == 0 && browser.Session.HandTimeLeft == remaining,
      "A missed drop cannot consume a piece, count a turn, or spend animation time.");
  }

  private static void BlockedEntryNeverPassesThroughTheRoof()
  {
    var roof = Enumerable.Range(0, Game0Session.Center).ToDictionary(
      y => new Cell(Game0Session.Center, y), _ => new PlacedCell(Shape.I, false));
    var browser = new Game0BrowserSession(12, new Game0Session(12, roof));
    var slot = IndexOf(browser.Session, Shape.I);
    BeginHandDrag(browser, slot);
    browser.KeyChanged("Space", true);
    MoveToLane(browser, Game0Session.Center);
    Release(browser);
    Require(browser.Falling is { Lands: false, Blocked: true } && browser.Falling.End.Y == -4,
      "A roof collision must stop outside the board, rather than pass through occupied cells.");
    browser.Advance(browser.Falling!.Duration);
    Require(browser.Session.Tiles.Count == roof.Count && browser.Session.Hand[slot] == Shape.I,
      "A blocked entry must return the original piece unchanged.");
  }

  private static void PauseAndBlurPreserveAnimationAndCancelDrag()
  {
    var browser = new Game0BrowserSession(7);
    BeginHandDrag(browser, IndexOf(browser.Session, Shape.I));
    browser.KeyChanged("Escape", true);
    Require(!browser.Dragging && !browser.Paused, "Escape cancels a drag before pausing.");
    BeginHandDrag(browser, IndexOf(browser.Session, Shape.I));
    browser.PauseForFocus();
    Require(!browser.Dragging && browser.Paused, "Losing focus pauses and cancels the gesture.");
    browser.TogglePause();
    BeginHandDrag(browser, IndexOf(browser.Session, Shape.I));
    browser.KeyChanged("KeyR", true);
    MoveToLane(browser, 6);
    Release(browser);
    var elapsed = browser.Falling!.Elapsed;
    browser.TogglePause();
    browser.Advance(10);
    Require(browser.Falling is not null && browser.Falling.Elapsed == elapsed && browser.Session.Tiles.Count == 0,
      "Pause preserves the pending animation without committing it.");
    browser.TogglePause();
    browser.Advance(1);
    Require(browser.Session.Tiles.Count == 4, "Resuming finishes the original drop.");
    browser.KeyChanged("F5", true);
    Require(!browser.Paused && !browser.Dragging && browser.Falling is null && browser.Session.Tiles.Count == 0,
      "F5 resets the browser session and all input/animation state.");
    BeginHandDrag(browser, 0);
    browser.PointerChanged("cancel", 0, 0, 0);
    Require(!browser.Dragging && !browser.CanDrop, "Pointer cancellation cannot leave a held piece behind.");
  }

  private static void TimeoutCancelsAnUnavailableSelection()
  {
    var browser = new Game0BrowserSession(3);
    BeginHandDrag(browser, 1);
    browser.Advance(Game0Session.HandSeconds);
    Require(browser.Session.Locked && !browser.Dragging && !browser.CanDrop,
      "Lock must cancel a held slot which is no longer selectable.");
    BeginHandDrag(browser, 1);
    Require(!browser.Dragging, "A locked future hand slot cannot be picked up.");
    BeginHandDrag(browser, 0);
    Require(browser.Dragging, "The first remaining hand slot stays selectable during Lock.");
  }

  private static void CatsEnterFromEverySide()
  {
    foreach (var side in Enum.GetValues<EntrySide>())
    {
      var browser = new Game0BrowserSession(11, new Game0Session(11, initialCats: new Shape?[] { Shape.O }));
      var slot = Game0BrowserSession.CatSlot(0);
      browser.PointerChanged("down", slot.X + slot.Width / 2, slot.Y + slot.Height / 2, 0);
      var board = Game0BrowserSession.Board;
      var centerX = board.X + board.Width / 2;
      var centerY = board.Y + board.Height / 2;
      var point = side switch
      {
        EntrySide.Top => (centerX, board.Y),
        EntrySide.Right => (board.X + board.Width, centerY),
        EntrySide.Bottom => (centerX, board.Y + board.Height),
        _ => (board.X, centerY)
      };
      browser.PointerChanged("move", point.Item1, point.Item2, 0);
      Require(browser.CanDrop && browser.Side == side, $"Cat input should choose the {side} entry.");
      Release(browser);
      Require(browser.Falling is { Lands: true }, $"A {side} cat entry must begin a legal fall.");
      browser.Advance(browser.Falling!.Duration);
      Require(browser.Session.Cats[0] is null && browser.Session.Tiles.Count == 4 &&
        browser.Session.Tiles.Values.All(tile => tile.Cat), $"A {side} cat must use the shared core placement.");
    }
  }

  private static void BeginHandDrag(Game0BrowserSession browser, int index)
  {
    var slot = Game0BrowserSession.HandSlot(index);
    browser.PointerChanged("down", slot.X + slot.Width / 2, slot.Y + slot.Height / 2, 0);
  }

  private static void MoveToLane(Game0BrowserSession browser, int lane)
  {
    browser.PointerChanged("move", Game0BrowserSession.Board.X + (lane + 0.5) * Game0BrowserSession.CellSize,
      Game0BrowserSession.Board.Y + Game0BrowserSession.CellSize, 0);
  }

  private static void Release(Game0BrowserSession browser) =>
    browser.PointerChanged("up", browser.Pointer.X, browser.Pointer.Y, 0);

  private static int IndexOf(Game0Session game, Shape shape) =>
    Enumerable.Range(0, Game0Session.HandSize).First(index => game.Hand[index] == shape);

  private static void Require(bool condition, string message)
  {
    if (!condition) throw new InvalidOperationException(message);
  }
}
