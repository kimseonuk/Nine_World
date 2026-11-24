using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

// A visual-novel (미연시) style conversation over the game: the scene behind is darkened like a backdrop, the speakers'
// portraits stand at the sides, and the lines come up in a dialogue box along the bottom with a name plate, letter by
// letter. Space, Enter or a click shows the rest of a line / goes on; holding Ctrl runs through; "건너뛰기" ends it.
//
// Portraits are drawn from the speakers' own 3D models (a copy posed far off-stage, lit and filmed into a texture), so
// expressions are a matter of pose, light and effect (VNPortrait). An illustration can stand in for any of them: put
// Resources/Dialogue/<key>_<expression>.png (or <key>.png) in the project and it is shown instead of the 3D render;
// Resources/Dialogue/bg_<key>.png likewise replaces the darkened scene as the backdrop.
//
// A scene is a coroutine: open it, stage the portraits, yield return Say(...) line by line, then Close().
public class VisualNovel : MonoBehaviour
{
    public static VisualNovel Current { get; private set; }
    public static bool Playing => Current != null;

    public enum Side { Left, Right }

    // The backdrop
    public float Dim;                 // 0..1 black over the scene
    public Color Tint = Color.clear;  // a colour wash over it (red runes, the dark creeping in)
    public float Shake;               // the whole frame trembling (a strong line, a blow)
    public Texture Backdrop;          // an illustration instead of the scene, if there is one

    public bool Skipping { get; private set; }

    private readonly Dictionary<Side, VNPortrait> portraits = new Dictionary<Side, VNPortrait>();
    private string speaker, text;
    private Color nameColor = Color.white;
    private bool narration, big;
    private float shown;              // letters shown so far
    private bool waiting, advance;
    private float boxAlpha;
    private float closing = -1f;
    private GUIStyle nameStyle, textStyle, bigStyle, narrationStyle, hintStyle, skipStyle;

    public static VisualNovel Open(string backdropKey = null)
    {
        if (Current != null) Destroy(Current.gameObject);
        var vn = new GameObject("VisualNovel").AddComponent<VisualNovel>();
        if (backdropKey != null)
            vn.Backdrop = Resources.Load<Texture2D>("Dialogue/bg_" + backdropKey);
        Current = vn;
        return vn;
    }

    // A speaker's portrait from their model (or their illustration, if one is in Resources/Dialogue)
    public VNPortrait Portrait(Side side, string key, GameObject model, Color rim)
    {
        if (portraits.TryGetValue(side, out VNPortrait old)) old.Dispose();
        var p = VNPortrait.Create(key, model, side == Side.Right ? -28f : 28f, rim);
        portraits[side] = p;
        return p;
    }

    public VNPortrait PortraitAt(Side side) => portraits.TryGetValue(side, out VNPortrait p) ? p : null;

    // One line. speaker null = narration (no name plate). side: who is speaking (lit), the other dims.
    public IEnumerator Say(string who, string line, Color color, Side? side = null, bool emphasis = false)
    {
        speaker = who;
        text = line;
        nameColor = color;
        narration = who == null;
        big = emphasis;
        shown = 0f;
        advance = false;
        foreach (var kv in portraits)
            kv.Value.Speaking = side.HasValue ? kv.Key == side.Value : false;
        if (Skipping)
            yield break;
        if (emphasis) Shake = Mathf.Max(Shake, 6f);
        bool voiced = Narration.Say(line) > 0f;   // read aloud (a line without a recording ticks as it types)
        float nextBlip = 0f;

        // Letters come out; a press shows the rest at once
        waiting = true;
        while (shown < text.Length)
        {
            if (Skipping) yield break;
            bool fast = Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
            shown += Time.unscaledDeltaTime * (fast ? 200f : big ? 22f : 34f);
            if (!voiced && !fast && Time.unscaledTime > nextBlip) { nextBlip = Time.unscaledTime + 0.075f; GameAudio.Play("ui_type"); }
            if (advance) { shown = text.Length; advance = false; }
            yield return null;
        }
        // Then wait for the next press (Ctrl runs on by itself)
        float held = 0f;
        while (!advance)
        {
            if (Skipping) yield break;
            bool fast = Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
            held += Time.unscaledDeltaTime;
            if (fast && held > 0.12f) break;
            yield return null;
        }
        advance = false;
        waiting = false;
        GameAudio.Play("ui_move");
    }

