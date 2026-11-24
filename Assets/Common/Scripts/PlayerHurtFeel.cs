using System.Collections;
using UnityEngine;

// What a monster's blow does to the player, so it lands as a threat: the screen's edges flush red (deeper for a heavy
// blow), the body flashes, the player is shoved back along the blow, the view punches that way and the action freezes,
// a red number of what was lost pops off the player, and the hit itself sounds — flesh, armour and, for a heavy one, a
// deep thud. At low health the edges keep pulsing red with the heartbeat. (EnemyFeel.PlayerHit calls Hit.)
public class PlayerHurtFeel : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private PlayerStats stats;
    private CharacterController controller;
    private float flash, flashPeak = 0.01f;
    private float bodyFlashUntil;
    private Coroutine shove, body;
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private Texture2D vignette;

    public static PlayerHurtFeel Of(PlayerStats p)
    {
        if (p == null) return null;
        var f = p.GetComponent<PlayerHurtFeel>();
        return f != null ? f : p.gameObject.AddComponent<PlayerHurtFeel>();
    }

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();
        controller = GetComponent<CharacterController>();
    }

    // weight: 0 a graze .. 1 a quarter of the player's health or more
    public void Hit(Enemy source, float taken, float weight)
    {
        Vector3 from = source != null ? source.transform.position : transform.position - transform.forward;
        Vector3 dir = transform.position - from;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
        dir.Normalize();
        bool heavy = weight > 0.45f;

        // the screen's edges flush red
        flash = Mathf.Max(flash, Mathf.Lerp(0.45f, 1f, weight));
        flashPeak = flash;

        // the body flashes red-white, then red, then back
        bodyFlashUntil = Time.unscaledTime + (heavy ? 0.22f : 0.14f);
        if (body == null) body = StartCoroutine(BodyFlash());

        // shoved back along the blow (not through walls: the controller stops it), a little lifted off its feet
        if (controller != null && controller.enabled)
        {
            if (shove != null) StopCoroutine(shove);
            shove = StartCoroutine(Shove(dir, Mathf.Lerp(0.35f, 1.3f, weight), heavy ? 0.2f : 0.13f));
        }

        // the view punches the way the blow went
        CameraShake.Kick(dir, Mathf.Lerp(0.12f, 0.35f, weight));

        // what was lost, in red over the player
        DamagePopups.Show(transform.position + Vector3.up * 2f, Mathf.Max(1, Mathf.RoundToInt(taken)), heavy ? DamagePopups.Style.HurtHeavy : DamagePopups.Style.Hurt);

        // the blow's own sounds: it lands on flesh and mail; a heavy one booms
        GameAudio.Play("hit_flesh", null, 0.7f + 0.3f * weight, 0.9f);
        GameAudio.Play("hit_metal", null, 0.35f + 0.25f * weight, 0.8f);
        if (heavy)
        {
            GameAudio.Play("hitheavy", null, 0.9f, 0.7f);
            GameAudio.Play("slam", null, 0.45f, 0.75f);
        }

        // a bright burst of red at the chest
        Vfx.Burst(transform.position + Vector3.up * 1f - dir * 0.2f, new Color(1f, 0.25f, 0.2f), heavy ? 22 : 12, heavy ? 7f : 5f, heavy ? 0.2f : 0.15f, 1.5f);
    }

    // A small tick of harm (fire underfoot, a stream of small shots): only a touch of red and the number
    public void Graze(float taken)
    {
        flash = Mathf.Max(flash, 0.3f);
        DamagePopups.Show(transform.position + Vector3.up * 2f, Mathf.Max(1, Mathf.RoundToInt(taken)), DamagePopups.Style.Hurt);
    }

    private IEnumerator Shove(Vector3 dir, float distance, float seconds)
    {
        float moved = 0f;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            if (controller == null || !controller.enabled || (stats != null && stats.IsDead)) break;
            // eased out: most of it at once, then sliding to a stop
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01((t + Time.unscaledDeltaTime) / seconds), 3f);
            float step = distance * k - moved;
            moved += step;
            controller.Move(dir * step);
            yield return null;
        }
        shove = null;
    }

    private IEnumerator BodyFlash()
    {
        while (Time.unscaledTime < bodyFlashUntil)
        {
            float left = bodyFlashUntil - Time.unscaledTime;
            // white-hot first, then a deep red as it fades
            Tint(left > 0.09f ? new Color(3f, 1.6f, 1.5f) : new Color(1.6f, 0.45f, 0.4f));
            yield return null;
        }
        Tint(Color.white);
        body = null;
    }

    private void Tint(Color color)
    {
        // the fighter shown now (the others are hidden), its own meshes only
        renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(false),
            r => (r is SkinnedMeshRenderer || r is MeshRenderer) && !r.name.StartsWith("Vfx") && !r.name.StartsWith("Sprite_"));
        block ??= new MaterialPropertyBlock();
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            if (color == Color.white) block.Clear();
            else block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(block);
        }
    }

    private void Update()
    {
        flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime * 1.6f);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || stats == null || !GameManager.IsRunActive) return;
        if (TitleScreen.Playing || OpeningCutscene.Playing || VisualNovel.Playing || BossCutscene.Playing) return;
        float low = stats.MaxHealth > 0f ? stats.Health / stats.MaxHealth : 1f;
        // the heartbeat's red at low health: stronger the lower it is, beating twice a second
        float beat = 0f;
        if (!stats.IsDead && low < 0.3f)
        {
            float k = 1f - low / 0.3f;
            float t = Time.unscaledTime * 2.1f;
            float pulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f)), 6f) + 0.6f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin((t - 0.18f) * Mathf.PI * 2f)), 6f);
            beat = (0.18f + 0.35f * pulse) * k;
        }
        float a = Mathf.Max(flash * 0.85f, beat);
        if (a < 0.01f) return;
        vignette ??= MakeVignette();
        GUI.depth = 4;
        GUI.color = new Color(0.85f, 0.05f, 0.05f, a);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill);
        // a heavy blow tints the whole screen for an instant
        if (flash > 0.75f)
        {
            GUI.color = new Color(1f, 0.15f, 0.1f, (flash - 0.75f) * 0.5f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
    }

    // Clear in the middle, red toward the edges
    private static Texture2D MakeVignette()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u * 0.8f + v * v);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, d));
                px[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private void OnDisable()
    {
        if (body != null) { StopCoroutine(body); body = null; }
        Tint(Color.white);
    }
}
