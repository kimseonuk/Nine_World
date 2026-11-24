using System.Collections.Generic;
using UnityEngine;

// ───────────────────────────────────────────────────────────────────────────────────────────────────────────────
// The game's sound (VARCO Sound). Four channels:
//   · effects   GameAudio.Play(key)          Resources/Audio/<key>_<n>.wav, several takes picked at random
//   · loops     GameAudio.Loop(host, key)    a looping 3D source on a thing (a portal, a brazier, a fountain, a rotor)
//   · music     MusicDirector                Resources/Music/<key>, crossfaded by what is happening (title, village,
//                                             halls, battle, boss, the end)
//   · voice     Narration.Say(text, who)     Resources/Voice/v_<hash>.wav for each story line; the music dips under it
// Effects are flat (2D) and quieter the farther they happen from the player; a sound can't pile up on itself.
// ───────────────────────────────────────────────────────────────────────────────────────────────────────────────
public static class GameAudio
{
    public static float Volume = 0.9f;        // effects
    public static float MusicVolume = 0.55f;
    public static float VoiceVolume = 1f;

    private class Entry
    {
        public AudioClip[] clips;
        public float volume = 1f, pitchJitter = 0.06f, minGap = 0.03f, last = -10f;
        public int maxVoices = 4, lastIndex = -1;
    }