    // A pause in the talk while something happens on screen
    public IEnumerator Beat(float seconds)
    {
        if (Skipping) yield break;
        speaker = text = null;
        for (float t = 0f; t < seconds && !Skipping; t += Time.unscaledDeltaTime)
            yield return null;
    }

    public void Close() { closing = 0f; Narration.Stop(); }

#if UNITY_EDITOR
    // Test hook: the 건너뛰기 button, pressed from a script
    public void EditorSkip() { Skipping = true; Narration.Stop(); }
#endif

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        if ((kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            || (mouse != null && mouse.leftButton.wasPressedThisFrame && !OverSkip(mouse.position.ReadValue())))
            advance = true;

        float dt = Time.unscaledDeltaTime;
        Shake = Mathf.MoveTowards(Shake, 0f, dt * 18f);
        foreach (var p in portraits.Values)
            p.Tick(dt);
        if (closing >= 0f)
        {
            closing += dt;
            boxAlpha = Mathf.Clamp01(1f - closing / 0.5f);
            foreach (var p in portraits.Values) p.Alpha = Mathf.Min(p.Alpha, boxAlpha);
            Dim = Mathf.Min(Dim, boxAlpha);
            Tint.a = Mathf.Min(Tint.a, boxAlpha);
            if (closing > 0.55f) Destroy(gameObject);
        }
        else
            boxAlpha = Mathf.MoveTowards(boxAlpha, 1f, dt * 3f);
    }

    private void OnDestroy()
    {
        foreach (var p in portraits.Values) p.Dispose();
        if (Current == this) Current = null;
    }

    private Rect SkipRect => new Rect(Screen.width - 168f, 18f, 150f, 38f);
    private bool OverSkip(Vector2 mouse) => SkipRect.Contains(new Vector2(mouse.x, Screen.height - mouse.y));

    private void OnGUI()
    {
        GUI.depth = -150;
        float w = Screen.width, h = Screen.height, k = h / 720f;
        Styles(k);
        Vector2 jolt = Shake > 0f ? Random.insideUnitCircle * Shake * k : Vector2.zero;

        // The backdrop: an illustration, or the scene behind darkened
        if (Backdrop != null)
            GUI.DrawTexture(new Rect(jolt.x - 10f, jolt.y - 10f, w + 20f, h + 20f), Backdrop, ScaleMode.ScaleAndCrop);
        if (Dim > 0f)
            UiFont.Fill(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, Dim));
        if (Tint.a > 0f)
            UiFont.Fill(new Rect(0f, 0f, w, h), Tint);

        // The portraits, standing behind the box
        foreach (var kv in portraits)
            kv.Value.Draw(kv.Key, w, h, jolt);

        if (boxAlpha <= 0f)
            return;
        // The dialogue box along the bottom, a gold rule along its top
        Rect box = new Rect(w * 0.06f + jolt.x * 0.3f, h * 0.71f + jolt.y * 0.3f, w * 0.88f, h * 0.25f);
        UiFont.Fill(box, new Color(0.04f, 0.035f, 0.06f, 0.88f * boxAlpha));
        UiFont.Fill(new Rect(box.x, box.y, box.width, 2f * k), new Color(0.95f, 0.78f, 0.4f, 0.9f * boxAlpha));
        UiFont.Fill(new Rect(box.x, box.yMax - 1f, box.width, 1f), new Color(0.95f, 0.78f, 0.4f, 0.4f * boxAlpha));

