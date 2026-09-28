using ChromaDrop.Core;
using ChromaDrop.Core.Game0;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ChromaDrop.Web;

public partial class App
{
  private static readonly string[] ShapeColors =
    ["30d6ef", "458ef2", "f5963d", "ffe15d", "59db68", "bd82ef", "f16670"];
  private readonly Game0BrowserSession _browser = new(Random.Shared.Next());
  private readonly CancellationTokenSource _cancellation = new();
  private ElementReference _surface;
  private IJSObjectReference? _interop;
  private DotNetObjectReference<App>? _callback;
  private Task? _loop;
  private bool _ready;
  private bool _disposed;
  private double _scale = 1;

  [Inject] private IJSRuntime JS { get; set; } = null!;

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (!firstRender || _disposed) return;
    var interop = await JS.InvokeAsync<IJSObjectReference>("import", "./interop.js?v=game0-1");
    if (_disposed) { await interop.DisposeAsync(); return; }
    _interop = interop;
    _callback = DotNetObjectReference.Create(this);
    await interop.InvokeVoidAsync("connectGame0", _callback, _surface);
    if (_disposed) return;
    _ready = true;
    _loop = RunLoop();
    StateHasChanged();
  }

  private async Task RunLoop()
  {
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var previous = clock.Elapsed.TotalSeconds;
    try
    {
      while (await timer.WaitForNextTickAsync(_cancellation.Token))
      {
        var now = clock.Elapsed.TotalSeconds;
        var delta = now - previous;
        previous = now;
        if (_browser.Paused || _browser.Session.IsOver) continue;
        _browser.Advance(delta);
        await InvokeAsync(StateHasChanged);
      }
    }
    catch (OperationCanceledException) { }
  }

  [JSInvokable]
  public void ViewportChanged(double scale)
  {
    if (_disposed || !double.IsFinite(scale) || scale <= 0) return;
    _scale = scale;
    StateHasChanged();
  }

  [JSInvokable]
  public void PointerChanged(string phase, double x, double y, int button)
  {
    if (!_ready || _disposed) return;
    _browser.PointerChanged(phase, x, y, button);
    StateHasChanged();
  }

  [JSInvokable]
  public void KeyChanged(string key, bool pressed)
  {
    if (!_ready || _disposed) return;
    _browser.KeyChanged(key, pressed);
    if (pressed) StateHasChanged();
  }

  [JSInvokable]
  public void PauseForFocus()
  {
    if (_disposed) return;
    _browser.PauseForFocus();
    StateHasChanged();
  }

  private void TogglePause() { _browser.TogglePause(); StateHasChanged(); }
  private void Restart() { _browser.Restart(); StateHasChanged(); }
  private static string N(double number) => Game0Svg.Number(number);

  private bool InMotion(PieceSource source, int slot) =>
    _browser.Dragging && _browser.Source == source && _browser.Slot == slot ||
    _browser.Falling is { } fall && fall.Source == source && fall.Slot == slot;

  private readonly record struct MiniTile(double X, double Y, double Size);

  private IEnumerable<MiniTile> MiniTiles(Shape shape, bool cat, Game0Rect slot)
  {
    var cells = Game0Session.ShapeCells(shape, 0);
    var width = cells.Max(cell => cell.X) + 1;
    var height = cells.Max(cell => cell.Y) + 1;
    // Native mini-shape caps and padding remain fixed in screen pixels.
    var padding = (cat ? 4 : 12) / _scale;
    var size = Math.Max(0, Math.Min((cat ? 31 : 23) / _scale,
      Math.Min((slot.Width - padding) / width, (slot.Height - padding) / height)));
    var x = slot.X + (slot.Width - width * size) / 2;
    var y = slot.Y + (slot.Height - height * size) / 2;
    return cells.Select(cell => new MiniTile(x + cell.X * size, y + cell.Y * size, size));
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;
    _ready = false;
    _cancellation.Cancel();
    if (_loop is not null) await _loop;
    try
    {
      if (_interop is not null)
      {
        await _interop.InvokeVoidAsync("disconnect");
        await _interop.DisposeAsync();
      }
    }
    catch (JSDisconnectedException) { }
    finally
    {
      _callback?.Dispose();
      _cancellation.Dispose();
    }
  }
}
