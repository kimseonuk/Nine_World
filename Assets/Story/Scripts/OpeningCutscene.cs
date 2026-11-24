using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

// 오프닝 「이그드라실의 몰락」 — the opening from the design doc, played in-engine when the game starts:
//  1 세계수 이그드라실      the world tree rises out of the dark, golden motes, the nine worlds hung round it
//  2 평화로운 아홉 세계     Asgard's halls, Midgard's village, the giants' land
//  3 이그드라실의 이상 현상  a root turns black, the leaves darken, the tree shudders
//  4 아홉 세계의 혼란       Niflheim's ice storm, a giant run wild, Muspelheim erupting, monsters over Asgard
//  5 아스가르드의 붕괴      the sky splits, the columns fall, a shockwave
//  6 주인공의 추락          the hero falls through the debris into the light
//  7 니플헤임 도착          snow and fog, frozen ruins, drifting shadows, the hero lying in the snow
//  8 고대의 무기 발견       an altar rises; the weapons float up, each in its own light and runes
//  9 무기 선택              the player picks one (←/→ or click, Enter); its light wraps the hero
// 10 여정의 시작            the hero faces the snowfield, the camera pulls away, the title
// Everything is built from the game's own models and effects, far from the village, with its own camera, light and fog
// (all put back afterwards). Esc skips straight to the weapons. While it plays the game's HUD and controls rest
// (GameManager checks Playing); when the choice is made, done(fighter index) hands back to the game.
public class OpeningCutscene : MonoBehaviour
{
    public static bool Playing { get; private set; }

