#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Portfolio video (temporary): plays a short scripted run with the twin blades — title, village, Asgard halls,
// the mid-boss and Heimdall — while CaptureRecorderHook (Editor) records the game view. Editor only; delete after.
public class CaptureDirector : MonoBehaviour
{
    public static bool WantRecord;
    public static bool Done;
    public static string OutputPath;
    public static string Log = "";

    private static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private GameManager gm;
    private DualBlades blades;
    private Transform player;
    private InputSettings originalSettings;
    private Key[] held = new Key[0];
    private bool fighting;
    private float nextSkillAt;
    private int skillTurn;
    private string logPath;

    public static void Begin(string outputPath, string logFile)
    {
        OutputPath = outputPath;
        Done = false;
        Log = "";
        var d = new GameObject("CaptureDirector").AddComponent<CaptureDirector>();
        d.logPath = logFile;
    }

    private void Note(string s)
    {
        string line = $"[{Time.time:0.0}] {s}";
        Log += line + "\n";
        try { if (logPath != null) File.AppendAllText(logPath, line + "\n"); } catch { }
    }

    private void Start()
    {
        Application.runInBackground = true;
        originalSettings = InputSystem.settings;
        var c = Instantiate(InputSystem.settings);
        c.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        c.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = c;
        DebugCheats.Invincible = true;
        var listener = FindAnyObjectByType<AudioListener>();
        if (listener != null && listener.GetComponent<AudioTap>() == null) listener.gameObject.AddComponent<AudioTap>();
        Note("listener " + (listener != null ? listener.name : "none"));
        gm = GameManager.Instance;
        StartCoroutine(Script());
        StartCoroutine(Grab());
    }

    // The editor hook's encoder: each finished frame (RGBA32) goes straight to it, so nothing piles up
    public static System.Action<Texture2D> Sink;
    public static System.Action<float[]> AudioSink;
    public static int Frames;
    private Texture2D rgba;
    private float recStart = -1f;

    // Real time (so the sound stays in step): 30 frames a second of wall clock, a frame repeated when the game runs slower
    private IEnumerator Grab()
    {
        var eof = new WaitForEndOfFrame();
        while (true)
        {
            yield return eof;
            if (!WantRecord || Sink == null) { AudioTap.Recording = false; continue; }
            if (recStart < 0f)
            {
                recStart = Time.realtimeSinceStartup;
                AudioTap.Drain();
                AudioTap.Recording = true;
            }
            int target = Mathf.FloorToInt((Time.realtimeSinceStartup - recStart) * 30f) + 1;
            if (Frames >= target) { PushAudio(); continue; }
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (shot == null) continue;
            int w = shot.width & ~1, h = shot.height & ~1;
            if (rgba == null) rgba = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Texture2D use = rgba;
            if (shot.format == TextureFormat.RGBA32 && shot.width == rgba.width && shot.height == rgba.height) use = shot;
            else
            {
                Color32[] src = shot.GetPixels32();
                Color32[] dst = new Color32[rgba.width * rgba.height];
                for (int y = 0; y < rgba.height; y++)
                    System.Array.Copy(src, y * shot.width, dst, y * rgba.width, rgba.width);
                rgba.SetPixels32(dst);
                rgba.Apply(false);
            }
            while (Frames < target) { Sink(use); Frames++; }
            Destroy(shot);
            PushAudio();
        }
    }

    private void PushAudio()
    {
        float[] a = AudioTap.Drain();
        if (a.Length > 0 && AudioSink != null) AudioSink(a);
    }

    private void OnDestroy()
    {
        Restore();
    }

    private void Restore()
    {
        SkillCast.AimOverride = null;
        DebugCheats.Invincible = false;
        DebugCheats.DamageBonus = 0;
        AudioTap.Recording = false;
        if (originalSettings != null) { InputSystem.settings = originalSettings; originalSettings = null; }
        WantRecord = false;
    }