    // key → (volume, pitch jitter, min gap between starts, max voices at once)
    private static readonly Dictionary<string, (float vol, float jit, float gap, int voices)> Tuning = new Dictionary<string, (float, float, float, int)>
    {
        // combat
        { "swing", (0.55f, 0.08f, 0.05f, 3) }, { "swing2", (0.65f, 0.06f, 0.05f, 2) },
        { "hit", (0.8f, 0.09f, 0.035f, 5) }, { "hitheavy", (1f, 0.05f, 0.07f, 2) }, { "kill", (0.8f, 0.08f, 0.05f, 3) },
        { "hurt", (0.9f, 0.06f, 0.15f, 1) }, { "skill", (0.75f, 0.06f, 0.06f, 3) }, { "slam", (0.8f, 0.06f, 0.08f, 2) },
        { "roar", (0.75f, 0.07f, 0.4f, 1) }, { "shoot", (0.4f, 0.1f, 0.05f, 3) }, { "dash", (0.5f, 0.08f, 0.1f, 2) },
        { "lightning", (0.6f, 0.08f, 0.08f, 2) }, { "explosion", (0.7f, 0.07f, 0.08f, 3) }, { "block", (0.7f, 0.06f, 0.08f, 2) },
        { "chains", (0.55f, 0.08f, 0.15f, 2) }, { "bell", (0.6f, 0.03f, 0.4f, 1) }, { "laser", (0.55f, 0.05f, 0.2f, 2) },
        { "whoosh_big", (0.6f, 0.06f, 0.1f, 2) }, { "spawn", (0.45f, 0.1f, 0.12f, 2) },
        // pickups & rewards
        { "coin", (0.45f, 0.1f, 0.04f, 3) }, { "xp", (0.35f, 0.12f, 0.04f, 3) }, { "levelup", (0.85f, 0f, 0.6f, 1) },
        { "clear", (0.85f, 0f, 1f, 1) }, { "boon", (0.85f, 0f, 0.5f, 1) }, { "orb_pickup", (0.7f, 0.05f, 0.2f, 1) },
        { "rune_activate", (0.7f, 0.04f, 0.3f, 1) }, { "chest_open", (0.8f, 0f, 0.5f, 1) }, { "coins_spill", (0.6f, 0.05f, 0.3f, 1) },
        { "anvil", (0.6f, 0.06f, 0.25f, 2) }, { "upgrade", (0.8f, 0f, 0.4f, 1) }, { "heal", (0.6f, 0.05f, 0.3f, 1) },
        { "token", (0.7f, 0.03f, 0.2f, 1) },
        // world
        { "portal_enter", (0.75f, 0.03f, 0.5f, 1) }, { "gate_close", (0.75f, 0.03f, 0.5f, 1) }, { "gate_open", (0.65f, 0.03f, 0.5f, 1) },
        { "seal_break", (0.65f, 0.05f, 0.3f, 1) }, { "realm_enter", (0.7f, 0f, 1f, 1) },
        { "step_stone", (0.22f, 0.12f, 0.12f, 2) }, { "step_grass", (0.25f, 0.12f, 0.12f, 2) }, { "heartbeat", (0.55f, 0f, 0.5f, 1) },
        { "player_death", (0.9f, 0f, 1f, 1) },
        // bosses
        { "boss_sting", (0.9f, 0f, 1f, 1) }, { "boss_awaken", (0.9f, 0f, 1f, 1) }, { "boss_death", (1f, 0f, 1f, 1) },
        { "god_shout", (0.85f, 0.04f, 1.2f, 1) }, { "god_laugh", (0.85f, 0f, 3f, 1) }, { "lion_roar", (0.85f, 0.04f, 1.5f, 1) },
        // UI
        { "ui_move", (0.35f, 0.06f, 0.04f, 2) }, { "ui_select", (0.55f, 0.03f, 0.06f, 2) }, { "ui_back", (0.45f, 0.03f, 0.06f, 1) },
        { "ui_open", (0.45f, 0.04f, 0.15f, 1) }, { "ui_close", (0.4f, 0.04f, 0.15f, 1) }, { "ui_error", (0.5f, 0.02f, 0.2f, 1) },
        { "ui_buy", (0.6f, 0.04f, 0.15f, 1) }, { "ui_type", (0.18f, 0.15f, 0.05f, 2) }, { "ui_toast", (0.35f, 0.02f, 0.4f, 1) },
        { "ui_card", (0.6f, 0.02f, 0.3f, 1) }, { "ui_pick", (0.65f, 0.03f, 0.2f, 1) }, { "ui_map", (0.55f, 0f, 0.4f, 1) },
        { "ui_mapsel", (0.6f, 0.02f, 0.3f, 1) }, { "ui_start", (0.85f, 0f, 1f, 1) }, { "ui_reroll", (0.6f, 0.04f, 0.2f, 1) },
        // opening
        { "cine_tree", (0.8f, 0f, 1f, 1) }, { "cine_corrupt", (0.8f, 0f, 1f, 1) }, { "cine_chaos", (0.8f, 0f, 1f, 1) },
        { "cine_stomp", (0.85f, 0.05f, 0.3f, 2) }, { "cine_rocks", (0.75f, 0.04f, 0.5f, 1) }, { "cine_erupt", (0.85f, 0f, 1f, 1) },
        { "cine_birds", (0.6f, 0.04f, 1f, 1) }, { "cine_collapse", (0.9f, 0f, 1f, 1) }, { "cine_fall", (0.8f, 0f, 1f, 1) },
        { "cine_relic", (0.6f, 0f, 1f, 1) }, { "cine_chosen", (0.9f, 0f, 1f, 1) },
        // voices (enemy families, the player)
        { "hm_atk", (0.55f, 0.08f, 0.2f, 2) }, { "hm_hurt", (0.5f, 0.08f, 0.15f, 2) }, { "hm_die", (0.6f, 0.07f, 0.1f, 2) },
        { "hf_atk", (0.55f, 0.07f, 0.2f, 2) }, { "hf_hurt", (0.5f, 0.07f, 0.15f, 2) }, { "hf_die", (0.6f, 0.06f, 0.1f, 2) },
        { "bst_atk", (0.65f, 0.07f, 0.3f, 1) }, { "bst_hurt", (0.55f, 0.08f, 0.2f, 2) }, { "bst_die", (0.7f, 0.05f, 0.2f, 1) },
        { "bird_atk", (0.5f, 0.08f, 0.25f, 2) }, { "bird_hurt", (0.5f, 0.08f, 0.15f, 2) }, { "bird_die", (0.6f, 0.06f, 0.2f, 2) },
        { "gnt_atk", (0.8f, 0.05f, 0.5f, 1) }, { "gnt_hurt", (0.6f, 0.06f, 0.3f, 1) }, { "gnt_die", (0.85f, 0.04f, 0.5f, 1) },
        { "mch_atk", (0.5f, 0.07f, 0.2f, 2) }, { "mch_hurt", (0.45f, 0.1f, 0.12f, 3) }, { "mch_die", (0.65f, 0.06f, 0.15f, 2) },
        { "rune_atk", (0.5f, 0.08f, 0.2f, 2) }, { "rune_hurt", (0.45f, 0.1f, 0.12f, 3) }, { "rune_die", (0.6f, 0.07f, 0.15f, 2) },
        { "pv_hurt", (0.6f, 0.06f, 0.4f, 1) }, { "pv_atk", (0.35f, 0.08f, 0.35f, 1) }, { "pv_die", (0.8f, 0f, 1f, 1) },
        { "pv_dash", (0.35f, 0.08f, 0.3f, 1) }, { "pv_heal", (0.45f, 0.05f, 1f, 1) }, { "pv_levelup", (0.55f, 0.03f, 1f, 1) },
        // weapons' charge, equip; rewards and the room
        { "crit_hit", (0.7f, 0.04f, 0.05f, 2) }, { "dummy_hit", (0.55f, 0.1f, 0.04f, 3) },
        { "charge_hold", (0.55f, 0.03f, 0.3f, 1) }, { "charge_full", (0.6f, 0.02f, 0.2f, 1) }, { "bow_draw", (0.5f, 0.05f, 0.2f, 1) },
        { "equip_blade", (0.55f, 0.03f, 0.3f, 1) }, { "equip_heavy", (0.55f, 0.03f, 0.3f, 1) }, { "equip_bow", (0.55f, 0.03f, 0.3f, 1) },
        { "heal_orb", (0.5f, 0.06f, 0.08f, 2) }, { "skill_learn", (0.65f, 0.03f, 0.15f, 1) }, { "skill_refund", (0.6f, 0f, 0.5f, 1) },
        { "ui_locked", (0.45f, 0.03f, 0.1f, 1) }, { "fight_drum", (0.8f, 0f, 1.5f, 1) }, { "elite_sting", (0.85f, 0f, 2f, 1) },
        { "reveal_sparkle", (0.6f, 0.03f, 0.3f, 1) }, { "pedestal_hum", (1f, 0f, 0f, 4) },
        { "boss_warn", (0.75f, 0f, 0.8f, 1) }, { "boss_phase", (0.9f, 0f, 2f, 1) }, { "boss_stagger", (0.8f, 0.03f, 0.5f, 1) },
    };

    private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
    private static readonly Dictionary<string, List<AudioSource>> playing = new Dictionary<string, List<AudioSource>>();
    private static readonly List<AudioSource> pool = new List<AudioSource>();
    private static GameObject host;
    private static Transform listenerTarget;

    public static GameObject Host
    {
        get
        {
            if (host == null)
            {
                host = new GameObject("GameAudio");
                Object.DontDestroyOnLoad(host);
                pool.Clear();
                playing.Clear();
            }
            return host;
        }
    }

    public static AudioClip[] Clips(string key) => Get(key).clips;

    private static Entry Get(string key)
    {
        if (entries.TryGetValue(key, out Entry e)) return e;
        e = new Entry();
        var found = new List<AudioClip>();
        for (int i = 0; i < 8; i++)
        {
            var c = Resources.Load<AudioClip>("Audio/" + key + "_" + i);
            if (c != null) found.Add(c);
        }
        e.clips = found.ToArray();
        if (Tuning.TryGetValue(key, out var t) || Guess(key, out t))
        {
            e.volume = t.vol; e.pitchJitter = t.jit; e.minGap = t.gap; e.maxVoices = t.voices;
        }
        entries[key] = e;
        return e;
    }

