using UnityEngine;

// Shared IMGUI font that can show Korean (Windows' Malgun Gothic, falling back to Arial).
public static class UiFont
{
    private static readonly string[] Names = { "Malgun Gothic", "맑은 고딕", "Arial" };
    private static Font font;

    public static Font Get()
    {
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont(Names, 14);
        return font;
    }

    public static GUIStyle Style(int size, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle fontStyle = FontStyle.Normal)
    {
        var style = new GUIStyle(GUI.skin.label) { font = Get(), fontSize = size, alignment = anchor, fontStyle = fontStyle, wordWrap = false };
        style.normal.textColor = Color.white;
        return style;
    }

    public static void Fill(Rect r, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color * previous;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
