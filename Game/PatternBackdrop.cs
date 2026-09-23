using Godot;

namespace ChromaDrop.Game;

public partial class PatternBackdrop : Control
{
  private readonly Texture2D _pattern = GD.Load<Texture2D>("res://Assets/PixelUI/Pattern/quiet.png");

  public override void _Draw()
  {
    DrawRect(new Rect2(Vector2.Zero, Size), PixelUi.Blue);
    const float tileSize = 64;
    for (var y = 0f; y < Size.Y; y += tileSize)
      for (var x = 0f; x < Size.X; x += tileSize)
        DrawTextureRect(_pattern, new Rect2(x, y, tileSize, tileSize), false,
          new Color(0.18f, 0.34f, 0.59f, 0.05f));
  }

  public override void _Notification(int what)
  {
    if (what == NotificationResized) QueueRedraw();
  }
}