    // The levels of the larger sets, by how their keys are named: each monster's own voice (<own>_atk/_skill/_hurt/_die),
    // the bosses' moves, each weapon's swing/heavy/hit, what a blow lands on, the far-off ambience
    private static readonly HashSet<string> BossSets = new HashSet<string> { "tyr", "heim", "lion", "boss" };

    private static bool Guess(string key, out (float vol, float jit, float gap, int voices) t)
    {
        t = (1f, 0.06f, 0.03f, 4);
        int cut = key.IndexOf('_');
        if (cut < 0) return false;
        string head = key.Substring(0, cut), tail = key.Substring(key.LastIndexOf('_') + 1);
        if (head == "amb") { t = (1f, 0f, 2f, 1); return true; }
        if (head == "hit") { t = (0.55f, 0.1f, 0.03f, 4); return true; }
        if (head == "w")
        {
            t = tail == "hit" ? (0.6f, 0.08f, 0.03f, 4) : tail == "heavy" ? (0.7f, 0.05f, 0.05f, 2) : (0.55f, 0.08f, 0.05f, 3);
            return true;
        }
        if (BossSets.Contains(head))
        {
            t = tail == "hurt" ? (0.6f, 0.05f, 0.3f, 1) : tail == "atk" ? (0.75f, 0.04f, 0.4f, 1) : (0.85f, 0.02f, 0.25f, 2);
            return true;
        }
        switch (tail)
        {
            case "atk": t = (0.6f, 0.06f, 0.2f, 2); return true;
            case "skill": t = (0.75f, 0.04f, 0.5f, 1); return true;
            case "hurt": t = (0.5f, 0.07f, 0.15f, 2); return true;
            case "die": t = (0.65f, 0.05f, 0.1f, 2); return true;
        }
        return false;
    }

    private static AudioSource FreeSource()
    {
        GameObject h = Host;
        foreach (AudioSource s in pool)
            if (s != null && !s.isPlaying) return s;
        if (pool.Count >= 28)
        {
            AudioSource oldest = pool[0];
            pool.RemoveAt(0);
            pool.Add(oldest);
            oldest.Stop();
            return oldest;
        }
        var src = h.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        pool.Add(src);
        return src;
    }

    public static Transform Listener
    {
        get
        {
            if (listenerTarget == null)
            {
                var p = Object.FindAnyObjectByType<PlayerStats>();
                if (p != null) listenerTarget = p.transform;
            }
            return listenerTarget;
        }
    }

    // Full nearby, fading out 32 m away
    private static float Distance(Vector3? at)
    {
        if (!at.HasValue || Listener == null) return 1f;
        Vector3 d = Listener.position - at.Value;
        d.y = 0f;
        return Mathf.Clamp01(1f - (d.magnitude - 8f) / 24f);
    }

    public static bool Has(string key) => Get(key).clips.Length > 0;

    private static bool LoudKey(string key) =>
        key.StartsWith("hm_") || key.StartsWith("hmw_") || key.StartsWith("ty_") || key.StartsWith("tyw_")
        || key.StartsWith("heim_") || key.StartsWith("tyr_") || key.StartsWith("lion_") || key == "boss_warn";

    public static AudioSource Play(string key, Vector3? at = null, float volume = 1f, float pitch = 1f)
    {
        if (string.IsNullOrEmpty(key)) return null;
        Entry e = Get(key);
        if (e.clips.Length == 0) return null;
        float now = Time.unscaledTime;
        if (now - e.last < e.minGap) return null;
        // (the bosses' moves are always heard in full: a warning that fades with distance is no warning)
        float loud = volume * e.volume * Volume * (LoudKey(key) ? 1f : Distance(at));
        if (loud < 0.03f) return null;

        if (!playing.TryGetValue(key, out var voices)) playing[key] = voices = new List<AudioSource>();
        voices.RemoveAll(s => s == null || !s.isPlaying || s.clip == null || !s.clip.name.StartsWith(key + "_"));
        if (voices.Count >= e.maxVoices)
        {
            voices[0].Stop();
            voices.RemoveAt(0);
        }

        int index = Random.Range(0, e.clips.Length);
        if (e.clips.Length > 1 && index == e.lastIndex) index = (index + 1) % e.clips.Length;
        e.lastIndex = index;
        e.last = now;

        AudioSource src = FreeSource();
        src.clip = e.clips[index];
        src.volume = Mathf.Clamp01(loud);
        src.pitch = pitch * (1f + Random.Range(-e.pitchJitter, e.pitchJitter));
        src.loop = false;
        src.panStereo = 0f;
        src.Play();
        voices.Add(src);
        return src;
    }

    // A looping sound on a thing: heard only near it (3D, fading out by maxDistance)
    public static AudioSource Loop(GameObject on, string key, float volume = 0.5f, float maxDistance = 12f, float pitch = 1f)
    {
        if (on == null) return null;
        AudioClip[] clips = Get(key).clips;
        if (clips.Length == 0) return null;
        var src = on.AddComponent<AudioSource>();
        src.clip = clips[Random.Range(0, clips.Length)];
        src.loop = true;
        src.playOnAwake = true;    // (picks up again when its room is shown again)
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = Mathf.Min(2.5f, maxDistance * 0.3f);
        src.maxDistance = maxDistance;
        src.dopplerLevel = 0f;
        src.priority = 200;
        src.volume = volume * Volume;
        src.pitch = pitch * Random.Range(0.97f, 1.03f);
        src.time = Random.Range(0f, src.clip.length * 0.9f);
        src.Play();
        return src;
    }

