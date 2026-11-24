using UnityEngine;

// The swirl of light standing in a zone's doorway (VARCO's portal ring, Resources/Vfx/portal_swirl): red and still
// while the zone is sealed by its fight, blue and turning once the way is open. It only shows the state ZoneExit
// already goes by (room.IsFighting); the seal's collider still does the blocking.
public class ExitPortal : MonoBehaviour
{
    public const string Texture = "portal_swirl";
    private static readonly Color Open = new Color(0.4f, 1.05f, 2.4f);
    private static readonly Color Shut = new Color(1.6f, 0.12f, 0.08f);
    private static readonly Color OpenCore = new Color(0.07f, 0.12f, 0.42f, 0.93f);
    private static readonly Color ShutCore = new Color(0.32f, 0.03f, 0.04f, 0.93f);
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private Room room;
    private Transform ringA, ringB, core, inner, haze;
    private Renderer ringARend, ringBRend, coreRend, innerRend, hazeRend;
    private Light glow;
    private MaterialPropertyBlock block;
    private float open;      // 0 sealed .. 1 open, eased
    private float strength;  // dimmer on the camera's side, where it stands between the camera and the player
    private Vector3 baseScale;

    public static bool Available => Resources.Load<Texture2D>("Vfx/" + Texture) != null;

    // A portal filling a doorway: centred at `centre` (local to `parent`), facing out along `normal`, `width` across and
    // `height` tall. Sized so its bright rim meets the door frame's inner edge; set inside the frame's depth, the frame's
    // posts and lintel overlap its outer wisps, so it reads as filling the door rather than standing in front of it.
    public static ExitPortal Create(Transform parent, Room room, Vector3 normal, Vector3 centre, float width, float height, float strength)
    {
        var go = new GameObject("ExitPortal");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre;
        go.transform.localRotation = Quaternion.LookRotation(normal);
        go.transform.localScale = new Vector3(width, height, 1f);
        var p = go.AddComponent<ExitPortal>();
        p.room = room;
        p.strength = strength;
        p.baseScale = go.transform.localScale;
        p.block = new MaterialPropertyBlock();
        // The deep fill, a luminous vortex turning inside it, then the two rim rings turning against each other
        p.core = p.Layer("Core", "glow", false, 0.92f, 0.03f, out p.coreRend);
        p.coreRend.sharedMaterial = CoreMaterial;
        p.inner = p.Layer("Inner", Texture, true, 0.6f, 0.02f, out p.innerRend);
        p.haze = p.Layer("Haze", "glow", true, 0.85f, 0.025f, out p.hazeRend);
        p.ringA = p.Layer("RingA", Texture, true, 1f, 0f, out p.ringARend);
        p.ringB = p.Layer("RingB", Texture, true, 0.86f, -0.01f, out p.ringBRend);
        p.ringB.localRotation = Quaternion.Euler(0f, 0f, 140f);

        var light = new GameObject("PortalLight");
        light.transform.SetParent(go.transform, false);
        light.transform.localPosition = new Vector3(0f, 0f, -0.6f);
        p.glow = light.AddComponent<Light>();
        p.glow.type = LightType.Point;
        p.glow.range = 7f;
        p.glow.shadows = LightShadows.None;

        p.open = room != null && room.IsFighting ? 0f : 1f;
        p.Paint(Time.time);
        return p;
    }

    // The dark well: an alpha-faded soft disc (the shared "glow" art is additive, its alpha solid, so it'd show a square)
    private static Material coreMaterial;
    private static Material CoreMaterial
    {
        get
        {
            if (coreMaterial != null)
                return coreMaterial;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "portal_core", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - r) * 4f));   // solid to ~3/4 out, then soft
                }
            tex.SetPixels(px);
            tex.Apply();
            coreMaterial = new Material(Vfx.Material("glow", false)) { name = "portal_core" };
            coreMaterial.SetTexture("_BaseMap", tex);
            return coreMaterial;
        }
    }

    private Transform Layer(string name, string texture, bool additive, float size, float depth, out Renderer rend)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, depth);
        go.transform.localScale = Vector3.one * size;
        go.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
        rend = go.AddComponent<MeshRenderer>();
        rend.sharedMaterial = Vfx.Material(texture, additive);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        return go.transform;
    }

    private AudioSource hum;

    private void Update()
    {
        if (ringA == null || ringB == null || glow == null) return;   // (a script reload in play mode drops the links)
        bool usable = room == null || !room.IsFighting;
        open = Mathf.MoveTowards(open, usable ? 1f : 0f, Time.deltaTime / 0.5f);
        if (hum == null) hum = GameAudio.Loop(gameObject, "portal_loop", 0f, 10f, 1.1f);
        if (hum != null) hum.volume = 0.35f * open * GameAudio.Volume;
        float t = Time.time;
        if (usable)
        {
            // Open: the rings wheel round against each other and the whole gate breathes
            ringA.Rotate(0f, 0f, -55f * Time.deltaTime, Space.Self);
            ringB.Rotate(0f, 0f, 85f * Time.deltaTime, Space.Self);
            if (inner != null) inner.Rotate(0f, 0f, -150f * Time.deltaTime, Space.Self);   // the vortex at its heart
            float breathe = 1f + 0.02f * Mathf.Sin(t * 3.1f);
            transform.localScale = new Vector3(baseScale.x * breathe, baseScale.y * breathe, 1f);
        }
        else
            transform.localScale = baseScale;
        Paint(t);
    }

    private void Paint(float t)
    {
        Color ring = Color.Lerp(Shut, Open, open);
        float flicker = open > 0.5f ? 0.85f + 0.15f * Mathf.Sin(t * 7.3f) * Mathf.Sin(t * 2.3f) : 0.7f;
        Set(ringARend, ring * strength * flicker);
        Set(ringBRend, ring * strength * 0.6f * flicker);
        Color core = Color.Lerp(ShutCore, OpenCore, open);
        core.a *= strength;
        Set(coreRend, core);
        // The fill's own light: a vortex and a soft haze, stirring while open, a dull ember glow while sealed
        float stir = open > 0.5f ? 0.8f + 0.2f * Mathf.Sin(t * 2.2f) : 0.55f;
        Set(innerRend, ring * strength * 0.45f * stir);
        Set(hazeRend, ring * strength * 0.28f * stir);
        glow.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.45f, 0.75f, 1f), open);
        glow.intensity = (open > 0.5f ? 2.6f : 1.6f) * flicker * strength;
    }

    private void Set(Renderer r, Color c)
    {
        if (r == null) return;
        block ??= new MaterialPropertyBlock();   // (lost on a script reload in play mode)
        r.GetPropertyBlock(block);
        block.SetColor(BaseColor, c);
        r.SetPropertyBlock(block);
    }
}
