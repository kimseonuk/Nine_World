using System.Collections;
using UnityEngine;

// A boss's phase change plays out as a short cutscene: the fight holds still (no input, no damage),
// black bars close in, the camera swings down low to the boss for its transformation, a title card
// names what it has become, then everything hands back to the player.
// The boss supplies the scene itself (its animation and effects) as a routine; this runs the framing around it.
public class BossCutscene : MonoBehaviour
{
    public static bool Playing => current != null;
    private static BossCutscene current;

    // Where the follow camera should be while a cutscene holds it (Weight 0 = not at all)
    public static float CameraWeight { get; private set; }
    public static Vector3 CameraPosition { get; private set; }
    public static Quaternion CameraRotation { get; private set; }
    public static float CameraFov { get; private set; } = 40f;

    private const float Pitch = 22f;        // looking down just enough that the frame stays below the arena walls
    private const float Fov = 40f;

    private Transform subject;
    private float height;                   // the boss's height, for framing
    private Vector3 side;                   // the direction the camera stands in, seen from the boss
    private float orbit, dolly = 1f, dollyTarget = 1f;
    private float bars;                     // letterbox, 0..1
    private string title, subtitle;
    private Color accent = Color.white;
    private float titleTime = -1f;          // cutscene time the title card came up
    private float flash, time;
    private Vector3 look;
    private bool hitStop;

    // Run a boss's phase-change scene. height: how tall the boss stands (frames the shot).
    // hitStop: open on a beat of slow motion (a phase change set off by a blow; not an entrance)
    public static void Play(Transform boss, float height, IEnumerator scene, bool hitStop = true)
    {
        GameAudio.Play("boss_sting");
        var host = new GameObject("BossCutscene").AddComponent<BossCutscene>();
        host.subject = boss;
        host.height = Mathf.Max(1.5f, height);
        host.hitStop = hitStop;
        host.StartCoroutine(host.Run(scene));
    }

    // A boss without an entrance of its own: it stands its ground while its name comes up
    public static IEnumerator NameCard(string title, string subtitle, Color accent)
    {
        yield return new WaitForSeconds(0.7f);
        Title(title, subtitle, accent);
        Dolly(0.85f);
        yield return new WaitForSeconds(1.6f);
    }

    // How tall a body stands, from its renderers
    public static float HeightOf(Transform body)
    {
        float top = body.position.y;
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
            top = Mathf.Max(top, r.bounds.max.y);
        return top - body.position.y;
    }

    // Called by the scene at its climax: the name of the new phase
    public static void Title(string title, string subtitle, Color accent)
    {
        if (current == null)
            return;
        current.title = title;
        current.subtitle = subtitle;
        GameAudio.Play("realm_enter", null, 0.9f, 0.85f);
        current.accent = accent;
        current.titleTime = current.time;
    }

    // A white flash over the whole screen (the burst of a transformation)
    public static void Flash(float strength)
    {
        if (current != null)
            current.flash = Mathf.Max(current.flash, strength);
        if (strength >= 0.5f) GameAudio.Play("boss_awaken");
    }

    // Push the camera in (below 1) or out (above 1) for a beat
    public static void Dolly(float distance)
    {
        if (current != null)
            current.dollyTarget = distance;
    }