    // ── game-wide moments, wired once ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Wire()
    {
        entries.Clear();
        Room.RoomCleared -= OnRoomCleared;
        Room.RoomCleared += OnRoomCleared;
        Room.FightStarted -= OnFightStarted;
        Room.FightStarted += OnFightStarted;
        RunState.LeveledUp -= OnLeveledUp;
        RunState.LeveledUp += OnLeveledUp;
        if (Object.FindAnyObjectByType<AudioWatcher>() == null)
        {
            var go = new GameObject("AudioWatcher");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<AudioWatcher>();
            go.AddComponent<MusicDirector>();
        }
    }

    private static void OnRoomCleared(Room room)
    {
        if (room.RoomKind == Room.Kind.Combat || room.RoomKind == Room.Kind.MidBoss || room.RoomKind == Room.Kind.Boss)
        {
            Play("clear", null, room.RoomKind == Room.Kind.Combat ? 0.8f : 1f);
            Play("gate_open", null, 0.8f);
        }
    }

    private static void OnFightStarted(Room room)
    {
        Play("gate_close");
        // the war drum and horn: a fight begins (a boss's own entrance has its own sting)
        if (room.RoomKind == Room.Kind.Combat) Play("fight_drum", null, 0.75f);
    }

    private static void OnLeveledUp(int level)
    {
        Play("levelup");
        Play("pv_levelup", null, 0.8f);
    }
}

// ───────────── music & the ambience bed ─────────────
// Chooses the score from what is on screen and crossfades between pieces; the ambience bed under it likewise.
public class MusicDirector : MonoBehaviour
{
    public static MusicDirector Instance { get; private set; }

    // A cutscene may hold its own ambience (the opening's blizzard)
    public static string AmbienceOverride;

    private AudioSource[] music = new AudioSource[2], amb = new AudioSource[2];
    private int musicIdx, ambIdx;
    private string musicKey = "", ambKey = "";
    private float duck = 1f;          // dips while a voice speaks
    private float battleUntil;        // the fight music lingers a little after the last kill
    private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

    private void Awake()
    {
        Instance = this;
        for (int i = 0; i < 2; i++)
        {
            music[i] = gameObject.AddComponent<AudioSource>();
            music[i].loop = true; music[i].playOnAwake = false; music[i].volume = 0f; music[i].ignoreListenerPause = true;
            amb[i] = gameObject.AddComponent<AudioSource>();
            amb[i].loop = true; amb[i].playOnAwake = false; amb[i].volume = 0f;
        }
    }

    private AudioClip Clip(string folder, string key)
    {
        string path = folder + "/" + key;
        if (!clips.TryGetValue(path, out AudioClip c))
        {
            c = folder == "Music" ? Resources.Load<AudioClip>(path) : (GameAudio.Clips(key).Length > 0 ? GameAudio.Clips(key)[0] : null);
            clips[path] = c;
        }
        return c;
    }

    private string Pick(string key, string fallback) => Clip("Music", key) != null ? key : fallback;

    // What should be playing now
    private (string music, float vol, bool loop) PickMusic()
    {
        GameManager gm = GameManager.Instance;
        if (TitleScreen.Playing) return ("bgm_title", 1f, true);
        if (OpeningCutscene.Playing) return ("bgm_opening", 0.9f, true);
        if (gm != null && gm.RunOutcome == 1) return ("bgm_gameover", 0.9f, false);
        if (gm != null && gm.RunOutcome == 2) return ("bgm_victory", 1f, false);
        // Asgard's two boss rooms have scores of their own, used nowhere else, heard from the moment you walk in (the
        // boss's words before the fight included) until the room is won
        if (gm != null && !gm.InHub)
        {
            string boss = BossRoomMusic(gm);
            if (boss != null) return (boss, 1f, true);
        }
        if (VisualNovel.Playing) return ("bgm_story", 0.75f, true);
        if (gm == null) return ("", 0f, true);
        if (gm.InHub) return ("bgm_village", 0.8f, true);
        Room room = gm.CurrentRoom;
        if (room != null && (room.IsFighting || BossCutscene.Playing))
        {
            // each of Asgard's bosses has its own score, one movement per phase (the general boss tracks if one is missing)
            if (room.Boss is HeimdallBoss heimdall)
                return (Pick("bgm_heim" + Mathf.Clamp(heimdall.Phase, 1, 3), "bgm_final"), 1f, true);
            if (room.Boss is TyrBoss tyr)
                return (Pick("bgm_tyr" + Mathf.Clamp(tyr.Phase, 1, 2), "bgm_boss"), 1f, true);
            if (room.RoomKind == Room.Kind.Boss)
                return ("bgm_final", 1f, true);
            if (room.RoomKind == Room.Kind.MidBoss || room.IsMidBossFight)
                return ("bgm_boss", 1f, true);
            battleUntil = Time.unscaledTime + 5f;
            return ("bgm_battle", 0.9f, true);
        }
        if (Time.unscaledTime < battleUntil && musicKey == "bgm_battle") return ("bgm_battle", 0.75f, true);
        if (room != null && (room.RoomKind == Room.Kind.Shop || room.RoomKind == Room.Kind.Heal || room.RoomKind == Room.Kind.Forge
            || room.RoomKind == Room.Kind.Supports || room.RoomKind == Room.Kind.Start || room.RoomKind == Room.Kind.Vigor
            || room.RoomKind == Room.Kind.Fate || room.RoomKind == Room.Kind.Essence || room.RoomKind == Room.Kind.Ascend
            || room.RoomKind == Room.Kind.Vault))
            return ("bgm_calm", 0.8f, true);
        return ("bgm_asgard", 0.85f, true);
    }

