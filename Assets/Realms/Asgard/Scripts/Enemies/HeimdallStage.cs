using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 「신들의 문지기」 staged as an animated scene: a dark temple hall far from the arena with its own camera and lights,
// the hero facing the corrupted Heimdall (VARCO model, acting with VARCO motions), the camera cutting between shots
// like an anime while the lines run in the VisualNovel box. Sections as in the design doc:
//  1 최후의 관문     the hall in darkness; only footsteps, then a voice and two eyes in the dark
//  2 헤임달의 등장   a light falls on him; full figure from low, then his face
//  3 엇갈린 신념     shot / reverse shot; a violet light passes through his eyes as he explains
//  4 헤임달의 본심   black cracks of blight: he doubles in pain wreathed in black smoke, then stamps — "닥쳐라"
//  5 최후의 경고     he threatens; the runes round him burn red
//  6 보스전 시작     he raises the horn — the stage goes, and HeimdallBoss blows it for real
public class HeimdallStage : MonoBehaviour
{
    private static readonly Vector3 Origin = new Vector3(6000f, 0f, -6000f);
    private static readonly Color Gold = new Color(1f, 0.82f, 0.4f);
    private static readonly Color HeroBlue = new Color(0.55f, 0.78f, 1f);
    private static readonly Color Narration = new Color(0.75f, 0.75f, 0.75f);
    private static readonly Color Blight = new Color(0.55f, 0.15f, 0.85f);

    private const float HeimdallZ = 6f;                // how far down the hall he stands from the hero
    private static float EyeForward = 0.42f, EyeUp = 0.3f, EyeApart = 0.16f;

    private Camera cam;
    private Vector3 camPos, camLook;
    private float shake;
    private Coroutine shot;
    private Transform heimdall, hero;
    private Light spot, rim, heroRim, key;
    private ParticleSystem smoke;
    private GameObject eyes;
    private readonly List<Light> sunsOff = new List<Light>();
    private bool fog;
    private FogMode fogMode;
    private Color fogColor, ambient;
    private float fogDensity;
    private AmbientMode ambientMode;

    public static IEnumerator Run()
    {
        var stage = new GameObject("HeimdallStage").AddComponent<HeimdallStage>();
        stage.Build();
        yield return stage.Scene();
        if (stage != null) Destroy(stage.gameObject);
    }

    // ───────────── The set ─────────────