        if (!string.IsNullOrEmpty(speaker))
        {
            Vector2 size = nameStyle.CalcSize(new GUIContent(speaker));
            Rect plate = new Rect(box.x + 26f * k, box.y - 34f * k, size.x + 40f * k, 36f * k);
            UiFont.Fill(plate, new Color(nameColor.r * 0.35f, nameColor.g * 0.35f, nameColor.b * 0.35f, 0.95f * boxAlpha));
            UiFont.Fill(new Rect(plate.x, plate.y, 4f * k, plate.height), new Color(nameColor.r, nameColor.g, nameColor.b, boxAlpha));
            nameStyle.normal.textColor = new Color(1f, 1f, 1f, boxAlpha);
            GUI.Label(plate, speaker, nameStyle);
        }
        if (!string.IsNullOrEmpty(text))
        {
            string visible = text.Substring(0, Mathf.Clamp(Mathf.FloorToInt(shown), 0, text.Length));
            GUIStyle style = narration ? narrationStyle : big ? bigStyle : textStyle;
            Rect inner = new Rect(box.x + 34f * k, box.y + 22f * k, box.width - 68f * k, box.height - 44f * k);
            style.normal.textColor = narration ? new Color(0.78f, 0.76f, 0.72f, boxAlpha) : new Color(0.97f, 0.95f, 0.9f, boxAlpha);
            GUI.Label(inner, narration ? visible : $"“{visible}”", style);
            // The next marker once the line is out
            if (shown >= text.Length && waiting && Mathf.Repeat(Time.unscaledTime, 1f) < 0.65f)
            {
                hintStyle.normal.textColor = new Color(0.95f, 0.78f, 0.4f, boxAlpha);
                GUI.Label(new Rect(box.xMax - 60f * k, box.yMax - 42f * k, 40f * k, 30f * k), "▼", hintStyle);
            }
        }
        hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f * boxAlpha);
        GUI.Label(new Rect(box.x + 30f * k, box.yMax - 30f * k, 520f * k, 24f * k), "[Space · 클릭] 다음   [Ctrl] 빨리 넘기기", hintStyle);

        // Skip the whole conversation
        if (!Skipping && closing < 0f && GUI.Button(SkipRect, "건너뛰기 ▶▶", skipStyle))
        {
            Skipping = true;
            Narration.Stop();
            GameAudio.Play("ui_back");
        }
    }

    private void Styles(float k)
    {
        if (nameStyle == null)
        {
            nameStyle = UiFont.Style(20, TextAnchor.MiddleCenter, FontStyle.Bold);
            textStyle = UiFont.Style(24, TextAnchor.UpperLeft);
            textStyle.wordWrap = true;
            bigStyle = UiFont.Style(34, TextAnchor.UpperLeft, FontStyle.Bold);
            bigStyle.wordWrap = true;
            narrationStyle = UiFont.Style(22, TextAnchor.UpperLeft, FontStyle.Italic);
            narrationStyle.wordWrap = true;
            hintStyle = UiFont.Style(14, TextAnchor.MiddleLeft);
            skipStyle = new GUIStyle(GUI.skin.button) { font = UiFont.Get(), fontSize = 16, fontStyle = FontStyle.Bold };
        }
        nameStyle.fontSize = Mathf.RoundToInt(20f * k);
        textStyle.fontSize = Mathf.RoundToInt(24f * k);
        bigStyle.fontSize = Mathf.RoundToInt(34f * k);
        narrationStyle.fontSize = Mathf.RoundToInt(22f * k);
        hintStyle.fontSize = Mathf.RoundToInt(14f * k);
    }
}

// One speaker's portrait. From their model: a copy posed far from the world on its own layer, lit by its own key and
// rim lights, filmed into a texture with a clear background. The shot moves between the full figure (0) and the face
// (1); the figure can be tinted, wreathed in dark smoke, flash its eyes, and play its animator's states.
// From an illustration (Resources/Dialogue/<key>_<expression>.png): just that picture, swapped by expression.
public class VNPortrait
{
    private const int Layer = 31;
    private static int slots;

    public float Alpha;               // shown 0..1 (fades/slides in)
    public bool Speaking = true;      // lit when speaking, dimmed while the other talks
    public float Shake;
    public float Zoom;                // 0 full figure .. 1 face (eased toward ZoomTarget)
    public float ZoomTarget;
    public float AlphaTarget = 1f;

    private readonly string key;
    private Texture illustration;
    private GameObject stage;
    private Camera cam;
    private RenderTexture rt;
    private Transform figure, head;
    private Animator animator;
    private Light keyLight, rimLight;
    private Bounds bounds;
    private float lit = 1f;
    private Renderer[] renderers;
    private Color[] baseColors;
    private ParticleSystem smoke;
    private GameObject eyes;

    private VNPortrait(string key) { this.key = key; }

    public Texture Texture => illustration != null ? illustration : rt;
    public Transform Head => head;

    public static VNPortrait Create(string key, GameObject model, float yaw, Color rim)
    {
        var p = new VNPortrait(key);
        p.illustration = Resources.Load<Texture2D>("Dialogue/" + key + "_neutral") ?? Resources.Load<Texture2D>("Dialogue/" + key);
        if (p.illustration == null && model != null)
            p.Build(model, yaw, rim);
        return p;
    }