    // VARCO: bgm_tyrhall1-2 for Týr's battleground, bgm_heimhall1-3 for Heimdall's arena (by phase)
    private string BossRoomMusic(GameManager gm)
    {
        Room room = gm.CurrentRoom;
        if (room == null || room.Cleared)
            return null;
        if (room.RoomKind == Room.Kind.Boss && Clip("Music", "bgm_heimhall1") != null)
        {
            int p = room.Boss is HeimdallBoss h ? Mathf.Clamp(h.Phase, 1, 3) : 1;
            return Pick("bgm_heimhall" + p, "bgm_heimhall1");
        }
        bool tyrRoom = room.IsMidBossFight && (room.Boss is TyrBoss
            || (room.Boss == null && gm.CurrentRealm != null && gm.CurrentRealm.midBoss != null && gm.CurrentRealm.midBoss.prefab is TyrBoss));
        if (tyrRoom && Clip("Music", "bgm_tyrhall1") != null)
        {
            int p = room.Boss is TyrBoss t ? Mathf.Clamp(t.Phase, 1, 2) : 1;
            return Pick("bgm_tyrhall" + p, "bgm_tyrhall1");
        }
        return null;
    }

    private (string key, float vol) PickAmbience()
    {
        if (!string.IsNullOrEmpty(AmbienceOverride)) return (AmbienceOverride, 0.7f);
        GameManager gm = GameManager.Instance;
        if (TitleScreen.Playing) return ("wind_loop", 0.35f);
        if (OpeningCutscene.Playing) return ("", 0f);
        if (gm == null || gm.RunOutcome != 0) return ("", 0f);
        if (gm.InHub) return ("village_amb", 0.5f);
        return ("asgard_amb", 0.35f);
    }

    private float musicVol = 1f, ambVol = 1f;

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        duck = Mathf.MoveTowards(duck, Narration.Speaking ? 0.4f : 1f, dt * (Narration.Speaking ? 3f : 0.8f));

        var (mKey, mVol, mLoop) = PickMusic();
        if (mKey != musicKey)
        {
            musicKey = mKey;
            musicIdx = 1 - musicIdx;
            AudioClip c = string.IsNullOrEmpty(mKey) ? null : Clip("Music", mKey);
            // Most tracks have a second take (<key>_b); alternate between them so a long run doesn't wear one out
            if (c != null && Random.value < 0.5f)
            {
                AudioClip alt = Clip("Music", mKey + "_b");
                if (alt != null) c = alt;
            }
            AudioSource s = music[musicIdx];
            s.Stop();
            s.clip = c;
            s.loop = mLoop;
            s.volume = 0f;
            if (c != null) s.Play();
        }
        musicVol = mVol;
        float fade = musicKey == "bgm_gameover" || musicKey == "bgm_victory" ? 0.6f : 1.6f;
        for (int i = 0; i < 2; i++)
        {
            float target = i == musicIdx && music[i].clip != null ? musicVol * GameAudio.MusicVolume * duck : 0f;
            music[i].volume = Mathf.MoveTowards(music[i].volume, target, dt / fade);
            if (i != musicIdx && music[i].volume <= 0f && music[i].isPlaying) music[i].Stop();
        }

        var (aKey, aVol) = PickAmbience();
        if (aKey != ambKey)
        {
            ambKey = aKey;
            ambIdx = 1 - ambIdx;
            AudioClip c = string.IsNullOrEmpty(aKey) ? null : Clip("Audio", aKey);
            AudioSource s = amb[ambIdx];
            s.Stop();
            s.clip = c;
            s.volume = 0f;
            if (c != null) s.Play();
        }
        ambVol = aVol;
        for (int i = 0; i < 2; i++)
        {
            float target = i == ambIdx && amb[i].clip != null ? ambVol * GameAudio.Volume : 0f;
            amb[i].volume = Mathf.MoveTowards(amb[i].volume, target, dt / 2f);
            if (i != ambIdx && amb[i].volume <= 0f && amb[i].isPlaying) amb[i].Stop();
        }

        // Now and then a far-off sound over the bed, from one side or the other: thunder and eagles over the
        // halls of Asgard, birds, sheep and the smith's hammer about the village
        if (Time.unscaledTime > nextSpot)
        {
            nextSpot = Time.unscaledTime + Random.Range(9f, 22f);
            string[] pool = ambKey == "village_amb" ? VillageSpots : ambKey == "asgard_amb" ? HallSpots : null;
            if (pool != null && musicKey != "bgm_battle" && !Narration.Speaking)
            {
                AudioSource s = GameAudio.Play(pool[Random.Range(0, pool.Length)], null, Random.Range(0.25f, 0.45f), Random.Range(0.94f, 1.04f));
                if (s != null) s.panStereo = Random.Range(-0.8f, 0.8f);
            }
        }
    }

    private float nextSpot = 12f;
    private static readonly string[] HallSpots = { "amb_thunder", "amb_eagle", "amb_gust", "amb_bell_far", "amb_choir", "amb_crows" };
    private static readonly string[] VillageSpots = { "amb_birds", "amb_sheep", "amb_hammer_far", "amb_chatter", "amb_crows", "amb_birds" };
}

// ───────────── narration: every story line read aloud ─────────────
// Lines are baked ahead of time to Resources/Voice/v_<hash>.wav (hash: FNV-1a of the line's text); a line without a
// recording is simply silent. Who speaks only matters for the baked voice, so the same line always plays the same.
public static class Narration
{
    private static AudioSource source;
    private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

    public static bool Speaking => source != null && source.isPlaying;

    public static string Hash(string text)
    {
        uint h = 2166136261u;
        foreach (char c in text.Trim())
        {
            h ^= c;
            h *= 16777619u;
        }
        return h.ToString("x8");
    }

    public static AudioClip ClipFor(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string key = Hash(text);
        if (!cache.TryGetValue(key, out AudioClip c))
        {
            c = Resources.Load<AudioClip>("Voice/v_" + key);
            cache[key] = c;
        }
        return c;
    }

    public static bool Has(string text) => ClipFor(text) != null;

    // Reads a line (stopping whatever was being read); returns how long it lasts (0 if there is no recording)
    public static float Say(string text)
    {
        AudioClip c = ClipFor(text);
        if (c == null) return 0f;
        if (source == null)
        {
            source = GameAudio.Host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.priority = 0;
        }
        source.Stop();
        source.clip = c;
        source.volume = GameAudio.VoiceVolume;
        source.Play();
        return c.length;
    }

