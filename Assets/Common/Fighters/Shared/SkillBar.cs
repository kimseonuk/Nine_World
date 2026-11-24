using System.Collections.Generic;
using UnityEngine;

// The one skill bar every fighter uses — the twin blades' layout: square icons in a row at the middle of the screen's
// foot, Q E R right click Space, the key in a small badge on each icon's corner and two dots underneath for the
// skill's supports (their elements' colours). Names are not drawn.
// A slot darkens from the top while it cools down (the seconds left over it), greys out with "잠김" until learned
// in the [K] tree, and its frame lights up while the skill is in use.
public static class SkillBar
{
    public struct Slot
    {
        public string key, name;
        public Texture2D icon;
        public bool learned;
        public float left, total;     // cooldown: seconds left and the full cooldown
        public bool lit;              // in use: the frame takes litColor
        public Color litColor;
        public float fill;            // 0..1 drawn along the bottom edge (a charge building), 0 = none
        public Color fillColor;
        public string note;           // a small line over the icon (a timer)
        public string lockText;       // shown on an unusable slot (default "잠김")
    }

    public const float Size = 46f, Gap = 14f;
    public static readonly string[] Keys = { "Q", "E", "R", "우클릭", "Space" };
    private static readonly Color Lit = new Color(0.75f, 0.2f, 0.95f);
    private static GUIStyle keyStyle, timeStyle, badgeStyle;
    private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

    public static Texture2D Icon(string resource)
    {
        if (string.IsNullOrEmpty(resource)) return null;
        if (!icons.TryGetValue(resource, out Texture2D tex))
            icons[resource] = tex = Resources.Load<Texture2D>(resource);
        return tex;
    }

    // Where a key sits on the bar (Q E R right click Space); anything else goes last
    public static int Order(string key)
    {
        for (int i = 0; i < Keys.Length; i++)
            if (string.Equals(Keys[i], key, System.StringComparison.OrdinalIgnoreCase)) return i;
        return Keys.Length;
    }

    public static bool Hidden =>
        BossCutscene.Playing || OpeningCutscene.Playing || StoryStage.Cinematic || TitleScreen.Playing || Time.timeScale <= 0f;

    public static void Draw(IList<Slot> slots)
    {
        if (slots == null || slots.Count == 0) return;
        // (the skill system notes what this class's skills are; their support sockets are drawn on them)
        slots = SkillSystem.Merge(slots);
        keyStyle ??= UiFont.Style(11, TextAnchor.LowerCenter, FontStyle.Bold);
        timeStyle ??= UiFont.Style(18, TextAnchor.MiddleCenter, FontStyle.Bold);
        badgeStyle ??= UiFont.Style(11, TextAnchor.MiddleCenter, FontStyle.Bold);
        float width = slots.Count * Size + (slots.Count - 1) * Gap;
        float x = (Screen.width - width) / 2f, y = Screen.height - 90f;
        foreach (Slot s in slots)
        {
            var r = new Rect(x, y, Size, Size);
            // the gold slot (VARCO art), glowing in the skill's colour while it's in use
            Color litC = s.litColor.a > 0f ? s.litColor : Lit;
            if (s.lit) UiFont.Fill(new Rect(r.x - 6f, r.y - 6f, r.width + 12f, r.height + 12f), new Color(litC.r, litC.g, litC.b, 0.45f));
            UiArt.Slot(r, s.lit ? Color.Lerp(Color.white, litC, 0.6f) * 1.3f : Color.white);
            var iconRect = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f);
            if (s.icon != null)
            {
                GUI.color = s.learned ? Color.white : new Color(0.35f, 0.35f, 0.38f, 0.9f);
                GUI.DrawTexture(iconRect, s.icon);
                GUI.color = Color.white;
            }
            if (!s.learned)
                GUI.Label(r, s.lockText ?? "잠김", timeStyle);   // not learned yet: [K] skill tree (or an empty / spent skill slot)
            else if (s.left > 0f)
            {
                UiFont.Fill(new Rect(r.x, r.y, r.width, r.height * Mathf.Clamp01(s.left / Mathf.Max(0.01f, s.total))), new Color(0f, 0f, 0f, 0.65f));
                GUI.Label(r, s.left >= 1f ? Mathf.CeilToInt(s.left).ToString() : s.left.ToString("0.0"), timeStyle);
            }
            else if (s.fill > 0f)
                UiFont.Fill(new Rect(r.x, r.yMax - 5f, r.width * Mathf.Clamp01(s.fill), 5f), s.fillColor.a > 0f ? s.fillColor : Lit);
            if (!string.IsNullOrEmpty(s.note))
                GUI.Label(new Rect(r.x - Gap, r.y - 18f, r.width + 2f * Gap, 16f), s.note, keyStyle);
            // the key in a small badge on the icon's corner (no name on screen); the supports' two dots underneath
            float badge = Mathf.Max(18f, keyStyle.CalcSize(new GUIContent(s.key)).x + 8f);
            var keyRect = new Rect(r.x - 4f, r.y - 6f, badge, 16f);
            UiFont.Fill(keyRect, new Color(0.08f, 0.05f, 0.12f, 0.92f));
            GUI.Label(keyRect, s.key, badgeStyle);
            SkillSystem.DrawSockets(s.key, r);
            x += Size + Gap;
        }
    }
}