    private const string AutoPlayKey = "opening_autoplay";
    public static bool AutoPlay
    {
        get { return PlayerPrefs.GetInt(AutoPlayKey, 1) == 1; }
        set { PlayerPrefs.SetInt(AutoPlayKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public const string Title = "이그드라실의 몰락";

    // After the weapon is picked the game goes straight to the village. The story's later part (cut 9's light wrapping
    // the hero, cut 10 여정의 시작) is kept for when it's written — set this true to play it again.
    public static bool PlayStoryAfterChoice = false;

    private static readonly Vector3 Origin = new Vector3(6000f, 0f, 6000f);
    private const float TreeSize = 50f;              // the VARCO world tree's width, metres
    private static readonly Color Gold = new Color(1f, 0.82f, 0.38f);
    private static readonly Color[] RuneColors =
    {
        new Color(1f, 0.82f, 0.35f), new Color(0.45f, 0.8f, 1f), new Color(1f, 0.35f, 0.3f), new Color(0.6f, 1f, 0.55f),
        new Color(0.8f, 0.5f, 1f), new Color(1f, 0.6f, 0.2f), new Color(0.5f, 1f, 0.95f), new Color(1f, 0.5f, 0.8f),
    };

    // What the game hands over
    private CharacterSwitcher switcher;
    private Func<string, GameObject> prop;           // the generator's and the village's art by name (null when not in this project)
    private Enemy[] creatures;                        // einherjar, valkyrie, raven, heimdall (any may be null)
    private Light sun;
    private Action<int> done;

    // The stage
    private Transform root, set;
    private Camera cam;
    private Light keyLight;
    private Vector3 camPos, camLook;
    private float shake;
    private FogMode fogMode;
    private bool fog;
    private Color fogColor, ambient;
    private float fogDensity;
    private AmbientMode ambientMode;
    private bool sunWasOn;

    // On screen
    private string caption, line;
    private float captionAlpha, lineAlpha, letterbox, fade, titleAlpha, skipHint = 1f;
    private Color fadeColor = Color.black;
    private string prompt;
    private GUIStyle captionStyle, lineStyle, titleStyle, smallStyle, promptStyle, descStyle;

    // The weapons and the choice
    private readonly List<Transform> relics = new List<Transform>();
    private readonly List<Light> relicLights = new List<Light>();
    private int choice;
    private bool chosen, skipped, altarStarted;
    private Transform hero, niflheim;
    private Coroutine story;

    // ───────────── Starting it ─────────────

    public static void Play(CharacterSwitcher switcher, Func<string, GameObject> prop, Enemy[] creatures, Light sun, Action<int> done)
    {
        if (Playing)
            return;
        var go = new GameObject("Opening · " + Title);
        var o = go.AddComponent<OpeningCutscene>();
        o.switcher = switcher;
        o.prop = prop;
        o.creatures = creatures ?? new Enemy[0];
        o.sun = sun;
        o.done = done;
    }

    private void Start()
    {
        Playing = true;
        root = new GameObject("OpeningStage").transform;
        root.position = Origin;

        // Its own look: dark, fogged, lit by its own key light
        fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor; fogDensity = RenderSettings.fogDensity;
        ambientMode = RenderSettings.ambientMode; ambient = RenderSettings.ambientLight;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.ambientMode = AmbientMode.Flat;
        if (sun != null) { sunWasOn = sun.enabled; sun.enabled = false; }
        keyLight = new GameObject("OpeningKeyLight").AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.transform.SetParent(root, false);
        keyLight.shadows = LightShadows.Soft;

        cam = new GameObject("OpeningCamera").AddComponent<Camera>();
        cam.transform.SetParent(root, false);
        cam.depth = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 600f;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null) data.renderPostProcessing = true;

        story = StartCoroutine(Story());
    }

    private void OnDestroy()
    {
        Playing = false;
        Narration.Stop();
        MusicDirector.AmbienceOverride = null;
        RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity;
        RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
        if (sun != null) sun.enabled = sunWasOn;
        if (root != null) Destroy(root.gameObject);
    }

    private void Update()
    {
        // Esc: straight to the weapons (the choice itself is part of the game, so it can't be skipped)
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            Skip();
        skipHint = Mathf.MoveTowards(skipHint, altarStarted ? 0f : 1f, Time.deltaTime);
        if (prompt != null)
            UpdateChoice(kb);
    }

    private void LateUpdate()
    {
        if (cam == null) return;
        Vector3 jitter = shake > 0f ? Random.insideUnitSphere * shake : Vector3.zero;
        shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 0.8f);
        cam.transform.position = camPos + jitter;
        Vector3 to = camLook - camPos;
        if (to.sqrMagnitude > 0.0001f)
            cam.transform.rotation = Quaternion.LookRotation(to);
    }

    // ───────────── The story (cuts 1-7) ─────────────

    private IEnumerator Story()
    {
        fade = 1f;
        StartCoroutine(Letterbox(1f, 1.2f));
        yield return Cut1Yggdrasil();
        yield return Cut2NineWorlds();
        yield return Cut3Corruption();
        yield return Cut4Chaos();
        yield return Cut5Collapse();
        yield return Cut6Fall();
        yield return Cut7Niflheim();
        yield return Altar();
    }

    // Esc: the story ends here and the weapon choice comes up at once
    public void Skip()
    {
        if (altarStarted || skipped)
            return;
        skipped = true;
        Narration.Stop();
        GameAudio.Play("ui_back");
        StopAllCoroutines();   // the story and every camera move, caption and effect it started
        captionAlpha = lineAlpha = 0f;
        letterbox = 1f;
        StartCoroutine(SkipToAltar());
    }

    private IEnumerator SkipToAltar()
    {
        Say(null);
        Speak(null);
        yield return Fade(1f, 0.25f, Color.black);
        shake = 0f;
        letterbox = 1f;
        BuildNiflheim();
        LayHero(false);
        Look(niflheim.position + new Vector3(0f, 2.2f, -3.6f), niflheim.position + new Vector3(0f, 2.2f, 4f));
        yield return Altar(true);
    }

    // 1. 세계수 이그드라실
    private IEnumerator Cut1Yggdrasil()
    {
        GameAudio.Play("cine_tree");
        Mood(new Color(0.03f, 0.03f, 0.07f), 0.01f, new Color(0.18f, 0.16f, 0.22f), new Color(1f, 0.85f, 0.6f), 0.8f, new Vector3(35f, 30f, 0f));
        Transform tree = BuildYggdrasil(Vector3.zero);
        Vector3 at = tree.position;
        Look(at + new Vector3(0f, 2f, -34f), at + new Vector3(0f, 6f, 0f));
        StartCoroutine(Fade(0f, 2.5f, Color.black));
        StartCoroutine(SayLater(1.5f, "태초부터 아홉 세계를 지탱해 온 거대한 세계수, 이그드라실.", 7f));
        yield return Move(at + new Vector3(0f, 2f, -34f), at + new Vector3(-16f, 46f, -66f), at + new Vector3(0f, 6f, 0f), at + new Vector3(0f, 32f, 0f), 11f);
    }

    // 2. 평화로운 아홉 세계
    private IEnumerator Cut2NineWorlds()
    {
        GameAudio.Play("realm_enter", null, 0.8f);
        StartCoroutine(SayLater(0.6f, "신과 거인, 그리고 수많은 생명들이 각자의 세계에서 살아가며 균형을 이루고 있었다.", 12f));

        // Asgard: the halls of the gods in gold
        yield return CutTo(Color.white, 0.5f);
        Mood(new Color(0.55f, 0.5f, 0.62f), 0.006f, new Color(0.55f, 0.5f, 0.45f), new Color(1f, 0.93f, 0.78f), 1.3f, new Vector3(40f, 210f, 0f));
        Vector3 a = BuildAsgard(new Vector3(300f, 0f, 0f), false);
        yield return Move(a + new Vector3(-6f, 3f, -22f), a + new Vector3(4f, 4f, -8f), a + new Vector3(0f, 4f, 8f), a + new Vector3(0f, 7f, 20f), 4.6f);

        // Midgard: the people's village
        yield return CutTo(Color.white, 0.5f);
        Mood(new Color(0.62f, 0.72f, 0.82f), 0.008f, new Color(0.5f, 0.52f, 0.48f), new Color(1f, 0.9f, 0.72f), 1.2f, new Vector3(35f, 140f, 0f));
        Vector3 m = BuildMidgard(new Vector3(600f, 0f, 0f));
        yield return Move(m + new Vector3(14f, 5f, -14f), m + new Vector3(-4f, 3.5f, -15f), m + new Vector3(0f, 1.5f, 0f), m + new Vector3(-2f, 2f, 2f), 4.4f);

        // The giants' land
        yield return CutTo(Color.white, 0.5f);
        Mood(new Color(0.42f, 0.48f, 0.5f), 0.01f, new Color(0.38f, 0.42f, 0.42f), new Color(0.85f, 0.9f, 1f), 1f, new Vector3(25f, 60f, 0f));
        Vector3 g = BuildGiantland(new Vector3(900f, 0f, 0f), false);
        yield return Move(g + new Vector3(-5f, 1.5f, -20f), g + new Vector3(4f, 1.2f, -16f), g + new Vector3(0f, 3.5f, 6f), g + new Vector3(0f, 4.5f, 6f), 4.2f);
    }

    // 3. 이그드라실의 이상 현상
    private IEnumerator Cut3Corruption()
    {
        GameAudio.Play("cine_corrupt");
        yield return CutTo(Color.black, 0.6f);
        Mood(new Color(0.03f, 0.03f, 0.06f), 0.012f, new Color(0.17f, 0.15f, 0.22f), new Color(1f, 0.85f, 0.6f), 0.75f, new Vector3(35f, 30f, 0f));
        Transform tree = BuildYggdrasil(Vector3.zero);
        Vector3 at = tree.position;
        // (the root reaching toward the camera, -z)
        Transform root4 = tree.Find("Root4");
        Vector3 rootSpot = root4 != null ? root4.position + Vector3.up * 0.9f : at + new Vector3(0f, 0.9f, -6.5f);
        // On the VARCO tree: find where a root actually lies on its rock island, straight down onto the mesh
        Transform wholeTree = tree.Find("Whole");
        MeshFilter treeMesh = wholeTree != null ? wholeTree.GetComponentInChildren<MeshFilter>() : null;
        if (treeMesh != null)
        {
            var probe = treeMesh.gameObject.AddComponent<MeshCollider>();
            probe.sharedMesh = treeMesh.sharedMesh;
            Physics.SyncTransforms();
            // (only hits low on the island count: the crown spreads above it)
            rootSpot = at + new Vector3(0f, TreeSize * 0.07f, -TreeSize * 0.3f);
            float lowest = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(at + new Vector3(0f, 80f, -TreeSize * 0.3f), Vector3.down, 160f))
                if (hit.collider == probe && hit.point.y < at.y + TreeSize * 0.2f && hit.point.y < lowest) { lowest = hit.point.y; rootSpot = hit.point + Vector3.up * 0.4f; }
            Destroy(probe);
        }
        Look(rootSpot + new Vector3(3f, 2.8f, -8f), rootSpot);
        StartCoroutine(SayLater(0.8f, "그러나 어느 날, 알 수 없는 힘이 세계수에 스며들기 시작했다.", 7.5f));
        StartCoroutine(Move(rootSpot + new Vector3(3f, 2.8f, -8f), rootSpot + new Vector3(1f, 1.6f, -4.2f), rootSpot, rootSpot + new Vector3(0f, 0.3f, 0f), 5f));

        // The blight: black creeping over the root, black smoke rising, the gold turning black
        var blight = new List<Transform>();
        var black = Mats.Get("op_blight_matte", new Color(0.01f, 0.005f, 0.015f), 0.02f);
        for (int i = 0; i < 9; i++)
        {
            var spot = Mats.Shape(PrimitiveType.Sphere, set, rootSpot - Origin + new Vector3(Random.Range(-1.4f, 1.4f), Random.Range(-0.3f, 0.2f), Random.Range(-1.4f, 1.4f)),
                Vector3.zero, black);
            blight.Add(spot.transform);
        }
        ParticleSystem ink = Motes(set, rootSpot - Origin, new Vector3(2.5f, 0.3f, 2.5f), new Color(0.04f, 0.02f, 0.06f, 0.85f), 0f, 0.6f, 1.4f, 1.2f, "smoke", false);
        var leaves = set.GetComponentsInChildren<ParticleSystem>().FirstOrDefault(p => p.name == "Motes");
        Renderer[] bark = tree.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer) && !(r is LineRenderer)).ToArray();
        var original = bark.Select(r => r.material.HasProperty("_BaseColor") ? r.material.GetColor("_BaseColor") : Color.white).ToArray();
        for (float t = 0f; t < 8f; t += Time.deltaTime)
        {
            float k = t / 8f;
            for (int i = 0; i < blight.Count; i++)
                blight[i].localScale = new Vector3(1.4f, 0.25f, 1.4f) * Mathf.Clamp01(k * 1.6f - i * 0.08f) * (1f + i * 0.12f);
            var em = ink.emission;
            em.rateOverTime = 40f * k;
            if (leaves != null)
            {
                var main = leaves.main;
                main.startColor = Color.Lerp(Gold * 1.4f, new Color(0.12f, 0.03f, 0.15f), k);
            }
            for (int i = 0; i < bark.Length; i++)
                if (bark[i].material.HasProperty("_BaseColor"))
                    bark[i].material.SetColor("_BaseColor", Color.Lerp(original[i], original[i] * 0.25f, k * k));
            if (t > 4.5f) shake = Mathf.Max(shake, 0.08f + 0.1f * k);
            yield return null;
        }
        // The rot takes the whole tree: a black pulse, and the world tree stands withered, purple veins in its bark
        Transform whole = tree.Find("Whole"), rotten = tree.Find("Rotten");
        if (whole != null && rotten != null)
        {
            Vfx.Flash(rootSpot + Vector3.up, 14f, new Color(0.55f, 0.2f, 0.8f), 0.6f);
            yield return CutTo(Color.black, 0.35f);
            whole.gameObject.SetActive(false);
            rotten.gameObject.SetActive(true);
            if (leaves != null)
            {
                var main = leaves.main;
                main.startColor = new Color(0.35f, 0.12f, 0.5f);
            }
            AddLight(set, at - Origin + new Vector3(0f, 8f, -16f), new Color(0.6f, 0.2f, 1f), 40f, 4f);
            StartCoroutine(Move(at + new Vector3(-22f, 4f, -48f), at + new Vector3(-12f, 10f, -60f), at + new Vector3(0f, 16f, 0f), at + new Vector3(0f, 20f, 0f), 4f));
        }
        // Black leaves come down as the tree shudders
        Motes(set, at - Origin + new Vector3(0f, 26f, 0f), new Vector3(36f, 4f, 36f), new Color(0.1f, 0.05f, 0.1f), 80f, 0.3f, 0.55f, -2.5f, "leaf", false);
        shake = 0.35f;
        yield return Wait(3f);
    }

    // 4. 아홉 세계의 혼란
    private IEnumerator Cut4Chaos()
    {
        GameAudio.Play("cine_chaos");
        StartCoroutine(SayLater(0.4f, "세계수의 부패는 순식간에 퍼져나갔고, 아홉 세계는 혼란에 빠졌다.", 12f));

        // Niflheim: an ice storm
        yield return CutTo(Color.white, 0.35f);
        Mood(new Color(0.55f, 0.66f, 0.8f), 0.018f, new Color(0.45f, 0.52f, 0.65f), new Color(0.8f, 0.9f, 1f), 0.9f, new Vector3(30f, 20f, 0f));
        Vector3 n = new Vector3(1200f, 0f, 0f);
        NewSet();
        Ground(Origin + n, new Color(0.82f, 0.88f, 0.96f), 120f);
        for (int i = 0; i < 22; i++)
        {
            float h = Random.Range(1.5f, 6f);
            var spike = Mats.Shape(PrimitiveType.Cube, set, n + new Vector3(Random.Range(-12f, 12f), h * 0.4f, Random.Range(2f, 22f)),
                new Vector3(0.7f, h, 0.7f), Mats.Get("op_ice", new Color(0.4f, 0.68f, 1f), 0.95f, new Color(0.12f, 0.32f, 0.6f)));
            spike.transform.rotation = Quaternion.Euler(Random.Range(-18f, 18f), Random.Range(0f, 90f), Random.Range(-18f, 18f));
        }
        Transform arch = StoryStage.Piece(set, "FrozenArch", Origin + n + new Vector3(3f, 0f, 13f), -20f, 9f);
        if (arch != null) StartCoroutine(Cue(1.6f, () => { shake = 0.3f; Vfx.Burst(arch.position + Vector3.up * 5f, new Color(0.7f, 0.85f, 1f), 40, 10f, 0.3f, 0f); }));
        Blizzard(n, 900f, 14f);
        shake = 0.12f;
        Vector3 nw = Origin + n;
        yield return Move(nw + new Vector3(-4f, 2f, -10f), nw + new Vector3(3f, 2.5f, -6f), nw + new Vector3(0f, 2f, 10f), nw + new Vector3(0f, 2f, 10f), 3f);

        // Jotunheim: a giant run wild
        yield return CutTo(Color.white, 0.3f);
        Mood(new Color(0.3f, 0.32f, 0.3f), 0.02f, new Color(0.3f, 0.3f, 0.28f), new Color(0.9f, 0.85f, 0.75f), 0.9f, new Vector3(20f, 70f, 0f));
        Vector3 j = BuildGiantland(new Vector3(1500f, 0f, 0f), true);
        StartCoroutine(Rockfall(j, 2.8f));
        yield return Move(j + new Vector3(-5f, 1.2f, -19f), j + new Vector3(3f, 1f, -15f), j + new Vector3(0f, 4.5f, 6f), j + new Vector3(0f, 4f, 8f), 3f);

        // Muspelheim: the mountain bursts
        yield return CutTo(new Color(1f, 0.5f, 0.2f), 0.3f);
        Mood(new Color(0.35f, 0.08f, 0.04f), 0.02f, new Color(0.35f, 0.15f, 0.1f), new Color(1f, 0.55f, 0.3f), 0.8f, new Vector3(25f, 0f, 0f));
        Vector3 v = BuildVolcano(new Vector3(1800f, 0f, 0f));
        yield return Move(v + new Vector3(-16f, 3f, -36f), v + new Vector3(-8f, 2.5f, -29f), v + new Vector3(0f, 13f, 0f), v + new Vector3(0f, 19f, 0f), 3.4f);

        // Asgard: monsters over the halls of the gods
        yield return CutTo(Color.black, 0.3f);
        Mood(new Color(0.18f, 0.08f, 0.12f), 0.012f, new Color(0.25f, 0.15f, 0.2f), new Color(1f, 0.45f, 0.4f), 0.9f, new Vector3(25f, 210f, 0f));
        Vector3 a = BuildAsgard(new Vector3(300f, 0f, 0f), true);
        shake = 0.1f;
        yield return Move(a + new Vector3(3f, 2.5f, -17f), a + new Vector3(-2.5f, 3f, -14f), a + new Vector3(0f, 3f, 4f), a + new Vector3(0f, 4f, 6f), 3.2f);
    }

    // 5. 아스가르드의 붕괴
    private IEnumerator Cut5Collapse()
    {
        GameAudio.Play("cine_collapse");
        yield return CutTo(Color.black, 0.4f);
        Mood(new Color(0.16f, 0.1f, 0.2f), 0.01f, new Color(0.22f, 0.18f, 0.26f), new Color(0.9f, 0.7f, 1f), 0.8f, new Vector3(30f, 210f, 0f));
        Vector3 a = BuildAsgard(new Vector3(300f, 0f, 0f), false, false);
        StartCoroutine(SayLater(0.6f, "마침내 신들의 세계, 아스가르드마저 무너지기 시작했다.", 8f));
        // Odin alone in the hall, looking up as the sky splits, then falling to his knees
        Transform odin = StoryStage.Actor(set, "Odin", a + new Vector3(0f, 0f, -2f), 180f, 2.7f, "LookUp");
        if (StoryStage.Has("AsgardRuin"))
        {
            StoryStage.Piece(set, "AsgardRuin", a + new Vector3(-9f, 0f, 4f), 30f, 9f);
            StoryStage.Piece(set, "AsgardRuin", a + new Vector3(10f, 0f, 9f), -40f, 8f);
        }
        Coroutine pan = StartCoroutine(Move(a + new Vector3(0f, 2.5f, -24f), a + new Vector3(0f, 4f, -30f), a + new Vector3(0f, 9f, 10f), a + new Vector3(0f, 12f, 10f), 11f));

        // The sky splits
        var crack = new GameObject("SkyCrack").AddComponent<LineRenderer>();
        crack.transform.SetParent(set, false);
        crack.material = Vfx.Material("trail", true);
        crack.startColor = crack.endColor = new Color(0.9f, 0.75f, 1f) * 5f;
        crack.widthMultiplier = 1.2f;
        var points = new List<Vector3>();
        Vector3 c = a + new Vector3(-30f, 34f, 40f);
        for (int i = 0; i < 22; i++)
        {
            points.Add(c);
            c += new Vector3(Random.Range(1.6f, 3.6f), Random.Range(-2.6f, 2.2f), Random.Range(-0.5f, 0.5f));
        }
        for (float t = 0f; t < 3f; t += Time.deltaTime)
        {
            int count = Mathf.Clamp(Mathf.CeilToInt(points.Count * t / 3f), 2, points.Count);
            // light bleeding out of each new stretch of the crack
            if (count > crack.positionCount)
                Vfx.Glow(set, points[count - 1] - Origin, 6f, new Color(0.85f, 0.65f, 1f));
            crack.positionCount = count;
            crack.SetPositions(points.Take(count).ToArray());
            RenderSettings.fogColor = Color.Lerp(new Color(0.16f, 0.1f, 0.2f), new Color(0.06f, 0.03f, 0.08f), t / 3f);
            cam.backgroundColor = RenderSettings.fogColor;
            shake = Mathf.Max(shake, 0.05f + t * 0.03f);
            yield return null;
        }

        // Close on Odin from low in front: the All-Father sinks to his knees under the broken sky
        if (odin != null)
        {
            StopCoroutine(pan);
            StoryStage.Act(odin, "KneelFear", 0.35f);
            Vector3 head = StoryStage.Head(odin);
            AddLight(set, head - Origin + new Vector3(0f, 2f, -2f), new Color(0.8f, 0.65f, 1f), 8f, 3f);
            yield return Move(head + new Vector3(1.4f, -0.9f, -3.2f), head + new Vector3(0.8f, -1.1f, -2.4f), head + new Vector3(0f, 0.4f, 0f), head + new Vector3(0f, -0.6f, 0f), 2.6f);
            Look(a + new Vector3(0f, 4f, -30f), a + new Vector3(0f, 11f, 10f));
        }

        // The halls come down
        foreach (Transform piece in set.Cast<Transform>().Where(x => x.name.StartsWith("Fallable")).ToArray())
        {
            var body = piece.gameObject.AddComponent<Rigidbody>();
            body.mass = 50f;
            var box = piece.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(1.6f, 4f, 1.6f);
            box.center = new Vector3(0f, 2f, 0f);
            body.AddForceAtPosition(new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(0.5f, 1.5f)) * 900f, piece.position + Vector3.up * 3.5f);
        }
        // The shockwave
        Vfx.Flash(a + new Vector3(0f, 6f, 12f), 40f, new Color(0.9f, 0.8f, 1f), 0.5f);
        Vfx.GroundRing(a + new Vector3(0f, 0.1f, 8f), 40f, new Color(0.8f, 0.6f, 1f), 1.4f);
        Shockwave.Spawn(a + new Vector3(0f, 0.2f, 8f), 14f, new Color(0.85f, 0.7f, 1f), 30);
        shake = 0.5f;
        yield return Wait(4.5f);
    }

    // 6. 주인공의 추락
    private IEnumerator Cut6Fall()
    {
        GameAudio.Play("cine_fall");
        yield return CutTo(Color.white, 0.25f);
        Mood(new Color(0.12f, 0.08f, 0.2f), 0.004f, new Color(0.4f, 0.35f, 0.5f), new Color(0.9f, 0.85f, 1f), 1f, new Vector3(60f, 0f, 0f));
        Vector3 start = new Vector3(2100f, 400f, 0f);
        Transform falling = Hero(switcher != null ? switcher.Current : null, set, Origin + start);
        // Debris falling alongside, and streaks of light rushing up past
        var debris = new List<Transform>();
        for (int i = 0; i < 26; i++)
        {
            var d = Mats.Shape(PrimitiveType.Cube, set, start + Random.insideUnitSphere * 10f, Vector3.one * Random.Range(0.3f, 1.4f),
                Mats.Get("op_debris", new Color(0.55f, 0.5f, 0.45f), 0.2f));
            d.transform.rotation = Random.rotation;
            debris.Add(d.transform);
        }
        ParticleSystem streaks = Motes(set, start - new Vector3(0f, 30f, 0f), new Vector3(16f, 2f, 16f), new Color(0.85f, 0.8f, 1f), 120f, 0.25f, 0.5f, 60f, "glow", true);
        var streakRender = streaks.GetComponent<ParticleSystemRenderer>();
        streakRender.renderMode = ParticleSystemRenderMode.Stretch;
        streakRender.velocityScale = 0.06f;
        Speak("으아아악!", 2.2f, 0.4f);
        float speed = 30f;
        for (float t = 0f; t < 6.5f; t += Time.deltaTime)
        {
            Vector3 fall = Vector3.down * speed * Time.deltaTime;
            falling.position += fall;
            falling.Rotate(new Vector3(140f, 60f, 90f) * Time.deltaTime, Space.World);
            foreach (Transform d in debris)
            {
                d.position += fall * Random.Range(0.92f, 1.04f);
                d.Rotate(new Vector3(90f, 45f, 30f) * Time.deltaTime);
            }
            streaks.transform.position = falling.position - new Vector3(0f, 30f, 0f);
            Look(falling.position + new Vector3(Mathf.Sin(t) * 2f, 5f, -4f), falling.position);
            shake = 0.06f;
            yield return null;
        }
        // Into the light
        Vfx.Flash(falling.position, 12f, Color.white, 0.6f);
        yield return Fade(1f, 0.8f, Color.white);
    }

    // 7. 니플헤임 도착
    private IEnumerator Cut7Niflheim()
    {
        MusicDirector.AmbienceOverride = "cine_blizzard";
        BuildNiflheim();
        LayHero(true);
        Vector3 p = niflheim.position;
        Look(p + new Vector3(-24f, 14f, -40f), p);
        StartCoroutine(Fade(0f, 2f, Color.white));
        StartCoroutine(SayLater(1.6f, "그렇게 한 전사는 모든 것을 잃은 채, 가장 깊은 세계로 추락했다.", 7f));
        yield return Move(p + new Vector3(-24f, 14f, -40f), p + new Vector3(-2.5f, 1.8f, -4.5f), p + new Vector3(0f, 4f, 10f), p + new Vector3(0f, 0.3f, 0f), 9f);
    }

    // ───────────── The weapons (cuts 8-10) ─────────────

    // quick (skipped with Esc): straight to the choice — the altar already standing, the weapons already up, no tour
    private IEnumerator Altar(bool quick = false)
    {
        MusicDirector.AmbienceOverride = "cine_blizzard";
        GameAudio.Play("cine_relic");
        altarStarted = true;
        Vector3 p = niflheim.position;
        Vector3 altarAt = p + new Vector3(0f, 0f, 4f);
        if (quick)
        {
            Altarstone(altarAt);
            BuildRelics(altarAt + new Vector3(0f, 2.4f, 0f));
            StartCoroutine(Fade(0f, 0.5f, fadeColor));
        }
        else
            yield return AltarTour(p, altarAt);

        // 9. The choice
        choice = Mathf.Clamp(switcher != null && switcher.Current != null ? IndexOf(switcher.Current) : 0, 0, Mathf.Max(0, relics.Count - 1));
        prompt = "무기를 선택하세요";
        while (!chosen)
            yield return null;
        prompt = null;
        if (PlayStoryAfterChoice)
        {
            yield return Chosen(altarAt);

            // 10. The journey begins
            yield return Journey();
        }
        else
            yield return Fade(1f, 0.45f, Color.black);   // (straight to the village; the rest of the story comes later)
        int picked = choice;
        Action<int> then = done;
        Destroy(gameObject);
        then?.Invoke(picked);
    }

    // 8. The hero gets up; an altar rises out of the snow and the weapons float up round it; the camera visits each
    private IEnumerator AltarTour(Vector3 p, Vector3 altarAt)
    {
        if (fade > 0f) StartCoroutine(Fade(0f, 1f, fadeColor));
        StartCoroutine(StandHero(1.2f));
        Transform altar = Altarstone(altarAt);
        StartCoroutine(Rise(altar, -2.4f, 0f, 2.2f));
        shake = 0.15f;
        StartCoroutine(SayLater(2.2f, "그러나 아직 희망은 사라지지 않았다. 세계수의 힘이 깃든 여덟 가지 무기.", 8f));
        yield return Move(p + new Vector3(-3f, 2f, -5f), p + new Vector3(-4f, 2.8f, -3f), altarAt + new Vector3(0f, 0.5f, 0f), altarAt + new Vector3(0f, 2.2f, 0f), 2.4f);
        BuildRelics(altarAt + new Vector3(0f, 2.4f, 0f));
        // Each weapon in turn, then the hero from behind
        for (int i = 0; i < relics.Count; i++)
        {
            Vector3 w = relics[i].position;
            Vector3 outward = (w - (altarAt + Vector3.up * 2.4f)).normalized;
            yield return Move(cam.transform.position, w + outward * 3f + Vector3.up * 0.6f, camLook, w, Mathf.Max(0.9f, 5.5f / relics.Count));
        }
        yield return Move(cam.transform.position, hero.position + new Vector3(0f, 2.2f, -3.6f), camLook, altarAt + new Vector3(0f, 2.2f, 0f), 1.6f);
    }

    // "Really take this one?" — asked before the choice is final (Enter/Y or 예 to take it, Esc/N or 아니요 to look again)
    private bool confirming;
    private float lastHoverSwitch = -1f;

    private int heardChoice;
    private bool heardConfirm;

    private void UpdateChoice(Keyboard kb)
    {
        if (chosen || relics.Count == 0) return;
        if (choice != heardChoice) { heardChoice = choice; GameAudio.Play("ui_move"); }
        if (confirming != heardConfirm) { heardConfirm = confirming; GameAudio.Play(confirming ? "ui_select" : "ui_back"); }
        int n = relics.Count;
        Mouse mouse = Mouse.current;

        if (confirming)
        {
            if (kb != null)
            {
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.yKey.wasPressedThisFrame)
                    chosen = true;
                else if (kb.escapeKey.wasPressedThisFrame || kb.nKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)
                    confirming = false;
            }
        }
        else
        {
            if (kb != null)
            {
                // A / D (or ←/→) turn the carousel one weapon round
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) choice = (choice + n - 1) % n;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) choice = (choice + 1) % n;
                for (int i = 0; i < n && i < 9; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame) choice = i;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                    confirming = true;
            }
            if (mouse != null && cam != null)
            {
                Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                // pointing at a weapon turns it to the front (only when the mouse moves or clicks, so it doesn't fight
                // the keys, and not again while the carousel is still swinging, so it doesn't chase the cursor)
                bool moved = mouse.delta.ReadValue().sqrMagnitude > 1f;
                bool click = mouse.leftButton.wasPressedThisFrame;
                if ((moved || click) && Physics.Raycast(ray, out RaycastHit hit, 100f))
                {
                    int i = relics.FindIndex(r => hit.collider.transform == r);
                    if (i >= 0)
                    {
                        bool settled = Mathf.Abs(Mathf.DeltaAngle(spin * 360f / n, choice * 360f / n)) < 12f;
                        if (i != choice && (settled || click) && Time.unscaledTime - lastHoverSwitch > 0.25f)
                        {
                            choice = i;
                            lastHoverSwitch = Time.unscaledTime;
                        }
                        else if (i == choice && click)
                            confirming = true;   // a click on the one at the front asks to take it
                    }
                }
            }
        }

        // The carousel turns the short way round toward the chosen one
        float target = choice;
        float delta = Mathf.Repeat(target - spin + n / 2f, n) - n / 2f;
        spin = Mathf.Repeat(spin + delta * Mathf.Clamp01(Time.deltaTime * 6f), n);
        for (int i = 0; i < n; i++)
            relics[i].position = RingPosition(i, n);

        // The one at the front glows and swells; the rest dim
        for (int i = 0; i < n; i++)
        {
            float scale = i == choice ? (confirming ? 1.55f : 1.35f) : 0.8f;
            relics[i].localScale = Vector3.one * Mathf.MoveTowards(relics[i].localScale.x, scale, Time.deltaTime * 2f);
            relicLights[i].intensity = Mathf.MoveTowards(relicLights[i].intensity, i == choice ? 9f : 1.5f, Time.deltaTime * 20f);
        }
    }

    private IEnumerator Chosen(Vector3 altarAt)
    {
        GameAudio.Play("cine_chosen");
        Transform relic = relics[choice];
        Color color = RuneColors[choice % RuneColors.Length];
        // Close on the chosen weapon as its light flares
        Vfx.Flash(relic.position, 5f, color, 0.5f);
        Vfx.Burst(relic.position, color, 30, 7f, 0.25f, 0f);
        yield return Move(cam.transform.position, relic.position + (cam.transform.position - relic.position).normalized * 2.2f, camLook, relic.position, 1f);
        for (int i = 0; i < relics.Count; i++)
            if (i != choice) StartCoroutine(Dissolve(relics[i], relicLights[i]));
        // It comes to the hero, and the light wraps them
        Vector3 from = relic.position;
        Vector3 to = hero.position + Vector3.up * 1.3f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
        {
            relic.position = Vector3.Lerp(from, to, t * t);
            relic.Rotate(Vector3.up, 540f * Time.deltaTime, Space.World);
            Look(Vector3.Lerp(cam.transform.position, hero.position + new Vector3(1.8f, 1.8f, 3.2f), Time.deltaTime * 3f), relic.position);
            yield return null;
        }
        relic.gameObject.SetActive(false);
        // The hero becomes the chosen weapon's fighter
        Vector3 at = hero.position;
        Quaternion facing = hero.rotation;
        Destroy(hero.gameObject);
        hero = Hero(Fighter(choice), set, at);
        hero.rotation = facing;
        Vfx.Flash(at + Vector3.up, 7f, color, 0.6f);
        Vfx.GroundRing(at + Vector3.up * 0.05f, 5f, color, 1.2f);
        Vfx.Aura(hero, 1.8f, color);
        shake = 0.25f;
        Look(at + new Vector3(1.6f, 1.7f, 2.8f), at + Vector3.up * 1.3f);
        Speak("이 힘으로 반드시 돌아가겠어.", 3.2f, 0.6f);
        yield return Move(at + new Vector3(1.6f, 1.7f, 2.8f), at + new Vector3(0.9f, 1.5f, 2.2f), at + Vector3.up * 1.3f, at + Vector3.up * 1.4f, 3.6f);
    }

    private IEnumerator Journey()
    {
        GameAudio.Play("realm_enter");
        // The hero turns to the snowfield; the camera draws back from behind them into the wide white world
        Vector3 at = hero.position;
        Quaternion from = hero.rotation, to = Quaternion.LookRotation(Vector3.forward);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
        {
            hero.rotation = Quaternion.Slerp(from, to, t);
            yield return null;
        }
        StartCoroutine(SayLater(0.8f, "무너진 세계를 되찾기 위한 여정. 그리고 신들의 세계로 돌아가기 위한 싸움이 시작된다.", 6.5f));
        StartCoroutine(Move(at + new Vector3(0f, 1.8f, -3f), at + new Vector3(0f, 14f, -30f), at + new Vector3(0f, 1.6f, 6f), at + new Vector3(0f, 2f, 40f), 9f));
        yield return Wait(7.5f);
        yield return Fade(1f, 1.5f, Color.black);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 1f) { titleAlpha = t; yield return null; }
        titleAlpha = 1f;
        yield return Wait(2.2f);
        for (float t = 1f; t > 0f; t -= Time.deltaTime / 0.8f) { titleAlpha = t; yield return null; }
        titleAlpha = 0f;
    }

    // ───────────── Sets ─────────────

    private void NewSet()
    {
        if (set != null) Destroy(set.gameObject);
        set = new GameObject("Cut").transform;
        set.SetParent(root, false);
        relics.Clear();
        relicLights.Clear();
    }

    // The world tree: the golden tree grown huge, roots in the dark, the nine worlds hung round it on threads of light
    private Transform BuildYggdrasil(Vector3 local)
    {
        NewSet();
        var tree = new GameObject("Yggdrasil").transform;
        tree.SetParent(set, false);
        tree.localPosition = local;
        Ground(Origin + local, new Color(0.08f, 0.07f, 0.06f), 220f);
        // The VARCO world tree (golden crown, roots over a rock island), and its rotten twin hidden inside it for cut 3
        Transform whole = StoryStage.Piece(tree, "Yggdrasil", Origin + local, 0f, TreeSize);
        if (whole != null)
        {
            whole.name = "Whole";
            Transform rotten = StoryStage.Piece(tree, "YggdrasilCorrupt", Origin + local, 0f, TreeSize);
            if (rotten != null) { rotten.name = "Rotten"; rotten.gameObject.SetActive(false); }
            // Markers where the great roots meet the ground (cut 3 watches one blacken)
            for (int i = 0; i < 8; i++)
            {
                var marker = new GameObject("Root" + i).transform;
                marker.SetParent(tree, false);
                marker.localPosition = Quaternion.Euler(0f, i * 45f, 0f) * new Vector3(0f, TreeSize * 0.04f, TreeSize * 0.2f);
            }
        }
        else
        {
            var bark = Mats.Get("op_bark", new Color(0.42f, 0.32f, 0.2f), 0.25f);
            Mats.Shape(PrimitiveType.Cylinder, tree, new Vector3(0f, 7f, 0f), new Vector3(6.5f, 7f, 6.5f), bark);
            Mats.Shape(PrimitiveType.Cylinder, tree, new Vector3(0f, 19f, 0f), new Vector3(4.6f, 5f, 4.6f), bark);
            if (Place(prop?.Invoke("tree"), tree, new Vector3(0f, 20f, 0f), 0f, 7f, "Crown") == null)
                Mats.Shape(PrimitiveType.Sphere, tree, new Vector3(0f, 32f, 0f), new Vector3(30f, 16f, 30f), Mats.Get("op_crown", Gold, 0.3f, Gold * 0.4f));
            for (int i = 0; i < 8; i++)
            {
                float yaw = i * 45f + 22.5f + Random.Range(-8f, 8f);
                var rootPiece = Mats.Shape(PrimitiveType.Cylinder, tree, Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.7f, 5.8f),
                    new Vector3(1.8f, 4.6f, 1.8f), bark);
                rootPiece.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(82f, 0f, 0f);
                rootPiece.name = "Root" + i;
            }
        }
        // Golden light and motes
        AddLight(set, local + new Vector3(0f, 34f, 0f), Gold, 70f, 6f);
        AddLight(set, local + new Vector3(0f, 5f, -10f), Gold, 25f, 2.5f);
        var motes = Motes(set, local + new Vector3(0f, 24f, 0f), new Vector3(60f, 44f, 60f), Gold, 180f, 0.14f, 0.32f, 0.6f, "glow", true);
        motes.name = "Motes";
        // The nine worlds, hung round the tree outside its crown
        var thread = Vfx.Material("trail", true);
        for (int i = 0; i < 9; i++)
        {
            float yaw = i * 140f;
            float height = 3f + i * 4.6f;
            Vector3 orb = local + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, height, 31f + (i % 3) * 4.5f);
            Color c = RuneColors[i % RuneColors.Length];
            Mats.Shape(PrimitiveType.Sphere, set, orb, Vector3.one * 1.2f, Mats.Get("op_world" + i, c, 0.6f, c * 2f));
            Vfx.Glow(set, orb, 4f, c);
            var line = new GameObject("Thread").AddComponent<LineRenderer>();
            line.transform.SetParent(set, false);
            line.useWorldSpace = false;
            line.material = thread;
            line.widthMultiplier = 0.12f;
            line.startColor = c * 1.5f;
            line.endColor = Gold * 1.5f;
            line.SetPositions(new[] { orb, local + new Vector3(0f, height, 0f) });
        }
        return tree;
    }

    // Asgard: Heimdall's tower beyond a golden colonnade, statues, banners, the throne; the gods' warriors standing by
    // (with monsters: dark sky, ravens wheeling and the warriors turned hostile)
    private Vector3 BuildAsgard(Vector3 local, bool monsters, bool gods = true)
    {
        NewSet();
        Ground(Origin + local, monsters ? new Color(0.25f, 0.2f, 0.22f) : new Color(0.78f, 0.72f, 0.6f), 160f);
        Place(prop?.Invoke("tower"), set, local + new Vector3(0f, 0f, 46f), 180f, 1f, "Tower");
        Place(prop?.Invoke("bifrost"), set, local + new Vector3(0f, 0f, 28f), 180f, 0.8f, "Bifrost");
        Place(prop?.Invoke("throne"), set, local + new Vector3(0f, 0f, 18f), 180f, 1.2f, "Throne");
        for (int i = 0; i < 6; i++)
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 at = local + new Vector3(side * 5.5f, 0f, -10f + i * 5.5f);
                GameObject columnArt = prop?.Invoke("column");
                if (columnArt == null) columnArt = prop?.Invoke("pillar");
                Place(columnArt, set, at, 0f, 1.5f, "Fallable");
                if (i % 2 == 1) Place(prop?.Invoke("banner"), set, at + new Vector3(-side * 0.8f, 0f, 0f), side > 0 ? -90f : 90f, 1.2f, "Banner");
                if (i == 0 || i == 5) Place(prop?.Invoke("statue"), set, at + new Vector3(side * 3f, 0f, 0f), side > 0 ? -90f : 90f, 1.3f, "Statue");
            }
        AddLight(set, local + new Vector3(0f, 10f, 10f), monsters ? new Color(1f, 0.3f, 0.25f) : Gold, 40f, 3f);
        Motes(set, local + new Vector3(0f, 6f, 8f), new Vector3(24f, 10f, 40f), monsters ? new Color(0.6f, 0.2f, 0.3f) : Gold, 60f, 0.08f, 0.2f, 0.3f, "glow", true);
        // Its people. At peace: einherjar on guard, a valkyrie, Odin before his throne with arms open, Thor at ease.
        // Overrun: blight wraiths stalking up the colonnade, Thor smashing at them
        bool cast = StoryStage.Has("Odin");
        if (cast && !monsters)
        {
            for (int i = 0; i < 4; i++)
                Figure(Creature(0), local + new Vector3(i % 2 == 0 ? -3f : 3f, 0f, -4f + i * 5f), i % 2 == 0 ? 90f : -90f, 1f, null);
            Figure(Creature(1), local + new Vector3(2.2f, 0f, 7f), 200f, 1f, null);
            if (gods)
            {
                StoryStage.Actor(set, "Odin", Origin + local + new Vector3(0f, 0f, 13.5f), 180f, 2.7f, "ArmSpread");
                StoryStage.Actor(set, "Thor", Origin + local + new Vector3(-2.4f, 0f, 2f), 160f, 2.4f, "Idle");
            }
        }
        else if (cast && monsters)
        {
            for (int i = 0; i < 6; i++)
            {
                Transform w = StoryStage.Actor(set, "Wraith", Origin + local + new Vector3((i % 3 - 1) * 2.6f + Random.Range(-0.5f, 0.5f), 0f, 2f + i * 2.4f),
                    180f + Random.Range(-15f, 15f), Random.Range(2f, 2.5f), i % 2 == 0 ? "Walk" : "Growl");
                if (w != null && i % 2 == 0) StartCoroutine(Advance(w, 0.9f, 6f));
            }
            Transform thor = StoryStage.Actor(set, "Thor", Origin + local + new Vector3(-1.5f, 0f, -6f), 15f, 2.4f, "Threat");
            StartCoroutine(Cue(0.8f, () => StoryStage.Act(thor, "Smash", 0.2f)));
            StartCoroutine(Cue(2.2f, () => { shake = 0.4f; if (thor != null) Vfx.Dust(thor.position + thor.forward * 1.5f, 4f, 20, new Color(0.4f, 0.35f, 0.4f)); }));
        }
        else if (creatures.Length > 0)
        {
            for (int i = 0; i < 4; i++)
                Figure(Creature(0), local + new Vector3(i % 2 == 0 ? -3f : 3f, 0f, -4f + i * 5f), monsters ? 180f + Random.Range(-40f, 40f) : (i % 2 == 0 ? 90f : -90f), 1f,
                    monsters ? new Color(0.5f, 0.3f, 0.3f) : (Color?)null);
            Figure(Creature(1), local + new Vector3(0f, 0f, 6f), 180f, 1f, monsters ? new Color(0.5f, 0.3f, 0.35f) : (Color?)null);
            Figure(Creature(1), local + new Vector3(0f, 0f, 14f), 180f, 1.2f, monsters ? new Color(0.4f, 0.3f, 0.4f) : (Color?)null);
        }
        if (monsters)
            StartCoroutine(Flock(local + new Vector3(0f, 9f, 10f), 10));
        return Origin + local;
    }

    // Midgard: the longhouse, the forge and its smith, braziers, people's fires
    private Vector3 BuildMidgard(Vector3 local)
    {
        NewSet();
        Ground(Origin + local, new Color(0.35f, 0.5f, 0.25f), 160f);
        Place(prop?.Invoke("longhouse"), set, local + new Vector3(-4f, 0f, 6f), 180f, 1f, "Longhouse");
        Place(prop?.Invoke("forge"), set, local + new Vector3(5f, 0f, 2f), 210f, 1f, "Forge");
        Place(prop?.Invoke("anvil"), set, local + new Vector3(3.5f, 0f, -0.5f), 200f, 1f, "Anvil");
        Place(prop?.Invoke("smith"), set, local + new Vector3(2.2f, 0f, -1f), 160f, 1f, "Smith");
        Place(prop?.Invoke("brazier"), set, local + new Vector3(-1f, 0f, -3f), 0f, 1f, "Brazier");
        Place(prop?.Invoke("tree"), set, local + new Vector3(-12f, 0f, 14f), 40f, 1.8f, "Tree");
        Place(prop?.Invoke("tree"), set, local + new Vector3(12f, 0f, 16f), 120f, 1.5f, "Tree");
        AddLight(set, local + new Vector3(4f, 2f, 1f), new Color(1f, 0.6f, 0.3f), 10f, 3f);
        Motes(set, local + new Vector3(0f, 3f, 4f), new Vector3(20f, 6f, 20f), new Color(1f, 0.9f, 0.6f), 30f, 0.05f, 0.1f, 0.2f, "glow", true);
        return Origin + local;
    }

    // The giants' land: grey stone, standing rocks and a giant (an einherjar grown tall and stone-grey); run wild, it stamps
    private Vector3 BuildGiantland(Vector3 local, bool wild)
    {
        NewSet();
        Ground(Origin + local, new Color(0.38f, 0.4f, 0.36f), 160f);
        var stone = Mats.Get("op_giantrock", new Color(0.32f, 0.33f, 0.3f), 0.1f);
        for (int i = 0; i < 14; i++)
        {
            float s = Random.Range(1.5f, 5f);
            var rock = Mats.Shape(PrimitiveType.Cube, set, local + new Vector3(Random.Range(-20f, 20f), s * 0.3f, Random.Range(0f, 28f)),
                new Vector3(s, s * Random.Range(0.6f, 1.6f), s), stone);
            rock.transform.rotation = Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f));
        }
        // The frost giant: at peace it lumbers across the land; run wild it roars and smashes the ground
        Transform frost = StoryStage.Actor(set, "FrostGiant", Origin + local + new Vector3(wild ? 0f : -6f, 0f, 6f), wild ? 180f : 110f, 9f, wild ? "Roar" : "Walk");
        if (frost != null)
        {
            if (wild)
            {
                StartCoroutine(Cue(0.4f, () => shake = 0.25f));
                StartCoroutine(Cue(1.5f, () => StoryStage.Act(frost, "Smash", 0.2f)));
                StartCoroutine(Cue(2.4f, () => { shake = 0.6f; Vfx.Dust(frost.position + frost.forward * 4f, 8f, 30, new Color(0.5f, 0.48f, 0.42f)); }));
            }
            else
                StartCoroutine(Advance(frost, 1.3f, 5f));
            return Origin + local;
        }
        Transform giant = Figure(Creature(0), local + new Vector3(0f, 0f, 6f), 180f, 5f, new Color(0.45f, 0.5f, 0.55f));
        if (wild && giant != null)
            StartCoroutine(Stomp(giant));
        return Origin + local;
    }

    // Muspelheim: a dark mountain spewing fire, embers in the air, a red glow
    private Vector3 BuildVolcano(Vector3 local)
    {
        NewSet();
        Ground(Origin + local, new Color(0.12f, 0.06f, 0.05f), 200f);
        Vector3 v = local + new Vector3(0f, 0f, 20f);
        Vector3 crater = v + new Vector3(0f, 18f, 0f);
        Transform mountain = StoryStage.Piece(set, "Volcano", Origin + v, 0f, 40f);
        if (mountain != null)
        {
            Bounds b = StoryStage.Bounds(mountain);
            crater = new Vector3(b.center.x, b.max.y - 3f, b.center.z) - Origin;
            // A fire giant rising out of the lava field in front of it
            Transform fire = StoryStage.Actor(set, "FireGiant", Origin + v + new Vector3(-3f, 0f, -11f), 195f, 10f, "Rise");
            if (fire != null)
            {
                AddLight(set, fire.position - Origin + new Vector3(0f, 4f, -5f), new Color(1f, 0.5f, 0.2f), 18f, 5f);
                AddLight(set, fire.position - Origin + new Vector3(0f, 9f, 4f), new Color(1f, 0.35f, 0.1f), 14f, 6f);
            }
        }
        else
        {
            var rock = Mats.Get("op_volcano", new Color(0.15f, 0.1f, 0.09f), 0.1f);
            for (int i = 0; i < 4; i++)
                Mats.Shape(PrimitiveType.Cylinder, set, v + new Vector3(0f, 2f + i * 4f, 0f), new Vector3(26f - i * 6f, 2f, 26f - i * 6f), rock);
            Mats.Shape(PrimitiveType.Cylinder, set, v + new Vector3(0f, 17.9f, 0f), new Vector3(4f, 0.2f, 4f), Mats.Get("op_lava", new Color(1f, 0.4f, 0.1f), 0.6f, new Color(3f, 0.9f, 0.2f)));
        }
        AddLight(set, crater + new Vector3(0f, 2f, 0f), new Color(1f, 0.45f, 0.15f), 60f, 8f);
        StartCoroutine(Eruption(Origin + crater));
        Motes(set, local + new Vector3(0f, 6f, 10f), new Vector3(40f, 10f, 40f), new Color(1f, 0.5f, 0.15f), 150f, 0.06f, 0.14f, 2.5f, "glow", true);
        return Origin + v - new Vector3(0f, 0f, 0f);
    }

    // Niflheim: a snowfield in fog, frozen ruins, shadows drifting through the mist
    private void BuildNiflheim()
    {
        NewSet();
        Mood(new Color(0.55f, 0.62f, 0.72f), 0.03f, new Color(0.45f, 0.5f, 0.6f), new Color(0.8f, 0.88f, 1f), 0.75f, new Vector3(25f, 30f, 0f));
        Vector3 local = new Vector3(2400f, 0f, 0f);
        niflheim = new GameObject("Niflheim").transform;
        niflheim.SetParent(set, false);
        niflheim.localPosition = local;
        Ground(Origin + local, new Color(0.86f, 0.9f, 0.96f), 220f);
        var ice = new Color(0.7f, 0.85f, 1f);
        for (int i = 0; i < 9; i++)
        {
            float a = i * 40f + Random.Range(-10f, 10f);
            Vector3 at = local + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, Random.Range(9f, 18f));
            Transform ruin = Place(prop?.Invoke(i % 3 == 0 ? "runestone" : "pillar"), set, at, Random.Range(0f, 360f), Random.Range(1f, 1.6f), "FrozenRuin");
            if (ruin != null) Tint(ruin, ice);
            ruin?.Rotate(Random.Range(-10f, 10f), 0f, Random.Range(-14f, 14f));
        }
        // A frozen arch half-buried in the drifts, and blight wraiths walking slowly round at the edge of the fog
        StoryStage.Piece(set, "FrozenArch", Origin + local + new Vector3(-7f, 0f, 15f), 25f, 8f);
        StoryStage.Piece(set, "FrozenArch", Origin + local + new Vector3(11f, 0f, 9f), -70f, 6.5f);
        for (int i = 0; i < 2; i++)
        {
            Transform wraith = StoryStage.Actor(set, "Wraith", Origin + local + new Vector3(0f, 0f, 19f), 0f, 2.3f, "Walk");
            if (wraith != null)
            {
                StoryStage.Tint(wraith, new Color(0.35f, 0.35f, 0.45f));
                StartCoroutine(Circle(wraith, Origin + local, 19f + i * 4f, 0.05f + i * 0.015f, i * 150f + 40f));
            }
        }
        Blizzard(local, 160f, 3f);
        // Shadows in the mist: dark shapes drifting at the edge of sight
        for (int i = 0; i < 5; i++)
        {
            var shade = Motes(set, local + Quaternion.Euler(0f, i * 72f, 0f) * new Vector3(0f, 1.6f, 16f), new Vector3(1.5f, 3f, 1.5f),
                new Color(0.04f, 0.04f, 0.07f, 0.75f), 12f, 1.2f, 2.4f, 0.15f, "smoke", false);
            StartCoroutine(Drift(shade.transform, Origin + local, 16f + i, 0.12f + i * 0.02f, i * 72f));
        }
        AddLight(set, local + new Vector3(0f, 5f, 4f), new Color(0.7f, 0.85f, 1f), 18f, 1.5f);
    }

    private Transform Altarstone(Vector3 at)
    {
        var altar = new GameObject("Altar").transform;
        altar.SetParent(set, true);
        altar.position = at;
        // The VARCO altar: a frozen stone well ringed with glowing runes
        if (StoryStage.Piece(altar, "NiflAltar", at, 0f, 4.4f) == null)
        {
            var stone = Mats.Get("op_altar", new Color(0.42f, 0.45f, 0.52f), 0.3f);
            Mats.Shape(PrimitiveType.Cylinder, altar, new Vector3(0f, 0.25f, 0f), new Vector3(3.2f, 0.25f, 3.2f), stone);
            Mats.Shape(PrimitiveType.Cylinder, altar, new Vector3(0f, 0.7f, 0f), new Vector3(2.2f, 0.25f, 2.2f), stone);
            Transform rune = Place(prop?.Invoke("runestone"), altar, Vector3.up * 0.95f, 0f, 0.8f, "AltarRune");
            if (rune != null) rune.localPosition = Vector3.up * 0.95f;
        }
        Vfx.Glow(altar, Vector3.up * 1.4f, 5f, new Color(0.6f, 0.85f, 1f));
        AddLight(altar, Vector3.up * 2f, new Color(0.6f, 0.85f, 1f), 10f, 3f);
        return altar;
    }

    // Each fighter's own weapon (the greatsword, the twin blades, the spear, the axes, the hammer...) taken off the model
    // and hung in its own light, all on a ring round the altar (a carousel: the chosen one turns to the front, facing the
    // camera); a weapon that's part of its fighter's body shows as its painted card instead
    private Vector3 ringCenter;
    private float spin;                                   // carousel position, in relic steps (eases toward choice)
    private readonly List<Transform> cards = new List<Transform>();
    private const float RingRadius = 2.7f;

    private void BuildRelics(Vector3 center)
    {
        relics.Clear();
        relicLights.Clear();
        cards.Clear();
        ringCenter = center;
        int n = switcher != null ? switcher.Characters.Count : 0;
        spin = Mathf.Clamp(switcher != null && switcher.Current != null ? IndexOf(switcher.Current) : 0, 0, Mathf.Max(0, n - 1));
        for (int i = 0; i < n; i++)
        {
            CharacterProfile fighter = switcher.Characters[i];
            Color c = RuneColors[i % RuneColors.Length];
            var holder = new GameObject("Relic_" + fighter.WeaponName).transform;
            holder.SetParent(set, true);
            holder.position = RingPosition(i, n);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(holder, false);
            // the spear shows Gungnir (made in VARCO) rather than the plain spear in the keeper's hand, laid at a slant
            // like the other weapons (stood upright, a long thin spear reached up behind the caption panel)
            bool shown = false;
            if (fighter.Weapon == CharacterProfile.WeaponKind.Spear)
            {
                var tilt = new GameObject("Tilt").transform;
                tilt.SetParent(visual, false);
                tilt.localRotation = Quaternion.Euler(0f, 0f, -60f);
                shown = WeaponIcons.Model(fighter.Weapon, tilt);
                if (!shown)
                    Destroy(tilt.gameObject);
            }
            if (!shown && !WeaponModel(fighter, visual) && !WeaponIcons.Model(fighter.Weapon, visual))
            {
                Texture2D icon = WeaponIcons.For(fighter.Weapon);
                if (icon != null)
                {
                    // the weapon's painted card, framed in its rune colour, turned to the camera
                    var card = Mats.Shape(PrimitiveType.Quad, visual, Vector3.zero, new Vector3(1.25f, 1.25f, 1f), CardMaterial(icon));
                    var rim = Mats.Shape(PrimitiveType.Quad, card.transform, new Vector3(0f, 0f, 0.01f), new Vector3(1.08f, 1.08f, 1f), Mats.Get("op_card_rim" + i, c, 0.5f, c * 1.4f));
                    rim.name = "Rim";
                    cards.Add(visual);
                }
                else
                {
                    var crystal = Mats.Shape(PrimitiveType.Cube, visual, Vector3.zero, new Vector3(0.45f, 1.3f, 0.45f), Mats.Get("op_relic" + i, c, 0.9f, c * 1.6f));
                    crystal.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                }
            }
            holder.gameObject.AddComponent<SphereCollider>().radius = 0.9f;
            Vfx.Glow(holder, Vector3.zero, 2.6f, c);
            relicLights.Add(AddLight(holder, Vector3.zero, c, 5f, 4f));
            relics.Add(holder);
            StartCoroutine(Hover(holder, visual, i));
            Vfx.Flash(holder.position, 2.5f, c, 0.4f);
        }
    }

    private bool WeaponModel(CharacterProfile fighter, Transform into)
    {
        var parts = new List<GameObject>();
        if (fighter.HeldWeapon != null)
            parts.Add(fighter.HeldWeapon);
        else
        {
            var candidates = fighter.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is MeshRenderer) && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null)
                .Select(r => r.gameObject).ToList();
            parts.AddRange(candidates.Where(g => !candidates.Any(o => o != g && g.transform.IsChildOf(o.transform))));
        }
        if (parts.Count == 0)
            return false;
        foreach (GameObject part in parts)
        {
            GameObject copy = Instantiate(part, into);
            copy.SetActive(true);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = part.transform.rotation * Quaternion.Inverse(fighter.transform.rotation);
            copy.transform.localScale = part.transform.lossyScale;
            StripBehaviours(copy);
            foreach (var t in copy.GetComponentsInChildren<TrailRenderer>(true)) Destroy(t);
        }
        // Centred, about a metre and a half long
        var renderers = into.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (size < 0.01f) return false;
        into.localScale = Vector3.one * (1.5f / size);
        b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        into.position += into.parent.position - b.center;
        return true;
    }

    // ───────────── Pieces ─────────────

    private Transform Place(GameObject prefab, Transform parent, Vector3 local, float yaw, float scale, string name)
    {
        if (prefab == null) return null;
        // made under an inactive holder so none of its scripts wake up
        var holder = new GameObject("hold");
        holder.SetActive(false);
        GameObject go = Instantiate(prefab, holder.transform);
        StripBehaviours(go);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        go.transform.SetParent(parent, false);
        Destroy(holder);
        go.name = name;
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = prefab.transform.localScale * scale;
        return go.transform;
    }

    // Scripts, agents, sounds: everything that would act; what's left only draws and animates
    private static void StripBehaviours(GameObject go)
    {
        foreach (var b in go.GetComponentsInChildren<Behaviour>(true).Reverse())
            if (!(b is Animator) && !(b is Animation) && !(b is Light))
                DestroyImmediate(b);
    }

    private Enemy Creature(int index) => index < creatures.Length && creatures[index] != null ? creatures[index] : creatures.FirstOrDefault(c => c != null);

    // A creature as a still figure (its scripts left asleep, its animator idling)
    private Transform Figure(Enemy prefab, Vector3 local, float yaw, float scale, Color? tint)
    {
        if (prefab == null) return null;
        Transform t = Place(prefab.gameObject, set, local, yaw, scale, prefab.name);
        if (t != null && tint.HasValue) Tint(t, tint.Value);
        return t;
    }

    // The hero: a copy of a fighter's model, idling
    private Transform Hero(CharacterProfile fighter, Transform parent, Vector3 world)
    {
        if (fighter == null && switcher != null && switcher.Characters.Count > 0)
            fighter = switcher.Characters[0];
        Transform t;
        if (fighter != null)
        {
            var holder = new GameObject("hold");
            holder.SetActive(false);
            GameObject go = Instantiate(fighter.gameObject, holder.transform);
            StripBehaviours(go);
            go.SetActive(true);
            go.transform.SetParent(parent, true);
            Destroy(holder);
            go.name = "Hero";
            go.transform.localScale = fighter.transform.lossyScale;
            t = go.transform;
            var legacy = go.GetComponentInChildren<Animation>();
            if (legacy != null && legacy.GetClip("idle") != null) legacy.Play("idle");
        }
        else
        {
            t = Mats.Shape(PrimitiveType.Capsule, parent, Vector3.zero, new Vector3(0.6f, 0.9f, 0.6f), Mats.Get("op_hero", Gold)).transform;
        }
        t.position = world;
        hero = t;
        return t;
    }

    private CharacterProfile Fighter(int index) =>
        switcher != null && index >= 0 && index < switcher.Characters.Count ? switcher.Characters[index] : null;

    private int IndexOf(CharacterProfile fighter)
    {
        for (int i = 0; i < switcher.Characters.Count; i++)
            if (switcher.Characters[i] == fighter) return i;
        return 0;
    }

    private void LayHero(bool lying)
    {
        if (hero != null && hero.name == "Hero" && hero.parent == set) Destroy(hero.gameObject);
        hero = Hero(switcher != null ? switcher.Current : null, set, niflheim.position + new Vector3(0f, lying ? 0.15f : 0f, 0f));
        hero.rotation = lying ? Quaternion.Euler(-88f, 30f, 0f) : Quaternion.LookRotation(Vector3.forward);
    }

    private IEnumerator StandHero(float seconds)
    {
        if (hero == null) yield break;
        Quaternion from = hero.rotation, to = Quaternion.LookRotation(Vector3.forward);
        Vector3 a = hero.position, b = niflheim.position;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            hero.rotation = Quaternion.Slerp(from, to, t * t * (3f - 2f * t));
            hero.position = Vector3.Lerp(a, b, t);
            yield return null;
        }
        hero.rotation = to;
    }

    private static void Tint(Transform t, Color color)
    {
        foreach (var r in t.GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * color);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", m.GetColor("_Color") * color);
            }
    }

    private void Ground(Vector3 center, Color color, float size)
    {
        var g = Mats.Shape(PrimitiveType.Cube, set, center - Origin - Vector3.up * 0.5f, new Vector3(size, 1f, size),
            Mats.Get("op_ground_" + ColorUtility.ToHtmlStringRGB(color), color, 0.15f));
        g.name = "Ground";
    }

    private Light AddLight(Transform parent, Vector3 local, Color color, float range, float intensity)
    {
        var l = new GameObject("Light").AddComponent<Light>();
        l.transform.SetParent(parent, false);
        l.transform.localPosition = local;
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        return l;
    }

    // A looping box of drifting particles (rise > 0 floats up, < 0 falls)
    private ParticleSystem Motes(Transform parent, Vector3 local, Vector3 box, Color color, float rate, float minSize, float maxSize, float rise,
        string texture, bool additive)
    {
        var go = new GameObject("Particles");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.maxParticles = 3000;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        velocity.y = new ParticleSystem.MinMaxCurve(rise * 0.7f, rise * 1.3f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var fadeOver = ps.colorOverLifetime;
        fadeOver.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fadeOver.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Vfx.Material(texture, additive);
        r.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    private void Blizzard(Vector3 local, float rate, float wind)
    {
        var snow = Motes(set, local + new Vector3(0f, 8f, 6f), new Vector3(50f, 16f, 50f), new Color(1f, 1f, 1f, 0.9f), rate, 0.05f, 0.14f, -2f, "glow", true);
        var v = snow.velocityOverLifetime;
        v.x = new ParticleSystem.MinMaxCurve(wind * 0.7f, wind * 1.3f);
        var mist = Motes(set, local + new Vector3(0f, 1f, 6f), new Vector3(50f, 2f, 50f), new Color(0.9f, 0.94f, 1f, 0.3f), rate * 0.04f, 4f, 8f, 0.05f, "smoke", false);
        var mv = mist.velocityOverLifetime;
        mv.x = new ParticleSystem.MinMaxCurve(wind * 0.2f, wind * 0.4f);
    }

    // ───────────── Motion ─────────────

    private IEnumerator Stomp(Transform giant)
    {
        Vector3 basePos = giant.position;
        for (float t = 0f; giant != null && t < 3.5f; t += Time.deltaTime)
        {
            float phase = Mathf.Repeat(t, 0.9f) / 0.9f;
            giant.position = basePos + Vector3.up * Mathf.Sin(phase * Mathf.PI) * 1.2f + giant.forward * t * 1.5f;
            if (phase < Time.deltaTime / 0.9f + 0.001f)
            {
                shake = 0.35f;
                Vfx.Dust(giant.position, 4f, 16, new Color(0.5f, 0.48f, 0.42f));
                GameAudio.Play("cine_stomp");
            }
            yield return null;
        }
    }

    private IEnumerator Rockfall(Vector3 center, float seconds)
    {
        GameAudio.Play("cine_rocks");
        for (float t = 0f; t < seconds; t += 0.25f)
        {
            var r = Mats.Shape(PrimitiveType.Sphere, set, center - Origin + new Vector3(Random.Range(-10f, 10f), 18f, Random.Range(-2f, 14f)),
                Vector3.one * Random.Range(0.6f, 1.6f), Mats.Get("op_rock", new Color(0.4f, 0.38f, 0.34f), 0.1f), true);
            r.AddComponent<Rigidbody>();
            yield return new WaitForSeconds(0.25f);
        }
    }

    private IEnumerator Eruption(Vector3 crater)
    {
        GameAudio.Play("cine_erupt");
        for (int i = 0; i < 4 && set != null; i++)
        {
            Vfx.Fire(crater, Vector3.up, 14f, 22f, 1.2f);
            Vfx.Burst(crater + Vector3.up * 2f, new Color(1f, 0.5f, 0.1f), 40, 14f, 0.5f, 4f);
            Vfx.Flash(crater, 18f, new Color(1f, 0.5f, 0.15f), 0.3f);
            shake = 0.3f;
            yield return new WaitForSeconds(0.8f);
        }
    }

    private IEnumerator Flock(Vector3 local, int count)
    {
        GameAudio.Play("cine_birds");
        Enemy raven = Creature(2);
        var birds = new List<Transform>();
        for (int i = 0; i < count; i++)
        {
            Transform b = raven != null
                ? Place(raven.gameObject, set, local + Random.insideUnitSphere * 6f, 0f, 1f, "Raven")
                : Mats.Shape(PrimitiveType.Sphere, set, local + Random.insideUnitSphere * 6f, new Vector3(0.6f, 0.2f, 0.9f), Mats.Get("op_bird", new Color(0.05f, 0.05f, 0.07f))).transform;
            if (b != null) birds.Add(b);
        }
        Vector3 center = Origin + local;
        for (float t = 0f; t < 6f; t += Time.deltaTime)
        {
            for (int i = 0; i < birds.Count; i++)
            {
                if (birds[i] == null) yield break;
                float a = t * 1.2f + i * 0.63f;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * (7f + i % 3), Mathf.Sin(a * 2f) * 1.5f + i * 0.3f, Mathf.Sin(a) * (7f + i % 3));
                birds[i].rotation = Quaternion.LookRotation(p - birds[i].position + Vector3.forward * 0.001f);
                birds[i].position = p;
            }
            yield return null;
        }
    }

    // Something to happen a moment into the shot
    private IEnumerator Cue(float delay, Action act)
    {
        yield return new WaitForSeconds(delay);
        if (set != null) act();
    }

    // An actor walking straight ahead (its walk plays in place; this carries it)
    private IEnumerator Advance(Transform actor, float speed, float seconds)
    {
        for (float t = 0f; t < seconds && actor != null; t += Time.deltaTime)
        {
            actor.position += actor.forward * speed * Time.deltaTime;
            yield return null;
        }
    }

    // An actor walking a slow circle round a point, facing the way it goes
    private IEnumerator Circle(Transform actor, Vector3 center, float radius, float speed, float startAngle)
    {
        for (float t = 0f; actor != null; t += Time.deltaTime)
        {
            float a = (startAngle + t * speed * 57.3f) * Mathf.Deg2Rad;
            actor.position = new Vector3(center.x + Mathf.Sin(a) * radius, center.y, center.z + Mathf.Cos(a) * radius);
            actor.rotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)));
            yield return null;
        }
    }

    private IEnumerator Drift(Transform shade, Vector3 center, float radius, float speed, float startAngle)
    {
        for (float t = 0f; shade != null; t += Time.deltaTime)
        {
            float a = (startAngle + t * speed * 57.3f) * Mathf.Deg2Rad;
            shade.position = center + new Vector3(Mathf.Sin(a) * radius, 1.6f + Mathf.Sin(t * 0.7f) * 0.4f, Mathf.Cos(a) * radius);
            yield return null;
        }
    }

    // The relic's visual rises up out of the altar, then bobs and turns (the holder itself is placed by the carousel;
    // a painted card doesn't turn, it faces the camera)
    private IEnumerator Hover(Transform holder, Transform visual, int index)
    {
        Vector3 rest = visual.localPosition;
        bool card = cards.Contains(visual);
        for (float t = 0f; t < 1f && holder != null; t += Time.deltaTime / 1.2f)
        {
            visual.localPosition = rest + Vector3.down * 1.6f * (1f - t) * (1f - t);
            if (card) FaceCamera(visual);
            yield return null;
        }
        for (float t = 0f; holder != null && holder.gameObject.activeSelf; t += Time.deltaTime)
        {
            visual.localPosition = rest + Vector3.up * Mathf.Sin(t * 1.6f + index) * 0.15f;
            if (card) FaceCamera(visual);
            else visual.Rotate(Vector3.up, 40f * Time.deltaTime, Space.World);
            yield return null;
        }
    }

    private void FaceCamera(Transform t)
    {
        if (cam != null) t.rotation = Quaternion.LookRotation(t.position - cam.transform.position);
    }

    // Where relic i stands on the ring as the carousel turns: the chosen one at the front, toward the camera (−z)
    private Vector3 RingPosition(int i, int n)
    {
        if (n <= 0) return ringCenter;
        float step = 360f / n;
        float angle = (i - spin) * step;
        return ringCenter + Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, -RingRadius);
    }

    private static Material CardMaterial(Texture2D icon)
    {
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var m = new Material(unlit != null ? unlit : Shader.Find("Unlit/Texture"));
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", icon);
        m.mainTexture = icon;
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // seen from either side
        return m;
    }

    private IEnumerator Dissolve(Transform relic, Light light)
    {
        Vector3 s = relic.localScale;
        for (float t = 0f; t < 1f && relic != null; t += Time.deltaTime / 0.6f)
        {
            relic.localScale = s * (1f - t);
            if (light != null) light.intensity *= 0.9f;
            yield return null;
        }
        if (relic != null) relic.gameObject.SetActive(false);
    }

    private IEnumerator Rise(Transform t, float fromY, float toY, float seconds)
    {
        Vector3 p = t.position;
        for (float k = 0f; k < 1f; k += Time.deltaTime / seconds)
        {
            t.position = new Vector3(p.x, p.y + Mathf.Lerp(fromY, toY, 1f - (1f - k) * (1f - k)), p.z);
            yield return null;
        }
        t.position = new Vector3(p.x, p.y + toY, p.z);
    }

    // ───────────── Camera, light, words ─────────────

    private void Look(Vector3 position, Vector3 target)
    {
        camPos = position;
        camLook = target;
    }

    private IEnumerator Move(Vector3 fromPos, Vector3 toPos, Vector3 fromLook, Vector3 toLook, float seconds)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            float k = t * t * (3f - 2f * t);
            Look(Vector3.Lerp(fromPos, toPos, k), Vector3.Lerp(fromLook, toLook, k));
            yield return null;
        }
        Look(toPos, toLook);
    }

    private void Mood(Color fogColor, float density, Color ambientColor, Color lightColor, float intensity, Vector3 lightEuler)
    {
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = density;
        RenderSettings.ambientLight = ambientColor;
        keyLight.color = lightColor;
        keyLight.intensity = intensity;
        keyLight.transform.rotation = Quaternion.Euler(lightEuler);
        if (cam != null) cam.backgroundColor = fogColor;
    }

    private IEnumerator CutTo(Color flash, float seconds)
    {
        yield return Fade(1f, seconds * 0.5f, flash);
        StartCoroutine(Fade(0f, seconds * 0.8f, flash));
    }

    private IEnumerator Fade(float to, float seconds, Color color)
    {
        fadeColor = color;
        float from = fade;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, seconds))
        {
            fade = Mathf.Lerp(from, to, t);
            yield return null;
        }
        fade = to;
    }

    private IEnumerator Letterbox(float to, float seconds)
    {
        float from = letterbox;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            letterbox = Mathf.Lerp(from, to, t);
            yield return null;
        }
        letterbox = to;
    }

    private static IEnumerator Wait(float seconds)
    {
        yield return new WaitForSeconds(seconds);
    }

    private IEnumerator SayLater(float delay, string text, float seconds)
    {
        yield return new WaitForSeconds(delay);
        Say(text, seconds);
    }

    // Narration: fades in, holds, fades out
    private Coroutine sayRoutine, speakRoutine;

    private void Say(string text, float seconds = 0f)
    {
        if (sayRoutine != null) StopCoroutine(sayRoutine);
        if (text == null) { caption = null; captionAlpha = 0f; return; }
        sayRoutine = StartCoroutine(Show(text, seconds, false));
    }

    // A line the hero speaks
    private void Speak(string text, float seconds = 0f, float delay = 0f)
    {
        if (speakRoutine != null) StopCoroutine(speakRoutine);
        if (text == null) { line = null; lineAlpha = 0f; return; }
        speakRoutine = StartCoroutine(Show(text, seconds, true, delay));
    }

    private IEnumerator Show(string text, float seconds, bool spoken, float delay = 0f)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (spoken) line = text; else caption = text;
        Narration.Say(text);   // read aloud
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float a = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((seconds - t) / 0.6f);
            if (spoken) lineAlpha = a; else captionAlpha = a;
            yield return null;
        }
        if (spoken) lineAlpha = 0f; else captionAlpha = 0f;
    }

    private void OnGUI()
    {
        GUI.depth = -200;
        if (captionStyle == null)
        {
            captionStyle = UiFont.Style(22, TextAnchor.MiddleCenter);
            captionStyle.wordWrap = true;
            lineStyle = UiFont.Style(26, TextAnchor.MiddleCenter, FontStyle.Bold);
            titleStyle = UiFont.Style(60, TextAnchor.MiddleCenter, FontStyle.Bold);
            smallStyle = UiFont.Style(13, TextAnchor.MiddleRight);
            promptStyle = UiFont.Style(18, TextAnchor.MiddleCenter, FontStyle.Bold);
            promptStyle.wordWrap = true;
        }
        float bar = Screen.height * 0.1f * letterbox;
        UiFont.Fill(new Rect(0f, 0f, Screen.width, bar), Color.black);
        UiFont.Fill(new Rect(0f, Screen.height - bar, Screen.width, bar), Color.black);

        if (fade > 0f)
            UiFont.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(fadeColor.r, fadeColor.g, fadeColor.b, fade));

        float y = Screen.height - bar - 70f;
        if (caption != null && captionAlpha > 0f)
            Shadowed(new Rect(Screen.width * 0.1f, y, Screen.width * 0.8f, 56f), caption, captionStyle, new Color(0.95f, 0.93f, 0.88f, captionAlpha));
        if (line != null && lineAlpha > 0f)
            Shadowed(new Rect(0f, Screen.height * 0.62f, Screen.width, 40f), $"“{line}”", lineStyle, new Color(1f, 0.86f, 0.55f, lineAlpha));

        if (prompt != null && relics.Count > 0)
        {
            CharacterProfile f = Fighter(choice);
            Color rune = RuneColors[choice % RuneColors.Length];
            Texture2D icon = f != null ? WeaponIcons.For(f.Weapon) : null;
            float pw = Mathf.Min(760f, Screen.width - 32f);
            Rect panel = new Rect((Screen.width - pw) / 2f, bar + 14f, pw, 118f);
            UiFont.Fill(panel, new Color(0f, 0f, 0f, 0.6f));
            // the weapon's picture on the left of the panel, the words beside it
            float textX = panel.x, textW = panel.width;
            if (icon != null)
            {
                Rect pic = new Rect(panel.x + 10f, panel.y + 9f, 100f, 100f);
                UiFont.Fill(new Rect(pic.x - 2f, pic.y - 2f, pic.width + 4f, pic.height + 4f), new Color(rune.r, rune.g, rune.b, 0.85f));
                GUI.DrawTexture(pic, icon, ScaleMode.ScaleToFit);
                textX = pic.xMax + 8f;
                textW = panel.xMax - textX - 8f;
            }
            Shadowed(new Rect(textX, panel.y + 6f, textW, 26f), $"{prompt}  ·  {choice + 1}/{relics.Count}", promptStyle, Color.white);
            if (f != null)
            {
                Shadowed(new Rect(textX, panel.y + 32f, textW, 24f), $"{f.WeaponName}  —  {f.DisplayName}", promptStyle, rune);
                descStyle ??= new GUIStyle(captionStyle) { fontSize = 14 };
                Shadowed(new Rect(textX + 6f, panel.y + 58f, textW - 12f, 56f), f.WeaponDescription, descStyle, new Color(0.9f, 0.88f, 0.82f));
            }
            if (!confirming)
                Shadowed(new Rect(0f, Screen.height - bar - 34f, Screen.width, 24f), "[A/D · ←/→] 돌리기   [마우스] 가리키면 앞으로   [Enter · 클릭] 선택", promptStyle, new Color(1f, 1f, 1f, 0.8f));
            else if (f != null)
                DrawConfirm(f, icon, rune);
        }

        if (titleAlpha > 0f)
        {
            Shadowed(new Rect(0f, Screen.height * 0.4f, Screen.width, 80f), Title, titleStyle, new Color(Gold.r, Gold.g, Gold.b, titleAlpha));
        }
        if (skipHint > 0.01f)
            Shadowed(new Rect(0f, Screen.height - bar + 4f, Screen.width - 16f, 20f), "[Esc] 건너뛰기", smallStyle, new Color(1f, 1f, 1f, 0.6f * skipHint));
    }

    // The "really take it?" box in the middle of the screen: the weapon's picture, its name, and 예 / 아니요
    private GUIStyle confirmButton;

    private void DrawConfirm(CharacterProfile f, Texture2D icon, Color rune)
    {
        float w = Mathf.Min(460f, Screen.width - 40f), h = 230f;
        Rect box = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f + 40f, w, h);
        UiFont.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
        UiFont.Fill(new Rect(box.x - 2f, box.y - 2f, box.width + 4f, box.height + 4f), new Color(rune.r, rune.g, rune.b, 0.9f));
        UiFont.Fill(box, new Color(0.05f, 0.05f, 0.08f, 0.96f));
        float textX = box.x + 20f;
        if (icon != null)
        {
            Rect pic = new Rect(box.x + 18f, box.y + 22f, 110f, 110f);
            GUI.DrawTexture(pic, icon, ScaleMode.ScaleToFit);
            textX = pic.xMax + 16f;
        }
        float textW = box.xMax - textX - 16f;
        Shadowed(new Rect(textX, box.y + 26f, textW, 30f), f.WeaponName, promptStyle, rune);
        Shadowed(new Rect(textX, box.y + 56f, textW, 24f), f.DisplayName, descStyle ?? promptStyle, new Color(0.85f, 0.83f, 0.78f));
        Shadowed(new Rect(textX, box.y + 90f, textW, 40f), "이 무기로 여정을 시작할까요?", promptStyle, Color.white);

        confirmButton ??= new GUIStyle(GUI.skin.button) { font = UiFont.Get(), fontSize = 18, fontStyle = FontStyle.Bold };
        Rect yes = new Rect(box.center.x - 150f, box.yMax - 62f, 140f, 42f);
        Rect no = new Rect(box.center.x + 10f, box.yMax - 62f, 140f, 42f);
        if (GUI.Button(yes, "예  [Enter]", confirmButton)) chosen = true;
        if (GUI.Button(no, "아니요  [Esc]", confirmButton)) confirming = false;
    }

    private static void Shadowed(Rect r, string text, GUIStyle style, Color color)
    {
        Color old = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
        style.normal.textColor = old;
    }
}