    public static void Stop()
    {
        if (source != null) source.Stop();
    }
}

// ───────────── the voices of monsters: each kind sounds like what it is ─────────────
// 인간형 (sword, spear and bow folk) grunt and cry out; 짐승 squeal and snort; 새 shriek; 거인 rumble; 기계 (the golden
// constructs: rotors, bells, blades, chains) whir, clank and break down — and hum while they run; 룬 (wisps, watchers,
// splitting light) chime, crack and scatter into sparkles — and hum too.
public class EnemyVoice : MonoBehaviour
{
    public enum Family { None, HumanMale, HumanFemale, Beast, Bird, Giant, Machine, Rune }

    public Family family;
    public float pitch = 1f;
    public string attackExtra;        // an extra sound with each attack (the bell's toll, chains, the beam) — family fallback only
    public string own;                // this monster's own sound set (<own>_atk/_skill/_hurt/_die); the family's when missing
    public string material = "flesh"; // what a blow on it sounds like: flesh, metal, rune, stone
    private string idleKey;
    private float idleVol = 0.3f;
    private float nextHurt, nextAttack, nextIdle, nextSkill;
    private AudioSource hum;
    private Enemy enemy;

    public static void Attach(Enemy e)
    {
        if (e == null || e.GetComponent<EnemyVoice>() != null) return;
        var v = e.gameObject.AddComponent<EnemyVoice>();
        v.Configure(e);
    }

    public static EnemyVoice Of(Component c) => c != null ? c.GetComponentInParent<EnemyVoice>() : null;

    private void Configure(Enemy e)
    {
        enemy = e;
        switch (e.GetType().Name)
        {
            case "Einherjar": family = Family.HumanMale; own = "einh"; material = "metal"; break;
            case "RuneArcher": family = Family.HumanMale; pitch = 1.08f; own = "archer"; break;
            case "SeidrMage": family = Family.HumanMale; pitch = 0.94f; own = "seidr"; break;
            case "WallGuardian": family = Family.HumanMale; pitch = 0.82f; attackExtra = "block"; own = "guard"; material = "metal"; break;
            case "TyrBoss": family = Family.HumanMale; pitch = 0.78f; own = "tyr"; material = "metal"; break;
            case "HeimdallBoss": family = Family.HumanMale; pitch = 0.72f; own = "heim"; material = "metal"; break;
            case "Valkyrie": family = Family.HumanFemale; own = "valk"; material = "metal"; break;
            case "OracleProphet": family = Family.HumanFemale; pitch = 1.1f; own = "oracle"; material = "metal"; break;
            case "Gullinbursti": family = Family.Beast; own = "boar"; idleKey = "boar_hurt"; idleVol = 0.25f; break;
            case "Raven": family = Family.Bird; own = "raven"; idleKey = "raven_atk"; idleVol = 0.3f; break;
            case "StarGiant": family = Family.Giant; own = "giant"; material = "stone"; idleKey = "giant_hurt"; idleVol = 0.3f; break;
            case "SolarisRotor": family = Family.Machine; pitch = 1.05f; own = "rotor"; material = "metal"; break;
            case "HeavenBell": family = Family.Machine; pitch = 0.9f; attackExtra = "bell"; own = "bell"; material = "metal"; break;
            case "Gladion": family = Family.Machine; pitch = 1.15f; attackExtra = "whoosh_big"; own = "glad"; material = "metal"; break;
            case "ChainBinder": family = Family.Machine; pitch = 0.95f; attackExtra = "chains"; own = "chain"; material = "metal"; break;
            case "RuneWisp": family = Family.Rune; pitch = 1.1f; own = "wisp"; material = "rune"; break;
            case "SkiaWatcher": family = Family.Rune; pitch = 0.92f; attackExtra = "laser"; own = "skia"; material = "rune"; break;
            case "LumenSplitter": family = Family.Rune; pitch = 1.2f; own = "lumen"; material = "rune"; break;
            case "TrainingDummy": family = Family.None; material = "dummy"; break;
            default: family = e is HumanoidEnemy ? Family.HumanMale : Family.Rune; material = e is HumanoidEnemy ? "flesh" : "rune"; break;
        }
        if (family == Family.Beast || family == Family.Bird || family == Family.Giant)
            idleKey ??= Prefix + "_atk";
    }

    // a splitting light's little copies squeak higher (it scales them down after spawning, so this is asked each time)
    private float Small => family == Family.Rune && transform.localScale.x < 0.8f ? 1.25f : 1f;

    private string Prefix => family switch
    {
        Family.HumanMale => "hm", Family.HumanFemale => "hf", Family.Beast => "bst", Family.Bird => "bird",
        Family.Giant => "gnt", Family.Machine => "mch", Family.Rune => "rune", _ => null,
    };

    // Heimdall fights his first phase from the back of his lion: the lion is the one that roars and bleeds
    private string Own => own == "heim" && enemy is HeimdallBoss hb && hb.Phase == 1 ? "lion" : own;

    public bool HasOwn(string what) => !string.IsNullOrEmpty(Own) && GameAudio.Clips(Own + "_" + what).Length > 0;

    // Plays this monster's own sound for the move, else its family's; returns whether its own was used
    private bool Say(string what, float loud)
    {
        if (HasOwn(what))
        {
            GameAudio.Play(Own + "_" + what, transform.position, loud, Small);
            return true;
        }
        if (Prefix == null) return false;
        GameAudio.Play(Prefix + "_" + (what == "skill" ? "atk" : what), transform.position, loud, pitch * Small);
        return false;
    }

