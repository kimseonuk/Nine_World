using UnityEngine;

// The mist giant's veil: for a few seconds a thick fog closes in over the arena. The world goes white a few metres
// past the player, mist drifts across the screen, and the wet cold drags at the player's legs (PlayerEffects.Bind).
// It runs on its own object, so the fog always lifts again even if the giant falls while it lasts.
public class MistVeil : MonoBehaviour
{
    private static MistVeil current;

    private float seconds, slow, age;
    private bool fogWas;
    private FogMode modeWas;
    private float startWas, endWas;
    private Color colourWas;
    private Transform player;
    private Material puffMat;
    private float puffTimer, bindTimer;

    private static readonly Color Fog = new Color(0.72f, 0.78f, 0.86f);
    private const float FadeIn = 1.2f, FadeOut = 1.5f;

    // A new veil replaces one still hanging (the fog settings it saved are kept)
    public static void Cast(float seconds, float slow)
    {
        if (current != null)
        {
            current.seconds = current.age + seconds;
            current.slow = slow;
            return;
        }
        var go = new GameObject("MistVeil");
        current = go.AddComponent<MistVeil>();
        current.seconds = seconds;
        current.slow = slow;
    }

    private void Awake()
    {
        fogWas = RenderSettings.fog;
        modeWas = RenderSettings.fogMode;
        startWas = RenderSettings.fogStartDistance;
        endWas = RenderSettings.fogEndDistance;
        colourWas = RenderSettings.fogColor;
        var mover = FindAnyObjectByType<PlayerMovement>();
        player = mover != null ? mover.transform : null;
        puffMat = Mats.Get("mist_veil_puff", Fog, 0.1f, Fog * 0.35f);
    }

    // 0 clear .. 1 thickest
    private float Thickness => Mathf.Clamp01(Mathf.Min(age / FadeIn, (seconds + FadeOut - age) / FadeOut));

    private void Update()
    {
        age += Time.deltaTime;
        var gm = GameManager.Instance;
        if (age >= seconds + FadeOut || (gm != null && (gm.CurrentState == GameManager.State.Dead || gm.CurrentState == GameManager.State.Victory)))
        {
            Destroy(gameObject);
            return;
        }
        float k = Thickness;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Color.Lerp(colourWas, Fog, k);
        // the camera sits ~11 m from the player: the fog thickens just past them, swallowing the edges of the arena
        RenderSettings.fogStartDistance = Mathf.Lerp(startWas, 4f, k);
        RenderSettings.fogEndDistance = Mathf.Lerp(endWas, 17f, k);

        // the cold mist drags at the player's legs while it is thick
        bindTimer -= Time.deltaTime;
        if (k > 0.5f && bindTimer <= 0f)
        {
            PlayerEffects.ApplyBind(slow, 0.6f);
            bindTimer = 0.5f;
        }
        // banks of mist (the Baroque fog art) rolling slowly past the player
        puffTimer -= Time.deltaTime;
        if (player != null && k > 0.2f && puffTimer <= 0f)
        {
            puffTimer = 0.28f;
            Vector3 at = player.position + new Vector3(Random.Range(-8f, 8f), Random.Range(0.4f, 1.8f), Random.Range(-5f, 8f));
            Vector3 drift = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-0.4f, 0.4f)).normalized * Random.Range(0.8f, 1.8f);
            if (MistVfx.Bank(at, drift, Random.Range(4f, 7f), 0.6f, Random.Range(0.8f, 1.6f), 0.9f, 0.55f) == null)
            {
                GameObject puff = Mats.Shape(PrimitiveType.Sphere, null, at, new Vector3(Random.Range(1.6f, 3f), Random.Range(0.6f, 1.2f), Random.Range(1.6f, 3f)), puffMat);
                Destroy(puff, Random.Range(0.8f, 1.4f));
            }
        }
    }

    // A pale wash over the screen, thickest at its edges
    private void OnGUI()
    {
        float k = Thickness;
        if (k <= 0f)
            return;
        Color wash = new Color(Fog.r, Fog.g, Fog.b, 0.22f * k);
        UiFont.Fill(new Rect(0f, 0f, Screen.width, Screen.height), wash);
        float edge = Mathf.Min(Screen.width, Screen.height) * 0.16f;
        Color rim = new Color(Fog.r, Fog.g, Fog.b, 0.35f * k);
        UiFont.Fill(new Rect(0f, 0f, Screen.width, edge), rim);
        UiFont.Fill(new Rect(0f, Screen.height - edge, Screen.width, edge), rim);
        UiFont.Fill(new Rect(0f, edge, edge, Screen.height - edge * 2f), rim);
        UiFont.Fill(new Rect(Screen.width - edge, edge, edge, Screen.height - edge * 2f), rim);
    }

    private void OnDestroy()
    {
        RenderSettings.fog = fogWas;
        RenderSettings.fogMode = modeWas;
        RenderSettings.fogStartDistance = startWas;
        RenderSettings.fogEndDistance = endWas;
        RenderSettings.fogColor = colourWas;
        if (current == this)
            current = null;
    }
}

