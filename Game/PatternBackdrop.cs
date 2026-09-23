using Godot;

namespace ChromaDrop.Game;

public partial class PatternBackdrop : Control
{
  private readonly Texture2D _pattern = GD.Load<Texture2D>("res://Assets/PixelUI/Pattern/quiet.png");

  public override void _Draw()
  {
    DrawRect(new Rect2(Vector2.Zero, Size), new Color("121711"));
    const float tileSize = 64;
    for (var y = 0f; y < Size.Y; y += tileSize)
      for (var x = 0f; x < Size.X; x += tileSize)
        DrawTextureRect(_pattern, new Rect2(x, y, tileSize, tileSize), false,
          new Color(0.20f, 0.26f, 0.19f, 0.10f));
  }

  public override void _Notification(int what)
  {
    if (what == NotificationResized) QueueRedraw();
  }
}