    private void Build(GameObject model, float yaw, Color rim)
    {
        int slot = slots++;
        stage = new GameObject("VNPortrait_" + key);
        stage.transform.position = new Vector3(20000f + slot * 60f, 5000f, 20000f);

        // The figure: a copy with nothing in it that would act, only its renderers and animator
        var hold = new GameObject("hold");
        hold.SetActive(false);
        GameObject copy = Object.Instantiate(model, hold.transform);
        foreach (var b in copy.GetComponentsInChildren<Behaviour>(true))
            if (!(b is Animator) && !(b is Animation)) Object.DestroyImmediate(b);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        copy.transform.SetParent(stage.transform, false);
        Object.Destroy(hold);
        copy.SetActive(true);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);   // facing +z, toward the camera, turned a little to the screen's middle
        copy.transform.localScale = model.transform.lossyScale;
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        figure = copy.transform;
        animator = copy.GetComponentInChildren<Animator>();
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLower().EndsWith("head")) { head = t; break; }

        renderers = copy.GetComponentsInChildren<Renderer>();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            baseColors[i] = renderers[i].material.HasProperty("_BaseColor") ? renderers[i].material.GetColor("_BaseColor") : Color.white;
        bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(stage.transform.position, Vector3.one * 2f);
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        // Lights: a key from the front side, a coloured rim from behind
        keyLight = NewLight("Key", new Vector3(1.5f, 2.2f, 3f), new Color(1f, 0.96f, 0.9f), 2.2f);
        rimLight = NewLight("Rim", new Vector3(-1.5f, 2.5f, -2.5f), rim, 3.2f);

        rt = new RenderTexture(768, 1024, 24, RenderTextureFormat.ARGB32) { name = "VN_" + key };
        cam = new GameObject("Camera").AddComponent<Camera>();
        cam.transform.SetParent(stage.transform, false);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.cullingMask = 1 << Layer;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 50f;
        cam.targetTexture = rt;
        cam.aspect = 0.75f;
        Aim();
    }

    private Light NewLight(string name, Vector3 local, Color color, float intensity)
    {
        var l = new GameObject(name).AddComponent<Light>();
        l.transform.SetParent(stage.transform, false);
        float s = Mathf.Max(1f, bounds.size.y / 2f);
        l.transform.localPosition = local * s;
        l.type = LightType.Point;
        l.range = 12f * s;
        l.color = color;
        l.intensity = intensity;
        l.cullingMask = 1 << Layer;
        return l;
    }

    // From the full figure to the face
    private void Aim()
    {
        if (cam == null) return;
        Vector3 center = bounds.center;
        float height = bounds.size.y;
        Vector3 face = head != null ? head.position + Vector3.up * height * 0.02f : center + Vector3.up * height * 0.38f;
        float fullDistance = height * 0.6f / Mathf.Tan(15f * Mathf.Deg2Rad);
        float faceDistance = height * 0.34f / Mathf.Tan(15f * Mathf.Deg2Rad);   // head and shoulders
        float z = Mathf.SmoothStep(0f, 1f, Zoom);
        Vector3 look = Vector3.Lerp(center + Vector3.up * height * 0.04f, face - Vector3.up * height * 0.08f, z);
        float distance = Mathf.Lerp(fullDistance, faceDistance, z);
        Vector3 jitter = Shake > 0f ? Random.insideUnitSphere * Shake * 0.02f * height : Vector3.zero;
        cam.transform.position = look + Vector3.forward * distance + jitter;   // in front of the figure (it faces +z)
        cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position);
    }

    public void Tick(float dt)
    {
        Alpha = Mathf.MoveTowards(Alpha, AlphaTarget, dt * 2.5f);
        Zoom = Mathf.MoveTowards(Zoom, ZoomTarget, dt * 0.9f);
        Shake = Mathf.MoveTowards(Shake, 0f, dt * 2f);
        lit = Mathf.MoveTowards(lit, Speaking ? 1f : 0.45f, dt * 3f);
        if (flicker > 0f)
        {
            flicker = Mathf.MoveTowards(flicker, 0f, dt * 1.2f);
            if (rimLight != null) rimLight.color = Color.Lerp(rimLight.color, flickerColor, flicker);
        }
        Aim();
    }

    // ── Expressions ──

    public void Play(string state, float fade = 0.15f)
    {
        if (animator != null && animator.isActiveAndEnabled)
            animator.CrossFadeInFixedTime(state, fade, -1);
    }

    public void Expression(string name)
    {
        var art = Resources.Load<Texture2D>("Dialogue/" + key + "_" + name);
        if (art != null) illustration = art;
    }

    // Tint toward a colour (1 = fully), e.g. the blight darkening him
    public void Darken(Color color, float amount)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null && renderers[i].material.HasProperty("_BaseColor"))
                renderers[i].material.SetColor("_BaseColor", Color.Lerp(baseColors[i], baseColors[i] * color, amount));
    }

    public void Lights(Color key, float keyIntensity, Color rim, float rimIntensity)
    {
        if (keyLight != null) { keyLight.color = key; keyLight.intensity = keyIntensity; }
        if (rimLight != null) { rimLight.color = rim; rimLight.intensity = rimIntensity; }
    }

    // Black smoke rising off the figure (the corruption showing through)
    public void Smoke(bool on)
    {
        if (stage == null) return;
        if (smoke == null)
        {
            var go = new GameObject("Smoke") { layer = Layer };
            go.transform.SetParent(stage.transform, false);
            go.transform.position = bounds.center;
            smoke = go.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(bounds.size.y * 0.1f, bounds.size.y * 0.2f);
            main.startColor = new Color(0.05f, 0.02f, 0.08f, 0.75f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // close round the body, so it stays inside the portrait's frame
            shape.scale = new Vector3(bounds.size.x * 0.35f, bounds.size.y * 0.6f, bounds.size.z * 0.35f);
            var vel = smoke.velocityOverLifetime;
            vel.enabled = true;
            // (all three axes in the same mode, as Unity requires)
            vel.x = new ParticleSystem.MinMaxCurve(-0.03f * bounds.size.y, 0.03f * bounds.size.y);
            vel.y = new ParticleSystem.MinMaxCurve(0.15f * bounds.size.y, 0.3f * bounds.size.y);
            vel.z = new ParticleSystem.MinMaxCurve(-0.03f * bounds.size.y, 0.03f * bounds.size.y);
            var col = smoke.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Vfx.Material("smoke", false);
        }
        var em = smoke.emission;
        em.rateOverTime = on ? 40f : 0f;
        if (on && !smoke.isPlaying) smoke.Play();
    }

    // A light in the eyes for a moment (black-violet when the blight stirs)
    public void EyeFlash(Color color, float size = 1f)
    {
        if (head == null || stage == null) return;
        if (eyes != null) Object.Destroy(eyes);
        // a glow just in front of the face, toward the camera
        eyes = Vfx.Glow(stage.transform, head.position - stage.transform.position + new Vector3(0f, bounds.size.y * 0.07f, bounds.size.y * 0.09f),
            bounds.size.y * 0.12f * size, color);
        foreach (Transform t in eyes.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        Object.Destroy(eyes, 0.9f);
        // and the whole figure darkens with it for a beat
        flicker = 1f;
        flickerColor = color;
    }

    private float flicker;
    private Color flickerColor;

    public void Draw(VisualNovel.Side side, float w, float h, Vector2 jolt)
    {
        Texture tex = Texture;
        if (tex == null || Alpha <= 0f) return;
        float ph = h * 0.9f, pw = ph * 0.75f;
        float slide = (1f - Alpha) * w * 0.08f;
        float x = side == VisualNovel.Side.Right ? w - pw - w * 0.02f + slide : w * 0.02f - slide;
        float scale = side == VisualNovel.Side.Right ? 1f : 0.82f;
        Rect r = new Rect(x + (side == VisualNovel.Side.Right ? 0f : 0f), h - ph * scale + jolt.y, pw * scale, ph * scale);
        if (side == VisualNovel.Side.Right) r.x = w - r.width - w * 0.02f + slide + jolt.x;
        float b = lit;
        GUI.color = new Color(b, b, b, Alpha);
        GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
        GUI.color = Color.white;
    }

    public void Dispose()
    {
        if (cam != null) cam.targetTexture = null;
        if (rt != null) { rt.Release(); Object.Destroy(rt); }
        if (stage != null) Object.Destroy(stage);
    }
}
