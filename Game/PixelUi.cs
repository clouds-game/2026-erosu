using Godot;

namespace ChromaDrop.Game;

// Every drawn UI surface comes from the selected Kenney Pixel UI Pack.
public static class PixelUi
{
  public static readonly Color Ink = new("253044");
  public static readonly Color Cream = new("f6f1e4");
  public static readonly Color Muted = new("d7e0ec");
  public static readonly Color Gold = new("f7ce46");
  public static readonly Color ActiveBlue = new("92d7f2");
  public static readonly Color Blue = new("4e7bb2");
  public static readonly Color DeepBlue = new("284a71");
  public static readonly Color PanelBlue = new("365f91");
  public static readonly Texture2D Sheet = GD.Load<Texture2D>("res://Assets/PixelUI/sheet.png");
  public static readonly Rect2 WhiteTileRegion = new(54, 18, 16, 16);
  public static Rect2 TileRegion(int color) => new(162 + color * 108, 18, 16, 16);

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

  public static TextureRect Tile(int color, Vector2 minimum)
  {
    var atlas = new AtlasTexture { Atlas = Sheet, Region = TileRegion(color) };
    return new TextureRect
    {
      Texture = atlas,
      CustomMinimumSize = minimum,
      ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
      StretchMode = TextureRect.StretchModeEnum.Scale,
      TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
      MouseFilter = Control.MouseFilterEnum.Ignore
    };
  }
}
