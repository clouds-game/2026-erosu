using Godot;

namespace ChromaDrop.Game;

// Every drawn UI surface comes from the selected Kenney Pixel UI Pack.
public static class PixelUi
{
  public static readonly Color Ink = new("2b211d");
  public static readonly Color Cream = new("fff0cb");
  public static readonly Color Muted = new("c2aa86");
  public static readonly Color Gold = new("ffd15c");
  public static readonly Texture2D Sheet = GD.Load<Texture2D>("res://Assets/PixelUI/sheet.png");
  public static readonly Rect2 TileRegion = new(54, 18, 16, 16);

  public static StyleBoxTexture Style(string path, float contentMargin, float slice)
  {
    return new StyleBoxTexture
    {
      Texture = GD.Load<Texture2D>(path),
      TextureMarginLeft = slice,
      TextureMarginTop = slice,
      TextureMarginRight = slice,
      TextureMarginBottom = slice,
      ContentMarginLeft = contentMargin,
      ContentMarginTop = contentMargin,
      ContentMarginRight = contentMargin,
      ContentMarginBottom = contentMargin
    };
  }

  public static TextureRect Tile(Color tint, Vector2 minimum)
  {
    var atlas = new AtlasTexture { Atlas = Sheet, Region = TileRegion };
    return new TextureRect
    {
      Texture = atlas,
      Modulate = tint,
      CustomMinimumSize = minimum,
      ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
      StretchMode = TextureRect.StretchModeEnum.Scale,
      TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
      MouseFilter = Control.MouseFilterEnum.Ignore
    };
  }
}
