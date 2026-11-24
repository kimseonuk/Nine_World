using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 나인 월드 (NINE WORLD) — the title screen the game opens on. VARCO key art (Resources/Title/title_bg: the world tree
// among the nine realms; title_logo: the logo) drifts slowly behind drifting motes; the logo rises out of the dark with
// a breathing glow; then the menu: 시작하기 (the opening if it's on, else straight in) · 오프닝 보기 · 게임 종료.
// ↑/↓ or W/S and Enter/Space, or the mouse. While it's up the game holds still and its HUD stays hidden (Playing).
public class TitleScreen : MonoBehaviour
{
    public static bool Playing { get; private set; }

    public const string GameName = "NINE WORLD";
    public const string GameNameKo = "나인 월드";

    private Action start, opening;
    private Texture2D background, logo, mote;
    private float time, fadeOut = -1f;
    private int choice;
    private Action chosen;
    private readonly string[] items = { "시작하기", "오프닝 보기", "게임 종료" };
    private readonly List<Vector4> motes = new List<Vector4>();   // x, y (0..1), size, speed
    private GUIStyle itemStyle, subStyle, hintStyle, tagStyle;
    private Rect[] itemRects = new Rect[0];
    private Vector2 lastMouse;

    public static void Show(Action start, Action opening)
    {
        if (Playing) return;
        var t = new GameObject("TitleScreen").AddComponent<TitleScreen>();
        t.start = start;
        t.opening = opening;
    }

    private void Awake()
    {
        Playing = true;
        background = Resources.Load<Texture2D>("Title/title_bg");
        logo = Resources.Load<Texture2D>("Title/title_logo");
        mote = Resources.Load<Texture2D>("Vfx/glow");
        for (int i = 0; i < 70; i++)
            motes.Add(new Vector4(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.Range(3f, 11f), UnityEngine.Random.Range(0.006f, 0.025f)));
    }

    private void OnDestroy() => Playing = false;

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        time += dt;
        for (int i = 0; i < motes.Count; i++)
        {
            Vector4 m = motes[i];
            m.y -= m.w * dt;                                  // rising (GUI y runs down)
            m.x += Mathf.Sin(time * 0.4f + i) * 0.002f * dt;
            if (m.y < -0.05f) { m.y = 1.05f; m.x = UnityEngine.Random.value; }
            motes[i] = m;
        }

        if (fadeOut >= 0f)
        {
            fadeOut += dt;
            if (fadeOut > 1.1f)
            {
                Action then = chosen;
                Destroy(gameObject);
                then?.Invoke();
            }
            return;
        }
        if (time < MenuAt) return;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) choice = (choice + items.Length - 1) % items.Length;
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) choice = (choice + 1) % items.Length;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Pick(choice);
        }
        if (choice != heard) { heard = choice; GameAudio.Play("ui_move"); }
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 p = mouse.position.ReadValue();
            Vector2 gui = new Vector2(p.x, Screen.height - p.y);
            bool moved = (p - lastMouse).sqrMagnitude > 1f;
            lastMouse = p;
            for (int i = 0; i < itemRects.Length; i++)
                if (itemRects[i].Contains(gui))
                {
                    if (moved) choice = i;
                    if (mouse.leftButton.wasPressedThisFrame) Pick(i);
                }
        }
    }

    private const float LogoAt = 0.8f, MenuAt = 2.6f;

    private int heard;

    private void Pick(int i)
    {
        choice = i;
        heard = i;
        GameAudio.Play(i == 2 ? "ui_back" : "ui_start");
        if (i == 0) chosen = start;
        else if (i == 1) chosen = opening;
        else
        {
            chosen = () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            };
        }
        fadeOut = 0f;
    }

    private void OnGUI()
    {
        GUI.depth = -200;
        float w = Screen.width, h = Screen.height, k = h / 720f;
        Styles(k);

        // The key art, slowly drifting in (a slow push and pan), over black
        UiFont.Fill(new Rect(0f, 0f, w, h), Color.black);
        float intro = Mathf.SmoothStep(0f, 1f, time / 2.2f);
        // (the art is square: it stands on the right at full height, a darkened wash of it fills the left panel)
        float artSize = h * (1.06f - 0.04f * Mathf.Clamp01(time / 30f));
        Rect art = new Rect(w - artSize * 0.93f + Mathf.Sin(time * 0.05f) * 6f * k, (h - artSize) / 2f + Mathf.Sin(time * 0.07f) * 4f * k, artSize, artSize);
        float panel = art.x + artSize * 0.18f;                // where the art fades out into the panel
        if (background != null)
        {
            GUI.color = new Color(0.35f, 0.33f, 0.45f, 0.55f * intro);
            GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, w, h), background, new Rect(0f, 0.2f, 0.35f, 0.6f));
            GUI.color = new Color(1f, 1f, 1f, intro);
            GUI.DrawTexture(art, background);
        }
        // The art's left edge fading into the dark panel, a vignette, a deeper band behind the menu
        for (int i = 0; i < 24; i++)
        {
            float t = i / 24f;
            UiFont.Fill(new Rect(0f, 0f, panel - artSize * 0.3f * t, h), new Color(0.02f, 0.015f, 0.04f, 0.09f * intro));
        }
        for (int i = 0; i < 10; i++)
        {
            float a = 0.07f * (1f - i / 10f);
            UiFont.Fill(new Rect(0f, h * (0.8f + i * 0.02f), w, h), new Color(0f, 0f, 0f, a * intro));
            UiFont.Fill(new Rect(0f, 0f, w, h * (0.06f - i * 0.006f)), new Color(0f, 0f, 0f, a * intro));
        }

        // Golden motes and ash rising
        if (mote != null)
            for (int i = 0; i < motes.Count; i++)
            {
                Vector4 m = motes[i];
                float s = m.z * k;
                float flicker = 0.5f + 0.5f * Mathf.Sin(time * 2f + i * 1.7f);
                GUI.color = i % 4 == 0 ? new Color(0.6f, 0.4f, 1f, 0.35f * flicker * intro) : new Color(1f, 0.8f, 0.4f, 0.55f * flicker * intro);
                GUI.DrawTexture(new Rect(m.x * w - s / 2f, m.y * h - s / 2f, s, s), mote);
            }

        // The logo rising out of the dark, its glow breathing
        float logoIn = Mathf.SmoothStep(0f, 1f, (time - LogoAt) / 1.6f);
        // (everything on the left panel, centred on its middle)
        float cx = Mathf.Min(w * 0.24f, (w - h) * 0.5f + h * 0.12f);
        float colW = Mathf.Min(w * 0.5f, cx * 2f);
        float colX = cx - colW / 2f;
        float logoW = Mathf.Min(colW * 0.95f, h * 0.6f);
        float logoH = logo != null ? logoW * logo.height / logo.width : logoW * 0.3f;
        Rect logoRect = new Rect(cx - logoW / 2f, h * 0.02f + (1f - logoIn) * 24f * k, logoW, logoH);
        if (logo != null)
        {
            float glow = 0.5f + 0.5f * Mathf.Sin(time * 1.3f);
            GUI.color = new Color(1f, 0.85f, 0.5f, 0.18f * glow * logoIn);
            for (int i = 0; i < 4; i++)
            {
                Vector2 o = new Vector2((i % 2 * 2 - 1) * 3f * k, (i / 2 * 2 - 1) * 3f * k);
                GUI.DrawTexture(new Rect(logoRect.x + o.x, logoRect.y + o.y, logoRect.width, logoRect.height), logo, ScaleMode.ScaleToFit);
            }
            GUI.color = new Color(1f, 1f, 1f, logoIn);
            GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit);
        }
        else
        {
            var big = UiFont.Style(Mathf.RoundToInt(84f * k), TextAnchor.MiddleCenter, FontStyle.Bold);
            big.normal.textColor = new Color(1f, 0.85f, 0.45f, logoIn);
            GUI.Label(new Rect(colX, h * 0.12f, colW, 120f * k), GameName, big);
        }
        GUI.color = Color.white;
        // 나인 월드, and the line beneath
        float subY = Mathf.Min(logoRect.yMax - 22f * k, h * 0.6f);
        subStyle.normal.textColor = new Color(0.95f, 0.88f, 0.7f, logoIn);
        Shadowed(new Rect(colX, subY, colW, 40f * k), GameNameKo, subStyle, logoIn);
        tagStyle.normal.textColor = new Color(0.78f, 0.74f, 0.85f, logoIn * 0.9f);
        Shadowed(new Rect(colX, subY + 38f * k, colW, 28f * k), "무너져 가는 아홉 세계, 마지막 전사의 여정", tagStyle, logoIn);

        // The menu
        float menuIn = Mathf.Clamp01((time - MenuAt) / 0.6f);
        if (menuIn > 0f)
        {
            if (itemRects.Length != items.Length) itemRects = new Rect[items.Length];
            float top = subY + 84f * k, step = 44f * k, itemW = Mathf.Min(300f * k, colW);
            for (int i = 0; i < items.Length; i++)
            {
                Rect r = new Rect(cx - itemW / 2f, top + i * step, itemW, step - 6f * k);
                itemRects[i] = r;
                bool on = i == choice;
                if (on)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(time * 4f);
                    UiFont.Fill(new Rect(r.x, r.y + r.height * 0.15f, r.width, r.height * 0.7f), new Color(0.95f, 0.75f, 0.35f, 0.16f * menuIn));
                    UiFont.Fill(new Rect(r.x + r.width * 0.2f, r.yMax - 2f * k, r.width * 0.6f, 2f * k), new Color(1f, 0.82f, 0.4f, 0.9f * menuIn * pulse));
                    itemStyle.fontSize = Mathf.RoundToInt(27f * k);
                }
                else
                    itemStyle.fontSize = Mathf.RoundToInt(23f * k);
                itemStyle.normal.textColor = on ? new Color(1f, 0.88f, 0.55f, menuIn) : new Color(0.82f, 0.8f, 0.86f, 0.75f * menuIn);
                Shadowed(r, (on ? "◆  " : "") + items[i] + (on ? "  ◆" : ""), itemStyle, menuIn);
            }
            hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.4f * menuIn);
            GUI.Label(new Rect(colX, h - 30f * k, colW, 24f * k), "[↑/↓] 선택   [Enter · 클릭] 결정", hintStyle);
            hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.3f * menuIn);
            GUI.Label(new Rect(w - 160f * k, h - 30f * k, 150f * k, 24f * k), "v0.4", hintStyle);
        }

        // Fade to black when something is chosen
        if (fadeOut >= 0f)
            UiFont.Fill(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, Mathf.Clamp01(fadeOut / 1f)));
    }

    private static void Shadowed(Rect r, string text, GUIStyle style, float alpha)
    {
        Color c = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.7f * alpha);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
        style.normal.textColor = c;
        GUI.Label(r, text, style);
    }

    private void Styles(float k)
    {
        if (itemStyle == null)
        {
            itemStyle = UiFont.Style(23, TextAnchor.MiddleCenter, FontStyle.Bold);
            subStyle = UiFont.Style(30, TextAnchor.MiddleCenter, FontStyle.Bold);
            tagStyle = UiFont.Style(17, TextAnchor.MiddleCenter, FontStyle.Italic);
            hintStyle = UiFont.Style(14, TextAnchor.MiddleCenter);
        }
        subStyle.fontSize = Mathf.RoundToInt(30f * k);
        tagStyle.fontSize = Mathf.RoundToInt(17f * k);
        hintStyle.fontSize = Mathf.RoundToInt(14f * k);
    }
}
