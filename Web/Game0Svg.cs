using System.Globalization;

namespace ChromaDrop.Web;

public static class Game0Svg
{
  public static string Number(double number) => number.ToString("0.###", CultureInfo.InvariantCulture);

  public static string TintMatrix(string color)
  {
    var red = int.Parse(color[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
    var green = int.Parse(color[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
    var blue = int.Parse(color[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
    return $"{Number(red)} 0 0 0 0 0 {Number(green)} 0 0 0 0 0 {Number(blue)} 0 0 0 0 0 1 0";
  }
}
