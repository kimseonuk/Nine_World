using UnityEngine;

// The skill system's VARCO art (Resources/UI/SkillUi): gold Norse frames, cards, slots, rings, bars, element emblems.
// Every helper falls back to plain fills when a texture is missing, so a panel never vanishes.
public static class UiArt
{
    private static bool loaded;
    private static Texture2D frame, bg, card, slot, ring, bar, tip, banner, button;
    private static readonly Texture2D[] emblems = new Texture2D[9];   // 8 elements + synergy
    private static readonly string[] EmblemNames = { "fire", "ice", "lightning", "wind", "earth", "holy", "shadow", "blood", "synergy" };

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        frame = Resources.Load<Texture2D>("UI/SkillUi/ui_frame");
        bg = Resources.Load<Texture2D>("UI/SkillUi/ui_bg");
        card = Resources.Load<Texture2D>("UI/SkillUi/ui_card");
        slot = Resources.Load<Texture2D>("UI/SkillUi/ui_slot");
        ring = Resources.Load<Texture2D>("UI/SkillUi/ui_ring");
        bar = Resources.Load<Texture2D>("UI/SkillUi/ui_bar");
        tip = Resources.Load<Texture2D>("UI/SkillUi/ui_tip");
        banner = Resources.Load<Texture2D>("UI/SkillUi/ui_banner");
        button = Resources.Load<Texture2D>("UI/SkillUi/ui_button_s");
        for (int i = 0; i < emblems.Length; i++) emblems[i] = Resources.Load<Texture2D>("UI/SkillUi/em_" + EmblemNames[i]);
    }

    public static Texture2D ButtonTexture { get { Load(); return button; } }

    // A texture drawn nine-sliced: l/r/t/b are its border in texture pixels, shown at `scale` screen pixels per texel
    public static void NineSlice(Rect r, Texture2D tex, float l, float rt, float t, float b, float scale)
    {
        if (tex == null || Event.current.type != EventType.Repaint) return;
        float W = tex.width, H = tex.height;
        float sl = Mathf.Min(l * scale, r.width / 2f), sr = Mathf.Min(rt * scale, r.width / 2f);
        float st = Mathf.Min(t * scale, r.height / 2f), sb = Mathf.Min(b * scale, r.height / 2f);
        float[] xs = { r.x, r.x + sl, r.xMax - sr, r.xMax };
        float[] ys = { r.y, r.y + st, r.yMax - sb, r.yMax };
        float[] us = { 0f, l / W, 1f - rt / W, 1f };
        float[] vs = { 1f, 1f - t / H, b / H, 0f };   // (texture v runs upwards)
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var dst = new Rect(xs[i], ys[j], xs[i + 1] - xs[i], ys[j + 1] - ys[j]);
                if (dst.width <= 0f || dst.height <= 0f) continue;
                var uv = new Rect(us[i], vs[j + 1], us[i + 1] - us[i], vs[j] - vs[j + 1]);
                GUI.DrawTextureWithTexCoords(dst, tex, uv, true);
            }
    }

    // A window: dark slate inside the gold raven frame
    public static void Panel(Rect r)
    {
        Load();
        // (the frame sits just outside the rect so the content keeps its room)
        if (bg != null) { GUI.color = new Color(0.85f, 0.85f, 0.9f, 0.97f); GUI.DrawTexture(r, bg, ScaleMode.ScaleAndCrop); GUI.color = Color.white; }
        else UiFont.Fill(r, new Color(0.08f, 0.07f, 0.1f, 0.96f));
        if (frame != null) NineSlice(new Rect(r.x - 14f, r.y - 14f, r.width + 28f, r.height + 28f), frame, 112f, 112f, 112f, 112f, 0.36f);
        else UiFont.Fill(new Rect(r.x, r.y, r.width, 3f), new Color(1f, 0.8f, 0.4f, 0.9f));
    }

    // A smaller boxed area inside a window (a detail pane, a list row)
    public static void Inset(Rect r, float alpha = 0.35f)
    {
        UiFont.Fill(r, new Color(0f, 0f, 0f, alpha));
        Color g = new Color(0.85f, 0.65f, 0.3f, 0.55f);
        UiFont.Fill(new Rect(r.x, r.y, r.width, 1f), g);
        UiFont.Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), g);
        UiFont.Fill(new Rect(r.x, r.y, 1f, r.height), g);
        UiFont.Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), g);
    }

    // A reward card: lifted and brighter under the mouse, greyed when it can't be taken
    public static bool Card(Rect r, string label = "")
    {
        Load();
        bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
        Rect d = hover ? new Rect(r.x - 3f, r.y - 6f, r.width + 6f, r.height + 6f) : r;
        var inner = new Rect(d.x + d.width * 0.06f, d.y + d.height * 0.05f, d.width * 0.88f, d.height * 0.9f);
        UiFont.Fill(inner, hover ? new Color(0.16f, 0.13f, 0.08f, 0.96f) : new Color(0.07f, 0.065f, 0.08f, 0.96f));
        if (hover) UiFont.Fill(inner, new Color(1f, 0.8f, 0.35f, 0.08f));
        GUI.color = GUI.enabled ? (hover ? new Color(1.15f, 1.1f, 1f) : Color.white) : new Color(0.45f, 0.45f, 0.45f, 0.8f);
        if (card != null) GUI.DrawTexture(d, card, ScaleMode.StretchToFill);
        GUI.color = Color.white;
        return GUI.Button(r, label, GUIStyle.none);
    }

    // A square slot (an item cell); tint lights the frame (selected, element)
    public static void Slot(Rect r, Color tint)
    {
        Load();
        UiFont.Fill(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), new Color(0.06f, 0.055f, 0.07f, 0.95f));
        if (slot == null) { UiFont.Fill(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, 2f), tint); return; }
        GUI.color = tint;
        NineSlice(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), slot, 40f, 40f, 40f, 40f, Mathf.Clamp(Mathf.Min(r.width, r.height) / 300f, 0.14f, 0.3f));
        GUI.color = Color.white;
    }

    // The frame drawn over a slot's content (icons go between Slot's back and this)
    public static void SlotOver(Rect r, Color tint) => Slot(r, tint);

    // A round socket: a coloured gem (or empty dark hole) in a gold ring
    public static void Ring(Vector2 c, float radius, Color? fill)
    {
        Load();
        SkillSystem.DrawDisc(c, radius * 0.82f, fill ?? new Color(0.07f, 0.065f, 0.08f, 0.95f));
        if (fill.HasValue) SkillSystem.DrawDisc(new Vector2(c.x - radius * 0.22f, c.y - radius * 0.22f), radius * 0.28f, new Color(1f, 1f, 1f, 0.45f));
        if (ring != null) GUI.DrawTexture(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), ring);
    }

    // An element's medallion (index 0-7), or the combo valknut (8 / -1)
    public static void Emblem(Rect r, int element)
    {
        Load();
        int i = element < 0 || element > 8 ? 8 : element;
        if (emblems[i] != null) GUI.DrawTexture(r, emblems[i], ScaleMode.ScaleToFit);
        else SkillSystem.DrawDisc(r.center, r.width / 2f, i < 8 ? SkillCatalog.ElementColor((SkillElement)i) : new Color(1f, 0.82f, 0.4f));
    }

    // A gauge: the fill inside the gold rim with knotwork caps
    public static void Bar(Rect r, float k, Color fill, string label, GUIStyle style)
    {
        Load();
        float s = r.height / 96f * 1.25f;
        float cap = 98f * s * 0.62f;
        var channel = new Rect(r.x + cap, r.y + r.height * 0.2f, r.width - 2f * cap, r.height * 0.6f);
        UiFont.Fill(channel, new Color(0f, 0f, 0f, 0.7f));
        UiFont.Fill(new Rect(channel.x, channel.y, channel.width * Mathf.Clamp01(k), channel.height), fill);
        UiFont.Fill(new Rect(channel.x, channel.y, channel.width * Mathf.Clamp01(k), channel.height * 0.35f), new Color(1f, 1f, 1f, 0.18f));
        if (bar != null) NineSlice(new Rect(r.x, r.y - r.height * 0.05f, r.width, r.height * 1.1f), bar, 98f, 98f, 14f, 14f, s);
        if (!string.IsNullOrEmpty(label) && style != null)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(channel.x + 7f, r.y + 1f, channel.width, r.height), label, style);
            GUI.color = Color.white;
            GUI.Label(new Rect(channel.x + 6f, r.y, channel.width, r.height), label, style);
        }
    }

    // A tooltip plate
    public static void Tip(Rect r)
    {
        Load();
        if (tip != null) NineSlice(r, tip, 40f, 40f, 30f, 30f, 0.45f);
        else UiFont.Fill(r, new Color(0.05f, 0.04f, 0.07f, 0.95f));
    }

    // The crimson title ribbon with its text
    public static void Banner(Rect r, string text, GUIStyle style)
    {
        Load();
        if (banner != null) GUI.DrawTexture(r, banner, ScaleMode.StretchToFill);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(r.x + 2f, r.y + r.height * 0.08f + 2f, r.width, r.height * 0.62f), text, style);
        GUI.color = new Color(1f, 0.9f, 0.62f);
        GUI.Label(new Rect(r.x, r.y + r.height * 0.08f, r.width, r.height * 0.62f), text, style);
        GUI.color = Color.white;
    }

    // A GUIStyle for buttons drawn on the walnut-and-gold plate
    public static GUIStyle ButtonStyle(int size)
    {
        Load();
        var st = new GUIStyle(GUI.skin.button) { font = UiFont.Get(), fontSize = size, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow, alignment = TextAnchor.MiddleCenter };
        if (button != null)
        {
            // (150×35 plate: knotwork caps about 18 px, a thin gold rim above and below)
            st.normal.background = st.hover.background = st.active.background = st.focused.background = button;
            st.onNormal.background = st.onHover.background = st.onActive.background = button;
            st.border = new RectOffset(18, 18, 7, 7);
            st.padding = new RectOffset(10, 10, 1, 1);
        }
        st.normal.textColor = new Color(1f, 0.92f, 0.75f);
        st.hover.textColor = st.focused.textColor = Color.white;
        st.active.textColor = new Color(1f, 0.8f, 0.4f);
        return st;
    }
}