    private void Start()
    {
        nextIdle = Time.time + Random.Range(3f, 8f);
        if (family == Family.Machine || family == Family.Rune)
            hum = GameAudio.Loop(gameObject, family == Family.Machine ? "mch_loop" : "rune_loop", enemy != null && enemy.IsBoss ? 0.5f : 0.3f, 11f, pitch);
        if (enemy != null && !enemy.IsBoss)
            GameAudio.Play("spawn", transform.position, 0.6f);
        // a boss's new phase: a surge of power
        if (enemy is TyrBoss tyr) tyr.PhaseChanged += (a, b) => GameAudio.Play("boss_phase");
        else if (enemy is HeimdallBoss heim) heim.PhaseChanged += (a, b) => GameAudio.Play("boss_phase");
    }

    private void Update()
    {
        if (enemy == null || enemy.IsDead)
        {
            if (hum != null) hum.Stop();
            return;
        }
        // beasts snort, ravens caw, giants rumble now and then while about
        if (Time.time > nextIdle)
        {
            nextIdle = Time.time + Random.Range(6f, 12f);
            if (idleKey != null)
                GameAudio.Play(idleKey, transform.position, idleVol, Random.Range(0.92f, 1.04f));
        }
    }

    public void Attack(float loud = 1f)
    {
        if (family == Family.None || Time.time < nextAttack) return;
        nextAttack = Time.time + 0.6f;
        bool mine = Say("atk", loud);
        if (!mine && !string.IsNullOrEmpty(attackExtra)) GameAudio.Play(attackExtra, transform.position, 0.6f * loud);
    }

    // Its big move (a roar, a chant, a charge-up): its own skill sound, or a loud attack when it has none
    public void Skill(float loud = 1f)
    {
        if (family == Family.None || Time.time < nextSkill) return;
        nextSkill = Time.time + 1.2f;
        nextAttack = Time.time + 0.6f;
        bool mine = Say("skill", loud);
        if (!mine && !string.IsNullOrEmpty(attackExtra)) GameAudio.Play(attackExtra, transform.position, 0.7f * loud);
    }

    public void Hurt()
    {
        if (family == Family.None || Time.time < nextHurt) return;
        nextHurt = Time.time + Random.Range(0.35f, 0.7f);
        bool voiced = family != Family.Machine && family != Family.Rune;
        if (voiced && Random.value < 0.35f) return;
        Say("hurt", 1f);
    }

    public void Die()
    {
        if (hum != null) hum.Stop();
        if (family == Family.None) return;
        Say("die", enemy != null && enemy.IsBoss ? 1f : 0.85f);
    }
}

// ───────────── the player: swings, shots, dodges, footsteps, voice, a heartbeat when low ─────────────
// Listens to the animator, so every fighter's moves sound however their own scripts start them.
public class PlayerMoveAudio : MonoBehaviour
{
    private static readonly HashSet<int> Swings = Hashes("Attack", "Combo", "Combo1", "Combo2", "Combo3", "Combo4", "Flurry",
        "ExecuteRelease", "Lunge", "Swing", "SkillSpin", "SkillPierce", "Throw", "SkillThrow", "Cleave", "Thrust", "Slash", "Strike");
    private static readonly HashSet<int> Heavy = Hashes("Whirlwind", "LeapSlam", "SkillSlam", "Combo4", "ExecuteRelease", "Charge");
    private static readonly HashSet<int> Shots = Hashes("Shoot", "ShootUp", "RollShoot", "Shot", "Fire");
    private static readonly HashSet<int> Dodges = Hashes("Dash", "Dodge", "Roll");

    private Animator animator;
    private PlayerMovement movement;
    private PlayerStats stats;
    private int lastState;
    private float lastTime, stepAt, beatAt;
    private bool wasDashing;
    private Vector3 lastPos;

    // The player's weapon, as its sound files name it (w_<key>_swing / _heavy / _hit), or null
    public static string WeaponKey
    {
        get
        {
            CharacterProfile c = Switcher != null ? Switcher.Current : null;
            if (c == null) return null;
            switch (c.Weapon)
            {
                case CharacterProfile.WeaponKind.Greatsword: return "gsword";
                case CharacterProfile.WeaponKind.DualBlades: return "blades";
                case CharacterProfile.WeaponKind.Spear: return "spear";
                case CharacterProfile.WeaponKind.Greataxe: return "gaxe";
                case CharacterProfile.WeaponKind.TwinAxes: return "taxe";
                case CharacterProfile.WeaponKind.Hammer: return "hammer";
                case CharacterProfile.WeaponKind.Gauntlets: return "gaunt";
                case CharacterProfile.WeaponKind.Bow: return "bow";
            }
            return null;
        }
    }

    private static CharacterSwitcher switcher;
    private static CharacterSwitcher Switcher
    {
        get
        {
            if (switcher == null && GameManager.Instance != null)
                switcher = Object.FindFirstObjectByType<CharacterSwitcher>();
            return switcher;
        }
    }

    // The weapon's own sound for a move, else the plain one
    private static string WeaponSound(string move, string plain)
    {
        string w = WeaponKey;
        string key = w != null ? "w_" + w + "_" + move : null;
        return key != null && GameAudio.Clips(key).Length > 0 ? key : plain;
    }

    private string lastWeapon;
    private PlayerMiner miner;
    private bool wasCharging;

    private static HashSet<int> Hashes(params string[] names)
    {
        var set = new HashSet<int>();
        foreach (string n in names) set.Add(Animator.StringToHash(n));
        return set;
    }

    public static void Attach(GameObject player)
    {
        if (player != null && player.GetComponent<PlayerMoveAudio>() == null)
            player.AddComponent<PlayerMoveAudio>();
    }

    private void Start()
    {
        stats = GetComponent<PlayerStats>();
        movement = GetComponent<PlayerMovement>();
        lastPos = transform.position;
        miner = GetComponent<PlayerMiner>();
        if (miner != null)
            miner.ChargeStageReached += stage => GameAudio.Play("charge_full", null, stage >= 2 ? 1f : 0.7f, stage >= 2 ? 1f : 0.85f);
        if (stats != null)
        {
            stats.Damaged += () => GameAudio.Play("pv_hurt");
            stats.Died += () => { GameAudio.Play("pv_die"); GameAudio.Play("player_death"); };
        }
    }