    private void Build()
    {
        StoryStage.Cinematic = true;
        transform.position = Origin;
        fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor; fogDensity = RenderSettings.fogDensity;
        ambientMode = RenderSettings.ambientMode; ambient = RenderSettings.ambientLight;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.04f);
        RenderSettings.fogDensity = 0.045f;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.07f, 0.07f, 0.1f);
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.enabled) { l.enabled = false; sunsOff.Add(l); }

        cam = new GameObject("StageCamera").AddComponent<Camera>();
        cam.transform.SetParent(transform, false);
        cam.depth = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = RenderSettings.fogColor;
        cam.fieldOfView = 36f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null) data.renderPostProcessing = true;

        key = new GameObject("Key").AddComponent<Light>();
        key.transform.SetParent(transform, false);
        key.type = LightType.Directional;
        key.color = new Color(0.55f, 0.6f, 0.85f);
        key.intensity = 0.12f;
        key.shadows = LightShadows.Soft;
        key.transform.rotation = Quaternion.Euler(50f, 160f, 0f);

        // The hall: a dark stone floor between two rows of tall columns, the ruins of Asgard closing it behind him
        var floor = Mats.Get("hs_floor", new Color(0.1f, 0.095f, 0.11f), 0.45f);
        var column = Mats.Get("hs_column", new Color(0.32f, 0.29f, 0.25f), 0.2f);
        var trim = Mats.Get("hs_trim", new Color(0.55f, 0.42f, 0.2f), 0.5f);
        Mats.Shape(PrimitiveType.Cube, transform, new Vector3(0f, -0.5f, 6f), new Vector3(60f, 1f, 70f), floor);
        Mats.Shape(PrimitiveType.Cube, transform, new Vector3(0f, 0.01f, 6f), new Vector3(3.2f, 0.02f, 40f), Mats.Get("hs_runner", new Color(0.22f, 0.05f, 0.06f), 0.1f));
        for (int i = 0; i < 7; i++)
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 at = new Vector3(side * 5f, 0f, -8f + i * 4.5f);
                Mats.Shape(PrimitiveType.Cylinder, transform, at + Vector3.up * 4.5f, new Vector3(1.1f, 4.5f, 1.1f), column);
                Mats.Shape(PrimitiveType.Cube, transform, at + Vector3.up * 0.25f, new Vector3(1.6f, 0.5f, 1.6f), trim);
                Mats.Shape(PrimitiveType.Cube, transform, at + Vector3.up * 9.1f, new Vector3(1.6f, 0.4f, 1.6f), trim);
                // a brazier at every other column, guttering
                if (i % 2 == 0)
                {
                    var fire = new GameObject("Brazier").AddComponent<Light>();
                    fire.transform.SetParent(transform, false);
                    fire.transform.localPosition = at + new Vector3(-side * 0.9f, 1.6f, 0f);
                    fire.type = LightType.Point;
                    fire.color = new Color(1f, 0.5f, 0.2f);
                    fire.range = 5f;
                    fire.intensity = 1.2f;
                    Vfx.Glow(transform, fire.transform.localPosition, 0.6f, new Color(1f, 0.55f, 0.2f));
                }
            }
        StoryStage.Piece(transform, "AsgardRuin", Origin + new Vector3(-7f, 0f, 17f), 20f, 10f);
        StoryStage.Piece(transform, "AsgardRuin", Origin + new Vector3(7.5f, 0f, 18f), -25f, 9f);
        StoryStage.Piece(transform, "AsgardRuin", Origin + new Vector3(0f, 0f, 24f), 180f, 14f);
        Motes(new Vector3(0f, 3f, 6f), new Vector3(12f, 6f, 26f), new Color(0.9f, 0.85f, 0.7f, 0.5f), 18f, 0.03f, 0.07f, 0.05f, "glow", true);

        // Heimdall at the far end, facing the way in; the hero at the threshold
        heimdall = StoryStage.Actor(transform, "HeimdallCorrupt", Origin + new Vector3(0f, 0f, HeimdallZ), 180f, 2.8f, "Idle");
        Animate(heimdall);
        hero = Hero(Origin);

        spot = MakeLight(LightType.Spot, new Vector3(0f, 7f, HeimdallZ - 3f), Gold * 0.9f + Color.white * 0.1f, 14f, 0f);
        spot.spotAngle = 38f;
        spot.shadows = LightShadows.Soft;
        spot.transform.LookAt(heimdall.position + Vector3.up * 1.4f);
        rim = MakeLight(LightType.Point, new Vector3(0f, 3.2f, HeimdallZ + 2.2f), new Color(0.5f, 0.7f, 1f), 6f, 0f);
        heroRim = MakeLight(LightType.Point, new Vector3(0.6f, 2.4f, -1.8f), new Color(0.6f, 0.75f, 1f), 5f, 0.8f);
    }

    private Transform Hero(Vector3 at)
    {
        var switcher = FindAnyObjectByType<CharacterSwitcher>();
        CharacterProfile fighter = switcher != null ? switcher.Current : null;
        Transform t;
        if (fighter != null)
        {
            var holder = new GameObject("hold");
            holder.SetActive(false);
            GameObject go = Instantiate(fighter.gameObject, holder.transform);
            foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
                if (!(b is Animator) && !(b is Animation) && !(b is Light)) DestroyImmediate(b);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            go.transform.SetParent(transform, true);
            Destroy(holder);
            go.SetActive(true);
            go.name = "Hero";
            go.transform.localScale = fighter.transform.lossyScale;
            t = go.transform;
            var legacy = go.GetComponentInChildren<Animation>();
            if (legacy != null && legacy.GetClip("idle") != null) legacy.Play("idle");
            Animate(t);
        }
        else
            t = Mats.Shape(PrimitiveType.Capsule, transform, Vector3.zero, new Vector3(0.6f, 0.9f, 0.6f), Mats.Get("hs_hero", HeroBlue)).transform;
        t.position = at;
        t.rotation = Quaternion.LookRotation(Vector3.forward);
        return t;
    }

    // Animators run on unscaled time, so a slow-motion beat elsewhere doesn't freeze the actors
    private static void Animate(Transform t)
    {
        if (t == null) return;
        foreach (var a in t.GetComponentsInChildren<Animator>())
        {
            a.updateMode = AnimatorUpdateMode.UnscaledTime;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    private void OnDestroy()
    {
        StoryStage.Cinematic = false;
        RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity;
        RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
        foreach (Light l in sunsOff) if (l != null) l.enabled = true;
    }

    private void LateUpdate()
    {
        if (cam == null) return;
        Vector3 jitter = shake > 0f ? Random.insideUnitSphere * shake : Vector3.zero;
        shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 0.9f);
        cam.transform.position = camPos + jitter;
        Vector3 to = camLook - camPos;
        if (to.sqrMagnitude > 0.0001f) cam.transform.rotation = Quaternion.LookRotation(to);
    }

    // ───────────── The scene ─────────────

    private IEnumerator Scene()
    {
        var switcher = FindAnyObjectByType<CharacterSwitcher>();
        string name = switcher != null && switcher.Current != null ? switcher.Current.DisplayName : "전사";
        const string Heimdall = "헤임달", Unknown = "???";
        const VisualNovel.Side H = VisualNovel.Side.Right, P = VisualNovel.Side.Left;
        VisualNovel vn = VisualNovel.Open();
        vn.Dim = 1f;

        // ── 1. 최후의 관문: down the dark hall from behind the hero; then two eyes open in the dark ──
        Shot(Origin + new Vector3(0.9f, 1.9f, -4.6f), Origin + new Vector3(0.6f, 1.8f, -3.2f), H8(1.6f), H8(1.6f), 9f);
        StartCoroutine(Ramp(v => vn.Dim = v, 1f, 0f, 2.2f));
        yield return vn.Beat(1.2f);
        yield return vn.Say(null, "주변의 빛이 서서히 사라지고… 텅 빈 신전에 발걸음 소리만이 울려 퍼진다.", Narration);
        Eyes(Gold, 0.7f);
        Shot(FaceOf(heimdall) + new Vector3(0f, -0.3f, -4.2f), FaceOf(heimdall) + new Vector3(0f, -0.25f, -3.2f), FaceOf(heimdall), FaceOf(heimdall), 5f);
        yield return vn.Say(Unknown, "여기까지 도달한 인간이 있었군.", Gold);
        HeroCloseUp(4f);
        yield return vn.Say(name, "인간이라고? 나는 아스가르드의 전사다.", HeroBlue, P);
        Shot(FaceOf(heimdall) + new Vector3(0.3f, -0.3f, -3.6f), FaceOf(heimdall) + new Vector3(0.2f, -0.25f, -2.7f), FaceOf(heimdall), FaceOf(heimdall), 5f);
        yield return vn.Say(Unknown, "……그렇다면 더욱더 이곳에 있어서는 안 되겠군.", Gold);

        // ── 2. 헤임달의 등장: the light falls on him — full figure from low, then his face ──
        StartCoroutine(Ramp(v => spot.intensity = v, 0f, 40f, 1.6f));
        StartCoroutine(Ramp(v => rim.intensity = v, 0f, 6f, 1.6f));
        shake = 0.06f;
        Vector3 feet = heimdall.position;
        Shot(feet + new Vector3(1.6f, 0.4f, -5.8f), feet + new Vector3(1.1f, 0.5f, -4.6f), feet + Vector3.up * 1.9f, feet + Vector3.up * 2.1f, 4f);
        yield return vn.Beat(1.6f);
        Eyes(null, 0f);
        StoryStage.Act(heimdall, "Talk", 0.3f);
        yield return vn.Say(Heimdall, "나는 아스가르드의 수호자, 헤임달.", Gold, H);
        HeimdallCloseUp(0.15f, 5f);
        yield return vn.Say(Heimdall, "이곳을 지키는 것이 나의 사명이다.", Gold, H);
        OverHero(5f);
        yield return vn.Say(name, "아스가르드가 무너지고 있다. 너도 그 사실을 알고 있잖아!", HeroBlue, P);
        StoryStage.Act(heimdall, "Talk", 0.3f);
        OverHeimdall(5f, true);
        yield return vn.Say(Heimdall, "알고 있다. 그렇기에 더욱 누구도 이곳을 지나갈 수 없다.", Gold, H);

        // ── 3. 엇갈린 신념: shot / reverse shot; a violet light through his eyes ──
        StoryStage.Act(heimdall, "Idle", 0.4f);
        OverHeimdall(5f, false);
        yield return vn.Say(name, "세계수를 되살리기 위해 이곳까지 왔다. 길을 비켜!", HeroBlue, P);
        StoryStage.Act(heimdall, "Talk", 0.3f);
        HeimdallCloseUp(-0.2f, 4f);
        yield return vn.Say(Heimdall, "세계수를 되살린다고?", Gold, H);
        StoryStage.Act(heimdall, "Explain", 0.3f);
        Eyes(Blight, 1.6f);
        StartCoroutine(Wash(vn, new Color(0.12f, 0f, 0.18f), 0.35f, 1.4f));
        Orbit(heimdall.position, 3.8f, 1.6f, -25f, 20f, 7f);
        yield return vn.Say(Heimdall, "이미 늦었다. 세계수는 썩어가고 있으며, 아스가르드는 돌이킬 수 없는 길에 들어섰다.", Gold, H);
        HeroCloseUp(3f);
        yield return vn.Say(name, "그렇다고 포기할 수는 없어!", HeroBlue, P, true);
        StoryStage.Act(heimdall, "Talk", 0.3f);
        TwoShot(6f);
        yield return vn.Say(Heimdall, "포기? 나는 단 한 번도 나의 사명을 포기한 적이 없다.", Gold, H);

        // ── 4. 헤임달의 본심: the blight shows through him — pain, black smoke; then the mask again ──
        StoryStage.Act(heimdall, "Idle", 0.4f);
        OverHeimdall(4f, false);
        yield return vn.Say(name, "너도 타락한 건가……?", HeroBlue, P);
        StoryStage.Act(heimdall, "Pain", 0.15f);
        Smoke(true);
        Eyes(Blight, 2.2f);
        shake = 0.25f;
        vn.Shake = 7f;
        StartCoroutine(Ramp(v => rim.color = Color.Lerp(new Color(0.5f, 0.7f, 1f), Blight, v), 0f, 1f, 0.5f));
        StartCoroutine(Ramp(v => spot.intensity = v, 40f, 12f, 0.5f));
        StartCoroutine(Wash(vn, Blight * 0.35f, 0.4f, 1.8f));
        Shot(feet + new Vector3(-2.2f, 1.2f, -3.6f), feet + new Vector3(-1.6f, 1.4f, -3f), feet + Vector3.up * 1.3f, feet + Vector3.up * 1.5f, 2.4f);
        yield return vn.Beat(2.4f);
        Smoke(false);
        StoryStage.Act(heimdall, "Stomp", 0.15f);
        StartCoroutine(Ramp(v => rim.color = Color.Lerp(Blight, new Color(0.5f, 0.7f, 1f), v), 0f, 1f, 0.8f));
        StartCoroutine(Ramp(v => spot.intensity = v, 12f, 40f, 0.6f));
        yield return vn.Beat(0.5f);
        shake = 0.35f;
        Vfx.Dust(feet, 2.5f, 18, new Color(0.3f, 0.28f, 0.3f));
        StoryStage.Act(heimdall, "Threat", 0.2f);
        HeimdallCloseUp(0f, 3f, 0.75f);
        yield return vn.Say(Heimdall, "닥쳐라.", Gold, H, true);
        StoryStage.Act(heimdall, "Explain", 0.3f);
        Eyes(Gold, 1.4f);
        HeimdallCloseUp(0.3f, 6f);
        yield return vn.Say(Heimdall, "내 눈은 아홉 세계의 모든 것을 꿰뚫어 본다. 너의 운명 또한 이미 보았다.", Gold, H);
        StoryStage.Act(heimdall, "Idle", 0.4f);
        OverHero(5f);
        yield return vn.Say(name, "그렇다면 내가 이곳에 도달할 것도 알고 있었겠군.", HeroBlue, P);
        OverHeimdall(5f, true);
        yield return vn.Say(Heimdall, "……그래. 그렇기에 네가 이곳에 도달하기를 기다렸다.", Gold, H);

        // ── 5. 최후의 경고: he threatens; the runes round him burn red ──
        StoryStage.Act(heimdall, "Threat", 0.25f);
        spot.color = new Color(1f, 0.7f, 0.55f);
        rim.color = new Color(1f, 0.25f, 0.15f);
        rim.intensity = 9f;
        VfxPiece runes = Vfx.Decal(feet + Vector3.up * 0.03f, 7f, new Color(1f, 0.2f, 0.12f), "gs_runecircle_one", 18f);
        StartCoroutine(Pulse(vn, new Color(0.5f, 0.05f, 0.02f), 0.18f));
        Shot(Origin + new Vector3(3.8f, 4.4f, -3f), Origin + new Vector3(3.4f, 3.8f, -2.2f), Origin + new Vector3(0f, 1f, HeimdallZ * 0.6f), Origin + new Vector3(0f, 1.2f, HeimdallZ * 0.65f), 6f);
        yield return vn.Say(name, "마지막으로 묻겠다. 정말 나와 싸우겠다는 건가?", HeroBlue, P);
        Shot(feet + new Vector3(1.4f, 0.5f, -4.6f), feet + new Vector3(0.9f, 0.6f, -3.8f), feet + Vector3.up * 2f, feet + Vector3.up * 2.2f, 5f);
        yield return vn.Say(Heimdall, "나는 아스가르드의 문지기다. 그 누구도 나를 넘어설 수 없다.", Gold, H);
        StoryStage.Act(heimdall, "Talk", 0.3f);
        HeimdallCloseUp(-0.15f, 5f);
        yield return vn.Say(Heimdall, "돌아가라. 이것이 너에게 주는 마지막 기회다.", Gold, H);
        HeroCloseUp(5f);
        yield return vn.Say(name, "미안하지만…… 여기까지 와서 돌아갈 수는 없어.", HeroBlue, P);
        StoryStage.Act(heimdall, "Threat", 0.3f);
        HeimdallCloseUp(0.1f, 5f, 0.85f);
        yield return vn.Say(Heimdall, "그렇다면 증명해라. 네가 세계를 구할 자격이 있는지.", Gold, H);

        // ── 6. 보스전 시작: he raises the horn ──
        if (StoryStage.HasState(heimdall, "Raise")) StoryStage.Act(heimdall, "Raise", 0.25f, 3f);   // (its arm comes up ~4 s in)
        else StoryStage.Act(heimdall, "Stomp", 0.2f);
        Vector3 face = FaceOf(heimdall);
        Shot(face + new Vector3(1.5f, -0.9f, -3.8f), face + new Vector3(1.1f, -0.8f, -3.1f), face + Vector3.down * 0.4f, face + Vector3.down * 0.2f, 3f);
        yield return vn.Say(Heimdall, "와라. 네 운명을 내 눈앞에서 증명해라!", Gold, H, true);
        Vfx.Flash(HeadOf(heimdall), 6f, Gold, 0.5f);
        shake = 0.4f;
        vn.Close();
        StartCoroutine(Ramp(v => vn.Dim = v, 0f, 1f, 0.4f));
        yield return Wait(0.5f);
        if (runes != null) Destroy(runes.gameObject);
    }

    // ───────────── Shots ─────────────

    private Vector3 H8(float y) => Origin + new Vector3(0f, y, HeimdallZ);
    private static Vector3 HeadOf(Transform t) => StoryStage.Head(t);

    // The middle of a face: chibi heads are big, the head bone sits at their base
    private Vector3 FaceOf(Transform t)
    {
        if (t == heimdall) return HeadOf(heimdall) + Vector3.up * 0.35f;
        float h = HeroHeight;
        return hero.position + Vector3.up * h * 0.78f;
    }

    private float HeroHeight => hero != null ? Mathf.Max(0.8f, StoryStage.Bounds(hero).size.y) : 1.6f;

    // Heimdall's face from in front, a little to one side (side: -..+ = his right..left); push in by `near` (<1 closer)
    private void HeimdallCloseUp(float side, float seconds, float near = 1f)
    {
        Vector3 f = FaceOf(heimdall);
        // (from a little below: his stance bows the head, and a low angle makes him loom)
        Vector3 from = f + new Vector3(side * 1.6f + 0.2f, -0.75f, -2.6f * near);
        Shot(from, from + new Vector3(0f, 0.05f, 0.35f * near), f + Vector3.down * 0.2f, f + Vector3.down * 0.12f, seconds);
    }

    private void HeroCloseUp(float seconds)
    {
        Vector3 f = FaceOf(hero);
        float s = HeroHeight;
        Vector3 from = f + new Vector3(-0.45f * s, -0.12f * s, 1.5f * s);
        Shot(from, from + new Vector3(0.05f * s, 0f, -0.2f * s), f + Vector3.down * 0.12f * s, f + Vector3.down * 0.1f * s, seconds);
    }

    // Over the hero's shoulder at Heimdall
    private void OverHero(float seconds)
    {
        Vector3 p = FaceOf(hero), h = FaceOf(heimdall);
        Vector3 from = p + new Vector3(0.75f, 0.25f, -1.6f);
        Shot(from, from + new Vector3(-0.15f, 0f, 0.35f), Vector3.Lerp(p, h, 0.8f), h, seconds);
    }

    // Over Heimdall's shoulder at the hero (onHim: from the side, framing Heimdall)
    private void OverHeimdall(float seconds, bool onHim)
    {
        Vector3 p = FaceOf(hero), h = FaceOf(heimdall);
        if (onHim)
        {
            Vector3 from = Vector3.Lerp(p, h, 0.45f) + new Vector3(-2.2f, -0.2f, 0f);
            Shot(from, from + new Vector3(0.25f, 0f, 0.3f), h + Vector3.down * 0.3f, h + Vector3.down * 0.2f, seconds);
            return;
        }
        Vector3 back = h + new Vector3(-1.5f, 0.1f, 2.3f);
        Shot(back, back + new Vector3(0.2f, 0f, -0.35f), p, p, seconds);
    }

    // Both of them side on, from inside the colonnade
    private void TwoShot(float seconds)
    {
        Vector3 mid = Vector3.Lerp(hero.position, heimdall.position, 0.5f);
        Shot(mid + new Vector3(3.6f, 1.5f, -4.2f), mid + new Vector3(3.3f, 1.6f, -3.4f), mid + Vector3.up * 1.3f, mid + Vector3.up * 1.4f, seconds);
    }

    // Round a point, from one angle to another, looking at it
    private void Orbit(Vector3 center, float radius, float height, float fromAngle, float toAngle, float seconds)
    {
        if (shot != null) StopCoroutine(shot);
        shot = StartCoroutine(OrbitRoutine(center, radius, height, fromAngle, toAngle, seconds));
    }

    private IEnumerator OrbitRoutine(Vector3 center, float radius, float height, float fromAngle, float toAngle, float seconds)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
        {
            float a = Mathf.Lerp(fromAngle, toAngle, t * t * (3f - 2f * t)) * Mathf.Deg2Rad;
            camPos = center + new Vector3(Mathf.Sin(a) * radius, height + 0.4f, -Mathf.Cos(a) * radius);
            camLook = HeadOf(heimdall) + Vector3.down * 0.3f;
            yield return null;
        }
    }

    // Cut to a shot and drift through it
    private void Shot(Vector3 fromPos, Vector3 toPos, Vector3 fromLook, Vector3 toLook, float seconds)
    {
        if (shot != null) StopCoroutine(shot);
        camPos = fromPos;
        camLook = fromLook;
        shot = StartCoroutine(Drift(fromPos, toPos, fromLook, toLook, seconds));
    }

    private IEnumerator Drift(Vector3 fromPos, Vector3 toPos, Vector3 fromLook, Vector3 toLook, float seconds)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
        {
            float k = 1f - (1f - t) * (1f - t);
            camPos = Vector3.Lerp(fromPos, toPos, k);
            camLook = Vector3.Lerp(fromLook, toLook, k);
            yield return null;
        }
    }

    // ───────────── Effects ─────────────

    // Two points of light where his eyes are (null: put them out); fade out over `seconds` (0 = stay)
    private void Eyes(Color? color, float seconds)
    {
        if (eyes != null) Destroy(eyes);
        if (!color.HasValue || heimdall == null) return;
        Transform head = heimdall.GetComponentInChildren<Animator>()?.GetBoneTransform(HumanBodyBones.Head);
        Transform at = head != null ? head : heimdall;
        eyes = new GameObject("Eyes");
        eyes.transform.SetParent(at, false);
        // (unit world scale and Heimdall's facing, so the offsets below are metres along his face)
        eyes.transform.localScale = Vector3.one / Mathf.Max(0.0001f, at.lossyScale.x);
        eyes.transform.rotation = heimdall.rotation;
        // (a chibi head: big, eyes well forward of the head bone — tuned on the 2.8 m figure)
        eyes.transform.position = HeadOf(heimdall) + heimdall.forward * EyeForward + Vector3.up * EyeUp;
        foreach (float side in new[] { -1f, 1f })
            Vfx.Glow(eyes.transform, new Vector3(side * EyeApart, 0f, 0f), 0.3f, color.Value);
        if (seconds > 0f) Destroy(eyes, seconds);
    }

    private void Smoke(bool on)
    {
        if (smoke == null)
        {
            smoke = Motes(heimdall.position - Origin + Vector3.up * 1.3f, new Vector3(1.2f, 2.2f, 1.2f), new Color(0.05f, 0.02f, 0.08f, 0.8f), 0f, 0.5f, 1.1f, 0.7f, "smoke", false);
        }
        var em = smoke.emission;
        em.rateOverTime = on ? 45f : 0f;
    }

    private Light MakeLight(LightType type, Vector3 local, Color color, float range, float intensity)
    {
        var l = new GameObject(type + "Light").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.localPosition = local;
        l.type = type;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        return l;
    }

    private ParticleSystem Motes(Vector3 local, Vector3 box, Color color, float rate, float minSize, float maxSize, float rise, string texture, bool additive)
    {
        var go = new GameObject("Particles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.useUnscaledTime = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        velocity.y = new ParticleSystem.MinMaxCurve(rise * 0.7f, rise * 1.3f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Vfx.Material(texture, additive);
        r.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    private static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }

    private static IEnumerator Ramp(System.Action<float> set, float from, float to, float seconds)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
        {
            set(Mathf.Lerp(from, to, t));
            yield return null;
        }
        set(to);
    }

    private static IEnumerator Wash(VisualNovel vn, Color color, float strength, float seconds)
    {
        for (float t = 0f; t < 1f && vn != null; t += Time.unscaledDeltaTime / seconds)
        {
            vn.Tint = new Color(color.r, color.g, color.b, strength * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }
        if (vn != null) vn.Tint = Color.clear;
    }

    private static IEnumerator Pulse(VisualNovel vn, Color color, float strength)
    {
        for (float t = 0f; vn != null; t += Time.unscaledDeltaTime)
        {
            vn.Tint = new Color(color.r, color.g, color.b, strength * (0.55f + 0.45f * Mathf.Sin(t * 3f)));
            yield return null;
        }
    }
}
