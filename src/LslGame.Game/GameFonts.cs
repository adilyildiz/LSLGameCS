using System.Numerics;
using Raylib_cs;

namespace LslGame.Game;

/// <summary>
/// Provides anti-aliased Segoe UI / Arial font rendering with proper Turkish character support.
/// </summary>
public static class GameFonts
{
    private static Font? _font;
    private static bool _initialized;

    public static Font GetFont()
    {
        if (_initialized && _font.HasValue)
            return _font.Value;

        string[] fontCandidates =
        [
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\calibri.ttf"
        ];

        foreach (var path in fontCandidates)
        {
            if (File.Exists(path))
            {
                try
                {
                    // Generate Turkish and standard ASCII codepoints:
                    // ASCII 32-126 plus Turkish characters: ç, ğ, ı, ö, ş, ü, Ç, Ğ, İ, Ö, Ş, Ü
                    var codepointList = new List<int>();
                    for (int i = 32; i < 127; i++) codepointList.Add(i);
                    int[] trChars = [0x00E7, 0x011F, 0x0131, 0x00F6, 0x015F, 0x00FC, 0x00C7, 0x011E, 0x0130, 0x00D6, 0x015E, 0x00DC];
                    codepointList.AddRange(trChars);

                    int[] codepoints = codepointList.ToArray();
                    var loaded = Raylib.LoadFontEx(path, 40, codepoints, codepoints.Length);
                    if (loaded.Texture.Id > 0)
                    {
                        Raylib.SetTextureFilter(loaded.Texture, TextureFilter.Bilinear);
                        _font = loaded;
                        _initialized = true;
                        return _font.Value;
                    }
                }
                catch { }
            }
        }

        _font = Raylib.GetFontDefault();
        _initialized = true;
        return _font.Value;
    }

    public static void DrawText(string text, float x, float y, float size, Color color)
    {
        var font = GetFont();
        Raylib.DrawTextEx(font, text, new Vector2(x, y), size, 1f, color);
    }

    public static Vector2 MeasureText(string text, float size)
    {
        var font = GetFont();
        return Raylib.MeasureTextEx(font, text, size, 1f);
    }

    public static void Unload()
    {
        if (_initialized && _font.HasValue && _font.Value.Texture.Id > 0)
        {
            Raylib.UnloadFont(_font.Value);
            _font = null;
            _initialized = false;
        }
    }
}