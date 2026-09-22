using System.Globalization;
using System.Text.Json;

namespace ChromaDrop.Localization;

public sealed class UiText
{
  public static readonly string[] Languages = ["en", "zh-CN", "ja"];
  public static readonly string[] LanguageNames = ["English", "简体中文", "日本語"];
  private static readonly Dictionary<string, Dictionary<string, string>> Catalog = Load();
  public string Language { get; private set; } = "en";

  public static string Normalize(string? locale)
  {
    var prefix = locale?.Replace('_', '-').Split('-')[0].ToLowerInvariant();
    return prefix switch { "zh" or "cn" => "zh-CN", "ja" => "ja", _ => "en" };
  }

  public void SetLanguage(string? locale) => Language = Normalize(locale);

  public string Get(string key, params object[] arguments)
  {
    var template = Catalog[Language].GetValueOrDefault(key) ?? Catalog["en"][key];
    return arguments.Length == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, arguments);
  }

  public static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

  private static Dictionary<string, Dictionary<string, string>> Load()
  {
    using var stream = typeof(UiText).Assembly.GetManifestResourceStream("ChromaDrop.Localization.strings.json")
      ?? throw new InvalidOperationException("Missing UI translations.");
    return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
  }
}