    // ── input ──
    private void Hold(params Key[] keys) => held = keys;

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (!kb.enabled) InputSystem.EnableDevice(kb);
        bool same = true;
        foreach (Key k in held) if (!kb[k].isPressed) same = false;
        if (held.Length == 0 && kb.anyKey.isPressed) same = false;
        if (!same) InputSystem.QueueStateEvent(kb, new KeyboardState(held));
        if (fighting) Fight();
    }

    private void MoveToward(Vector3 to, bool run)
    {
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) { Hold(); return; }
        float a = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;   // 0 = +z (W), 90 = +x (D)
        int oct = Mathf.RoundToInt(a / 45f);
        oct = ((oct % 8) + 8) % 8;
        var keys = new System.Collections.Generic.List<Key>();
        if (oct == 7 || oct == 0 || oct == 1) keys.Add(Key.W);
        if (oct >= 3 && oct <= 5) keys.Add(Key.S);
        if (oct >= 1 && oct <= 3) keys.Add(Key.D);
        if (oct >= 5 && oct <= 7) keys.Add(Key.A);
        if (run) keys.Add(Key.LeftShift);
        Hold(keys.ToArray());
    }

    // ── fighting: the nearest enemy, closing in, the combo, the skills in turn ──
    private void Fight()
    {
        if (blades == null || player == null) return;
        Enemy target = null;
        float best = float.MaxValue;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead) continue;
            float d = (e.transform.position - player.position).sqrMagnitude;
            if (d < best) { best = d; target = e; }
        }
        if (target == null) { Hold(); SkillCast.AimOverride = null; return; }

        Vector3 at = target.transform.position;
        Vector3 to = at - player.position; to.y = 0f;
        float dist = to.magnitude;
        float reach = 2.0f;
        var col = target.GetComponentInChildren<Collider>();
        if (col != null) reach += Mathf.Min(3f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.z));
        SkillCast.AimOverride = at;
        bool busy = blades.EditorTestBusy;

        if (dist > reach)
        {
            if (!busy && dist < 9f && Time.time >= nextSkillAt && blades.EditorTestLearned(DualBlades.Skill.Dash) && blades.Ready(DualBlades.Skill.Dash))
            {
                Hold();
                blades.EditorTestUse(DualBlades.Skill.Dash);
                nextSkillAt = Time.time + 1.2f;
                return;
            }
            if (!busy) MoveToward(to, true); else Hold();
            return;
        }
        Hold();
        if (busy) return;
        if (Time.time >= nextSkillAt)
        {
            DualBlades.Skill[] order = { DualBlades.Skill.Flurry, DualBlades.Skill.Berserk, DualBlades.Skill.Dash, DualBlades.Skill.Dodge };
            for (int i = 0; i < order.Length; i++)
            {
                var s = order[(skillTurn + i) % order.Length];
                if (blades.EditorTestLearned(s) && blades.Ready(s))
                {
                    if (s == DualBlades.Skill.Dodge) player.rotation = Quaternion.LookRotation(-to.normalized);
                    blades.EditorTestUse(s);
                    skillTurn = (skillTurn + i + 1) % order.Length;
                    nextSkillAt = Time.time + Random.Range(1.6f, 2.6f);
                    return;
                }
            }
            nextSkillAt = Time.time + 1f;
        }
        blades.Tap(at);
    }

    // ── helpers on the game manager ──
    private T Get<T>(string field) => (T)typeof(GameManager).GetField(field, Any).GetValue(gm);
    private object Call(string method, params object[] args)
    {
        var m = typeof(GameManager).GetMethods(Any).First(x => x.Name == method && x.GetParameters().Length == args.Length);
        return m.Invoke(gm, args);
    }
    private bool Travelling => Get<bool>("travelling");
    private Room Here => gm.CurrentRoom;
    private DungeonGenerator Dungeon => Get<DungeonGenerator>("dungeon");

    private void FindPlayer()
    {
        blades = FindAnyObjectByType<DualBlades>();
        player = blades != null ? blades.transform : null;
    }

    private static float Dt => Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Time.unscaledDeltaTime;
    private IEnumerator Wait(float s) { for (float t = 0f; t < s; t += Dt) yield return null; }

    private IEnumerator WaitSettled(float max)
    {
        yield return Wait(0.5f);
        for (float t = 0f; t < max && (Travelling || BossCutscene.Playing); t += Dt) yield return null;
        FindPlayer();
    }

    private IEnumerator TravelToRoom(Room room)
    {
        Room neighbour = room.Neighbours.FirstOrDefault(n => n.Exits.ContainsValue(room));
        if (neighbour == null) { Note("no neighbour for " + room.RoomKind); yield break; }
        Vector2Int side = neighbour.Exits.First(e => e.Value == room).Key;
        var routine = (IEnumerator)Call("TravelTo", room, -side);
        gm.StartCoroutine(routine);
        yield return WaitSettled(10f);
    }

    private IEnumerator FightRoom(float max)
    {
        fighting = true;
        float idle = 0f;
        for (float t = 0f; t < max; t += Dt)
        {
            if (BossCutscene.Playing || VisualNovel.Playing) { Hold(); yield return null; continue; }
            bool any = Enemy.Alive.Any(e => e != null && !e.IsDead);
            if (!any)
            {
                idle += Dt;
                if (Here != null && Here.Cleared && idle > 1.2f) break;
                if (Here != null && idle > 1.5f) MoveToward(Here.transform.position - player.position, false);
            }
            else idle = 0f;
            yield return null;
        }
        fighting = false;
        Hold();
        SkillCast.AimOverride = null;
    }

    // A boss through every phase: if a phase drags on, the blades hit harder (DebugCheats.DamageBonus) so each phase
    // shows its patterns for a while and then gives way to the next
    private IEnumerator BossFight(string typeName, float max)
    {
        fighting = true;
        bool seen = false;
        int lastPhase = -1;
        float phaseT = 0f, goneT = 0f, idle = 0f, nextBump = 0f;
        DebugCheats.DamageBonus = 4;
        for (float t = 0f; t < max; t += Dt)
        {
            if (BossCutscene.Playing || VisualNovel.Playing)
            {
                Hold();
                if (VisualNovel.Playing && VisualNovel.Current != null && (phaseT += Dt) > 6f) VisualNovel.Current.EditorSkip();
                yield return null; continue;
            }
            if (player == null || blades == null || !blades.isActiveAndEnabled) FindPlayer();
            Enemy boss = Enemy.Alive.FirstOrDefault(e => e != null && !e.IsDead && e.GetType().Name == typeName);
            if (boss != null)
            {
                seen = true; goneT = 0f;
                var prop = boss.GetType().GetProperty("Phase");
                int ph = prop != null ? (int)prop.GetValue(boss) : 0;
                if (ph != lastPhase)
                {
                    lastPhase = ph; phaseT = 0f; nextBump = 13f;
                    DebugCheats.DamageBonus = 4;
                    Note($"{typeName} phase {ph} hp {boss.Health}/{boss.MaxHealth}");
                }
                phaseT += Dt;
                if (phaseT > nextBump) { DebugCheats.DamageBonus += 8; nextBump = phaseT + 2.5f; }
            }
            else if (seen)
            {
                goneT += Dt;
                if (goneT > 3f) { Note(typeName + " down"); break; }
            }
            else if (!Enemy.Alive.Any(e => e != null && !e.IsDead))
            {
                idle += Dt;
                if (Here != null && idle > 1.5f) MoveToward(Here.transform.position - player.position, false);
            }
            yield return null;
        }
        fighting = false;
        DebugCheats.DamageBonus = 0;
        Hold();
        SkillCast.AimOverride = null;
    }

    private IEnumerator Script()
    {
        Note("start");
        FindPlayer();
        // 1) the title screen, then 시작하기
        WantRecord = true;
        yield return Wait(4.5f);
        var title = FindAnyObjectByType<TitleScreen>();
        if (title != null) typeof(TitleScreen).GetMethod("Pick", Any).Invoke(title, new object[] { 0 });
        yield return Wait(1.4f);
        FindPlayer();
        Note("village");
        // 2) the village: a few steps north toward the Bifrost
        MoveToward(Vector3.forward + Vector3.right * 0.3f, false);
        yield return Wait(2.5f);
        MoveToward(Vector3.forward, true);
        yield return Wait(2.5f);
        Hold();

        // 3) Asgard
        Realm[] realms = Get<Realm[]>("realms");
        int asgard = System.Array.FindIndex(realms, r => r.palace);
        Note("asgard index " + asgard);
        Call("TestGoToFloor", asgard);
        yield return WaitSettled(15f);
        yield return Wait(1.5f);
        FindPlayer();
        Note("arrived " + (Here != null ? Here.RoomKind + " " + Here.Shape : "-"));
        MoveToward(Vector3.forward, false);
        yield return Wait(3f);
        Hold();

        // 4) a fight hall
        var shown = new System.Collections.Generic.HashSet<string>();
        for (int n = 0; n < 1; n++)
        {
            Room hall = Dungeon.Rooms.FirstOrDefault(r => r.RoomKind == Room.Kind.Combat && !r.Visited && !r.IsMidBossFight && !shown.Contains(r.Shape))
                        ?? Dungeon.Rooms.FirstOrDefault(r => r.RoomKind == Room.Kind.Combat && !r.Visited && !r.IsMidBossFight);
            if (hall == null) { Note("no more fight halls"); break; }
            shown.Add(hall.Shape);
            Note("hall " + hall.Shape);
            yield return TravelToRoom(hall);
            yield return Wait(0.8f);
            yield return FightRoom(28f);
            Note("hall done, cleared=" + (Here != null && Here.Cleared));
            yield return Wait(1.2f);
        }

        // 5) the mid-boss (Týr)
        Note("midboss");
        Call("TestGoToBoss", asgard, Room.Kind.MidBoss);
        yield return Wait(1f);
        yield return WaitSettled(25f);
        yield return SkipTalk(7f);
        yield return BossFight("TyrBoss", 75f);

        // 6) Heimdall: his entrance, then the sky arena
        Note("boss");
        Call("TestGoToBoss", asgard, Room.Kind.Boss);
        yield return Wait(1f);
        for (float t = 0f; t < 15f && Travelling; t += Dt) yield return null;
        yield return SkipTalk(10f);
        for (float t = 0f; t < 25f && BossCutscene.Playing; t += Dt) yield return null;
        FindPlayer();
        yield return BossFight("HeimdallBoss", 110f);

        Note("end");
        Hold();
        yield return Wait(4f);
        WantRecord = false;
        yield return Wait(1f);
        Done = true;
        Restore();
    }

    // Let a conversation play a while, then skip the rest
    private IEnumerator SkipTalk(float show)
    {
        for (float t = 0f; t < 20f && !VisualNovel.Playing && !BossCutscene.Playing; t += Dt) yield return null;
        float shown = 0f;
        while (VisualNovel.Playing || BossCutscene.Playing)
        {
            shown += Dt;
            if (VisualNovel.Playing && shown > show) VisualNovel.Current.EditorSkip();
            if (shown > 40f) break;
            yield return null;
        }
    }
}
#endif