    private static bool Quiet => TitleScreen.Playing || OpeningCutscene.Playing || BossCutscene.Playing || VisualNovel.Playing || Time.timeScale < 0.01f;

    private void LateUpdate()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            animator = GetComponentInChildren<Animator>();
        if (Quiet) { lastPos = transform.position; return; }

        // the move the animator just started
        if (animator != null)
        {
            AnimatorStateInfo now = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            int h = now.shortNameHash;
            float t = now.normalizedTime;
            bool started = h != lastState || (t + 0.25f < lastTime && t < 0.3f);
            lastState = h;
            lastTime = t;
            // (the greatsword and the twin blades sound their own blows from their scripts, each move its own)
            string wk = WeaponKey;
            if (started && (wk == "gsword" || wk == "blades"))
            {
                if (Dodges.Contains(h) && wk == "gsword") GameAudio.Play("dash");
            }
            else if (started)
            {
                if (Heavy.Contains(h)) { GameAudio.Play(WeaponSound("heavy", "swing2"), null, 1f, 0.95f); GameAudio.Play("pv_atk", null, 1f); }
                else if (Swings.Contains(h)) { GameAudio.Play(WeaponSound("swing", "swing")); if (Random.value < 0.22f) GameAudio.Play("pv_atk"); }
                else if (Shots.Contains(h)) GameAudio.Play(WeaponSound("swing", "shoot"), null, 1.1f);
                else if (Dodges.Contains(h)) GameAudio.Play("dash");
            }
        }

        // taking up another weapon: the blade rings out, the heavy one is hefted, the bow creaks
        string weaponNow = WeaponKey;
        if (weaponNow != lastWeapon)
        {
            if (lastWeapon != null && weaponNow != null)
                GameAudio.Play(weaponNow == "bow" ? "equip_bow" : weaponNow == "gaxe" || weaponNow == "hammer" || weaponNow == "taxe" || weaponNow == "gaunt" ? "equip_heavy" : "equip_blade");
            lastWeapon = weaponNow;
        }

        // a charged blow starting to gather
        bool charging = miner != null && miner.IsCharging;
        if (charging && !wasCharging) GameAudio.Play("charge_hold");
        wasCharging = charging;

        // the dash (Space)
        bool dashing = movement != null && movement.IsDashing;
        if (dashing && !wasDashing) { GameAudio.Play("dash"); if (Random.value < 0.5f) GameAudio.Play("pv_dash"); }
        wasDashing = dashing;

        // footsteps: grass in the village, stone in the halls
        Vector3 d = transform.position - lastPos;
        d.y = 0f;
        float speed = Time.deltaTime > 0f ? d.magnitude / Time.deltaTime : 0f;
        lastPos = transform.position;
        if (speed > 1.5f && !dashing && speed < 20f)
        {
            if (Time.time > stepAt)
            {
                stepAt = Time.time + Mathf.Clamp(1.9f / speed, 0.22f, 0.5f);
                bool grass = GameManager.Instance != null && GameManager.Instance.InHub;
                GameAudio.Play(grass ? "step_grass" : "step_stone");
            }
        }
        else stepAt = Mathf.Min(stepAt, Time.time + 0.08f);

        // a heartbeat below a quarter of health
        if (stats != null && !stats.IsDead && stats.Health < stats.MaxHealth * 0.25f && Time.time > beatAt)
        {
            beatAt = Time.time + 0.85f;
            GameAudio.Play("heartbeat");
        }
    }
}

// ───────────── the interface: hover ticks and clicks on every button ─────────────
public static class UiSound
{
    private static int hoverId, lastFrame;

    // GUI.Button, with a tick when the mouse comes onto it and a chime when it is pressed
    public static bool Button(Rect r, string text) => Hover(r, GUI.Button(r, text));
    public static bool Button(Rect r, string text, GUIStyle style) => Hover(r, GUI.Button(r, text, style));
    public static bool Button(Rect r, GUIContent content) => Hover(r, GUI.Button(r, content));
    public static bool Button(Rect r, GUIContent content, GUIStyle style) => Hover(r, GUI.Button(r, content, style));
    public static bool Button(Rect r, Texture image) => Hover(r, GUI.Button(r, image));
    public static bool Button(Rect r, Texture image, GUIStyle style) => Hover(r, GUI.Button(r, image, style));

    private static bool Hover(Rect r, bool pressed)
    {
        if (pressed)
            GameAudio.Play("ui_select");
        else if (Event.current != null && Event.current.type == EventType.Repaint && r.Contains(Event.current.mousePosition))
            Hovered((int)(r.x * 7 + r.y * 131 + r.width * 17));
        return pressed;
    }

    // Something under the mouse (a button, a map node, a card): a tick when it changes
    public static void Hovered(int id)
    {
        if (id == hoverId) return;
        hoverId = id;
        GameAudio.Play("ui_move");
    }

    public static void Clear() => hoverId = 0;
}

// Finds the player (who changes when a fighter is picked) and gives them their sounds; tells HitFeel of crits
public class AudioWatcher : MonoBehaviour
{
    private float next;
    private PlayerMiner hooked;

    private void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 1f;
        var p = FindAnyObjectByType<PlayerStats>();
        if (p != null) PlayerMoveAudio.Attach(p.gameObject);
        var miner = p != null ? p.GetComponent<PlayerMiner>() : null;
        if (miner != null && miner != hooked)
        {
            if (hooked != null) hooked.HitEnemy -= OnHitEnemy;
            hooked = miner;
            miner.HitEnemy += OnHitEnemy;
        }
    }

    private static void OnHitEnemy(Enemy enemy, int damage, bool critical)
    {
        if (critical) HitFeel.MarkCrit(enemy);
    }
}