    private IEnumerator Run(IEnumerator scene)
    {
        current = this;
        ClearTheField();
        Camera cam = Camera.main;
        CameraFov = Fov;

        // Stand the camera on the open side of the boss: towards the arena's middle, or failing that the player
        Room room = GameManager.Instance != null ? GameManager.Instance.CurrentRoom : null;
        Vector3 toward = room != null ? room.transform.position - subject.position : Vector3.zero;
        toward.y = 0f;
        if (toward.magnitude < 3f && cam != null)
        {
            toward = cam.transform.position - subject.position;
            toward.y = 0f;
        }
        side = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.back;
        orbit = -12f;
        // The boss turns to face the camera (and so the player's eye) for its moment
        subject.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, orbit + 20f, 0f) * side);

        // A beat of slow motion as the blow that triggered it lands
        if (hitStop)
            StartCoroutine(HitStop());

        // Bars in and camera down, while the scene starts
        StartCoroutine(Blend(0f, 1f, 0.9f));
        var steps = new System.Collections.Generic.Stack<IEnumerator>();
        steps.Push(scene);
        while (steps.Count > 0)
        {
            IEnumerator top = steps.Peek();
            if (!top.MoveNext())
            {
                steps.Pop();
                continue;
            }
            if (top.Current is IEnumerator nested)
                steps.Push(nested);
            else
                yield return top.Current;
        }

        // Let the title card read, then hand back
        float hold = titleTime >= 0f ? Mathf.Max(0.4f, 2.4f - (time - titleTime)) : 0.4f;
        yield return new WaitForSeconds(hold);
        yield return Blend(1f, 0f, 0.8f);
        CameraWeight = 0f;
        current = null;
        Destroy(gameObject);
    }

    // Anything mid-flight would hit the player the moment the cutscene ends: clear it away
    private static void ClearTheField()
    {
        foreach (Projectile p in FindObjectsByType<Projectile>())
            Destroy(p.gameObject);
        foreach (SonicRing r in FindObjectsByType<SonicRing>())
            Destroy(r.gameObject);
        PlayerEffects.Clear();
    }

    private IEnumerator HitStop()
    {
        if (Time.timeScale != 1f)
            yield break;
        Time.timeScale = 0.12f;
        yield return new WaitForSecondsRealtime(0.35f);
        // Unless the game was paused meanwhile
        if (Mathf.Approximately(Time.timeScale, 0.12f))
            Time.timeScale = 1f;
    }

    private IEnumerator Blend(float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime * (Time.timeScale > 0f ? 1f : 0f))
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            CameraWeight = Mathf.Lerp(from, to, k);
            bars = CameraWeight;
            yield return null;
        }
        CameraWeight = bars = to;
    }

    private void LateUpdate()
    {
        if (subject == null)
            return;
        time += Time.deltaTime;
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.6f);
        // A slow drift around the boss, easing in
        orbit += Time.deltaTime * 4f;
        dolly = Mathf.Lerp(dolly, dollyTarget, Time.deltaTime * 3f);

        // Glide after the boss rather than jumping with it (Heimdall leaping off his lion)
        Vector3 wanted = subject.position + Vector3.up * height * 0.6f;
        look = time <= Time.deltaTime ? wanted : Vector3.Lerp(look, wanted, Time.deltaTime * 4f);
        Vector3 dir = Quaternion.Euler(0f, orbit, 0f) * side;
        // The boss keeps its face to the camera, whatever the scene turns it toward (the player may be behind)
        subject.rotation = Quaternion.Slerp(subject.rotation,
            Quaternion.LookRotation(Quaternion.Euler(0f, 20f, 0f) * dir), Time.deltaTime * 6f);
        float distance = (height * 2.3f + 3f) * dolly;
        Quaternion rot = Quaternion.LookRotation(-dir) * Quaternion.Euler(Pitch, 0f, 0f);
        CameraRotation = rot;
        CameraPosition = look - rot * Vector3.forward * distance;
    }

    private void OnGUI()
    {
        float w = Screen.width, h = Screen.height;
        GUI.depth = -10;
        if (flash > 0f)
            UiFont.Fill(new Rect(0f, 0f, w, h), new Color(1f, 1f, 1f, Mathf.Clamp01(flash)));

        // Letterbox
        float bar = h * 0.12f * bars;
        UiFont.Fill(new Rect(0f, 0f, w, bar), Color.black);
        UiFont.Fill(new Rect(0f, h - bar, w, bar), Color.black);

        if (titleTime < 0f || string.IsNullOrEmpty(title))
            return;
        float t = time - titleTime;
        float alpha = Mathf.Clamp01(t / 0.35f) * Mathf.Clamp01(bars * 1.5f);
        // The name stands just above the lower bar, the line beneath it in the bar itself;
        // an accent rule draws out from the middle between them
        float barTop = h - h * 0.12f;
        // Twin guardians ("A · B") stand one name per line, a little smaller; every line is shrunk to fit the screen
        string[] lines = title.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries);
        float scale = Mathf.Min(w / 1280f, h / 720f);
        float baseSize = (lines.Length > 1 ? Mathf.Lerp(46f, 40f, Mathf.Clamp01(t / 0.5f)) : Mathf.Lerp(66f, 56f, Mathf.Clamp01(t / 0.5f))) * scale;
        float lineHalf = Mathf.Lerp(0f, w * 0.3f, Mathf.SmoothStep(0f, 1f, t / 0.6f));
        UiFont.Fill(new Rect(w / 2f - lineHalf, barTop - 2f, lineHalf * 2f, 2f), new Color(accent.r, accent.g, accent.b, alpha));

        Color main = new Color(Mathf.Lerp(1f, accent.r, 0.45f), Mathf.Lerp(1f, accent.g, 0.45f), Mathf.Lerp(1f, accent.b, 0.45f), alpha);
        float y = barTop - 6f;
        for (int l = lines.Length - 1; l >= 0; l--)
        {
            float size = baseSize;
            var style = UiFont.Style(Mathf.RoundToInt(size), TextAnchor.MiddleCenter, FontStyle.Bold);
            float textW = style.CalcSize(new GUIContent(lines[l])).x;
            if (textW > w * 0.9f)
            {
                size *= w * 0.9f / textW;
                style = UiFont.Style(Mathf.Max(12, Mathf.RoundToInt(size)), TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            float lineH = size * 1.25f;
            y -= lineH;
            // A dark outline so it reads over bright floors
            style.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.6f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                GUI.Label(new Rect(Mathf.Cos(a) * 2f, y + Mathf.Sin(a) * 2f, w, lineH), lines[l], style);
            }
            style.normal.textColor = main;
            GUI.Label(new Rect(0f, y, w, lineH), lines[l], style);
        }
        var subStyle = UiFont.Style(Mathf.RoundToInt(20f * scale), TextAnchor.MiddleCenter);
        subStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, Mathf.Clamp01((t - 0.3f) / 0.4f) * alpha);
        GUI.Label(new Rect(0f, barTop + 6f, w, h * 0.12f - 12f), subtitle, subStyle);
    }

    private void OnDestroy()
    {
        if (current == this)
        {
            current = null;
            CameraWeight = 0f;
        }
    }
}