// The mist giant's fog art (Resources/VFX/MistSurge, made in Baroque 3D): a rolling wave of spectral vapour drawn as a
// soft camera-facing sheet that drifts, swells and thins away
public static class MistVfx
{
    private static Material surge, poison;
    private static Material Surge => surge != null ? surge : surge = Resources.Load<Material>("VFX/MistSurge");
    private static Material Poison => poison != null ? poison : poison = Resources.Load<Material>("VFX/PoisonBurst");

    public static readonly Color PoisonTint = new Color(0.55f, 0.95f, 0.45f);
    public static readonly Color VenomTint = new Color(0.75f, 0.5f, 1f);

    // A bank of fog at `at`, drifting by `velocity` (m/s), swelling from 60% to full `size` over `grow`, then thinning out
    public static GameObject Bank(Vector3 at, Vector3 velocity, float size, float grow, float hold, float fade, float opacity = 1f, Color? tint = null)
        => Sheet(Surge, at, velocity, size, grow, hold, fade, opacity, tint ?? Color.white, false);

    // A burst of venom (the Baroque poison art: emerald and violet vapour with glowing droplets), glowing additively
    public static GameObject Venom(Vector3 at, Vector3 velocity, float size, float grow, float hold, float fade, float opacity = 1f)
        => Sheet(Poison, at, velocity, size, grow, hold, fade, opacity, Color.white, true);

    private static GameObject Sheet(Material mat, Vector3 at, Vector3 velocity, float size, float grow, float hold, float fade, float opacity, Color tint, bool round)
    {
        if (mat == null)
            return null;
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = round ? "VenomBurst" : "MistBank";
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.transform.position = at;
        go.AddComponent<MistBillboard>().Init(velocity, size, grow, hold, fade, opacity, tint, round);
        return go;
    }
}

public class MistBillboard : MonoBehaviour
{
    private Vector3 velocity;
    private float size, grow, hold, fade, opacity, age, roll, aspect, spin;
    private bool flip;
    private Color tint = Color.white;
    private Renderer rend;
    private MaterialPropertyBlock block;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public void Init(Vector3 drift, float fullSize, float growTime, float holdTime, float fadeTime, float alpha, Color colour, bool round)
    {
        velocity = drift; size = fullSize; grow = growTime; hold = holdTime; fade = fadeTime; opacity = alpha; tint = colour;
        if (round)
        {
            // a radial burst: any turn, square, slowly turning
            roll = Random.Range(0f, 360f);
            aspect = 1f;
            spin = Random.Range(-40f, 40f);
            flip = Random.value < 0.5f;
        }
        else
        {
            roll = Random.Range(-12f, 12f);
            flip = velocity.x < 0f ? Random.value < 0.8f : Random.value < 0.2f;   // the wave rolls the way it drifts
            aspect = Random.Range(0.55f, 0.75f);
        }
        rend = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        Apply();
    }

    private void LateUpdate()
    {
        age += Time.deltaTime;
        if (age >= grow + hold + fade)
        {
            Destroy(gameObject);
            return;
        }
        transform.position += velocity * Time.deltaTime;
        roll += spin * Time.deltaTime;
        velocity *= 1f - 0.6f * Time.deltaTime;   // it slows as it spreads
        Apply();
    }

    private void Apply()
    {
        float t = age;
        float s = t < grow ? Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.001f, grow))) : 1f + (t - grow) * 0.06f;
        float a = t < grow ? t / Mathf.Max(0.001f, grow) : t < grow + hold ? 1f : 1f - (t - grow - hold) / Mathf.Max(0.001f, fade);
        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, roll);
        transform.localScale = new Vector3(size * s * (flip ? -1f : 1f), size * s * aspect, 1f);
        rend.GetPropertyBlock(block);
        block.SetColor(BaseColor, new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(a) * opacity));
        rend.SetPropertyBlock(block);
    }
}
