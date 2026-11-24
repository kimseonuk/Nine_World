using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ── 훈련장: a room to try every skill on training dummies ──
// Reached from the F1 test panel between fights. The arena sits far off the map; the dummies never die (their health
// fills up again) and never strike back. Every hit shows its number, and a board shows the total, DPS over the last
// five seconds, the biggest hit and which skill the damage came from. [T] opens the setup: put any of the 32 skills
// (or the weapon in hand's own skill orb) on Q / E / R, set its level and its boss special, turn cooldowns off,
// and lay out the dummies (a row, a crowd, a moving one, a boss-type one that is never knocked down).
public class TrainingRoom : MonoBehaviour
{
    public static TrainingRoom Instance { get; private set; }
    public static bool Active => Instance != null && Instance.inside;
    public static bool PanelOpen => Instance != null && Instance.panelOpen;

    private static readonly Vector3 Origin = new Vector3(-1000f, 0f, -1000f);
    private const float HalfX = 13f, HalfZ = 15f;   // (the back half holds the class gallery)

    private Transform arena;
    private bool inside, panelOpen;
    private Vector3 returnPos;
    private Quaternion returnRot;
    private readonly List<TrainingDummy> dummies = new List<TrainingDummy>();
    private int dummyHealth = 5000;
    private int layout;
    private float savedTimeScale = 1f;

    // ── what the dummies took ──
    private struct HitRecord { public float time; public int amount; public string source; }
    private static readonly List<HitRecord> hits = new List<HitRecord>();
    private static readonly Dictionary<string, (int total, int count, int max)> bySource = new Dictionary<string, (int, int, int)>();
    private static int totalDamage, biggest, hitCount;
    private static float firstHitTime = -1f;
    private struct Floater { public Vector3 at; public int amount; public float time; public Color color; }
    private static readonly List<Floater> floaters = new List<Floater>();

    private static GameManager Gm => GameManager.Instance;
    private static PlayerStats player;
    private static PlayerStats Player => player != null ? player : player = FindAnyObjectByType<PlayerStats>();

    // ═══════════ In and out ═══════════
    public static void Enter()
    {
        if (Instance == null)
        {
            var go = new GameObject("TrainingRoom");
            Instance = go.AddComponent<TrainingRoom>();
        }
        Instance.DoEnter();
    }

    public static void Exit()
    {
        if (Instance != null) Instance.DoExit(true);
    }

    private void DoEnter()
    {
        if (inside || Gm == null || Player == null) return;
        DebugCheats.UsedThisRun = true;
        returnPos = Player.transform.position;
        returnRot = Player.transform.rotation;
        if (arena == null) BuildArena();
        arena.gameObject.SetActive(true);
        inside = true;
        Gm.TeleportPlayer(Origin + new Vector3(0f, 1.1f, -2f), Quaternion.identity, true);
        Lay(layout);
        ResetStats();
        if (!galleryBuilt) StartCoroutine(GatherClassSkills());
        Toast("훈련장 — 양옆 진열장에서 [1]~[4] 보조스킬 장착 · [T] 설정 · [F] 인벤토리", new Color(0.7f, 0.9f, 1f));
    }

    private void DoExit(bool teleportBack)
    {
        if (!inside) return;
        ClosePanel();
        inside = false;
        ClearDummies();
        if (arena != null) arena.gameObject.SetActive(false);
        // (came from the village's portal: back to the village rather than the run's first room)
        if (teleportBack && Gm != null && Gm.TrainingFromVillage) Gm.LeaveTrainingToVillage();
        else if (teleportBack && Gm != null) Gm.TeleportPlayer(returnPos, returnRot, false);
    }

    private static void Toast(string msg, Color c)
    {
        if (Gm == null) return;
        typeof(GameManager).GetMethod("Toast", (System.Reflection.BindingFlags)36)?.Invoke(Gm, new object[] { msg, c });
    }

    // ═══════════ The arena ═══════════
    private void BuildArena()
    {
        arena = new GameObject("TrainingArena").transform;
        arena.position = Origin;
        Material marble = Mats.Get("train_marble", new Color(0.88f, 0.86f, 0.82f), 0.55f);
        Material dark = Mats.Get("train_dark", new Color(0.16f, 0.15f, 0.17f), 0.2f);
        Material gold = Mats.Get("train_gold", new Color(0.95f, 0.75f, 0.35f), 0.7f, new Color(0.35f, 0.24f, 0.08f));
        Material wood = Mats.Get("train_wood", new Color(0.45f, 0.3f, 0.18f), 0.25f);

        // floor, a darker apron round it, gold inlay
        Mats.Shape(PrimitiveType.Cube, arena, new Vector3(0f, -0.2f, 0f), new Vector3(HalfX * 2f + 2f, 0.4f, HalfZ * 2f + 2f), marble, true, "Floor");
        Mats.Shape(PrimitiveType.Cube, arena, new Vector3(0f, -0.45f, 0f), new Vector3(120f, 0.4f, 120f), dark, false, "Apron");
        for (int s = -1; s <= 1; s += 2)
        {
            Mats.Shape(PrimitiveType.Cube, arena, new Vector3(0f, 0.01f, s * (HalfZ + 0.6f)), new Vector3(HalfX * 2f + 1.6f, 0.02f, 0.25f), gold, false, "Inlay");
            Mats.Shape(PrimitiveType.Cube, arena, new Vector3(s * (HalfX + 0.6f), 0.01f, 0f), new Vector3(0.25f, 0.02f, HalfZ * 2f + 1.6f), gold, false, "Inlay");
        }
        // walls that hold the player in (only the far ones show)
        Mats.Shape(PrimitiveType.Cube, arena, new Vector3(0f, 1.5f, HalfZ + 1.4f), new Vector3(HalfX * 2f + 4f, 3f, 0.8f), marble, true, "WallN");
        Mats.Shape(PrimitiveType.Cube, arena, new Vector3(-HalfX - 1.4f, 1.5f, 0f), new Vector3(0.8f, 3f, HalfZ * 2f + 4f), marble, true, "WallW");
        Mats.Shape(PrimitiveType.Cube, arena, new Vector3(HalfX + 1.4f, 1.5f, 0f), new Vector3(0.8f, 3f, HalfZ * 2f + 4f), marble, true, "WallE");
        GameObject south = Mats.Shape(PrimitiveType.Cube, arena, new Vector3(0f, 1.5f, -HalfZ - 1.4f), new Vector3(HalfX * 2f + 4f, 3f, 0.8f), marble, true, "WallS");
        south.GetComponent<Renderer>().enabled = false;
        // pillars, braziers and rune circles
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                Vector3 p = new Vector3(sx * (HalfX + 0.4f), 0f, sz * (HalfZ + 0.4f));
                Mats.Shape(PrimitiveType.Cylinder, arena, p + Vector3.up * 2f, new Vector3(1.1f, 2f, 1.1f), marble, false, "Pillar");
                Mats.Shape(PrimitiveType.Cylinder, arena, p + Vector3.up * 4.05f, new Vector3(1.4f, 0.1f, 1.4f), gold, false, "PillarCap");
                Vfx.Glow(arena, p + Vector3.up * 4.4f, 3.5f, new Color(1f, 0.6f, 0.25f) * 0.9f, "glow", 0f);
            }
        Ambient.RuneHalo(arena, new Vector3(0f, 0.02f, 3f), 6.5f, new Color(0.6f, 0.8f, 1f) * 0.6f);
        // a weapon rack and a sign by the entrance
        Mats.Shape(PrimitiveType.Cube, arena, new Vector3(-HalfX + 1.2f, 0.9f, -HalfZ + 1f), new Vector3(2.4f, 0.15f, 0.3f), wood, false, "Rack");
        Mats.Shape(PrimitiveType.Cube, arena, new Vector3(-HalfX + 1.2f, 0.45f, -HalfZ + 1f), new Vector3(0.15f, 0.9f, 0.15f), wood, false, "RackLeg");

        // light
        var lgo = new GameObject("TrainingLight");
        lgo.transform.SetParent(arena, false);
        lgo.transform.localPosition = new Vector3(0f, 9f, 0f);
        var light = lgo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 30f;
        light.intensity = 2.2f;
        light.color = new Color(1f, 0.93f, 0.82f);

        BuildShowcase(marble, gold);
    }

    // ── 진열장: every skill on its own pedestal, by element (west wall: 화염 빙결 번개 바람, east: 대지 신성 암흑 피),
    // and the weapon in hand's own skill at the back. Walk up and press 1 / 2 / 3 for Q / E / R, 4 for the bag. ──
    private void BuildShowcase(Material marble, Material gold)
    {
        float[] rows = { 6.5f, 3.5f, 0.5f, -2.5f };
        foreach (SkillDef d in SkillCatalog.All)
        {
            int e = (int)d.element;
            int side = e < 4 ? -1 : 1;
            int row = e % 4;
            int col = SkillCatalog.All.FindAll(s => s.element == d.element).IndexOf(d);   // 0..3, outward from the wall
            float x = side * (HalfX - 0.9f - col * 1.45f);
            SkillShowcase.Create(arena, new Vector3(x, 0f, rows[row]), d, marble, gold);
        }
        showcaseMarble = marble;
        showcaseGold = gold;
        // a strip of each element's colour under its row
        for (int e = 0; e < 8; e++)
        {
            int side = e < 4 ? -1 : 1;
            float cx = side * (HalfX - 0.9f - 1.5f * 1.45f);
            Material strip = Mats.Get("train_el" + e, SkillCatalog.ElementColor((SkillElement)e) * 0.7f, 0.4f, SkillCatalog.ElementColor((SkillElement)e) * 0.35f);
            Mats.Shape(PrimitiveType.Cube, arena, new Vector3(cx, 0.015f, rows[e % 4]), new Vector3(5.6f, 0.02f, 1.2f), strip, false, "ShowcaseStrip");
        }
    }

    // ── 직업 진열장: every class's own Q / E / R / right click, one group per class at the back of the room ──
    private Material showcaseMarble, showcaseGold;
    private bool galleryBuilt, gathering;
    private readonly List<(Vector3 at, string title)> galleryLabels = new List<(Vector3, string)>();
    private static CharacterSwitcher switcher;
    private static CharacterSwitcher Switcher => switcher != null ? switcher : switcher = FindAnyObjectByType<CharacterSwitcher>();

    // A class's skills are known once its own skill bar has been drawn: take each class in hand for a moment (the
    // first visit only), then give back the one the player had
    private System.Collections.IEnumerator GatherClassSkills()
    {
        var sw = Switcher;
        if (sw == null || gathering) yield break;
        gathering = true;
        var select = typeof(CharacterSwitcher).GetMethod("Select", (System.Reflection.BindingFlags)36);
        var chars = sw.Characters;
        CharacterProfile had = sw.Current;
        int hadIndex = -1;
        for (int i = 0; i < chars.Count; i++) if (chars[i] == had) hadIndex = i;
        bool switched = false;
        for (int i = 0; i < chars.Count && select != null; i++)
        {
            bool known = true;
            foreach (char k in SkillSystem.WeaponKeys)
                if (k != 'M' && SkillSystem.WeaponSkill(chars[i].name + "/" + k) == null) known = false;
            if (known) continue;
            select.Invoke(sw, new object[] { i, false });
            switched = true;
            for (int f = 0; f < 12; f++) yield return null;   // (long enough for the old class's bar to clear)
        }
        if (switched && hadIndex >= 0) select.Invoke(sw, new object[] { hadIndex, false });
        yield return null;
        gathering = false;
        BuildClassGallery();
    }

    private void BuildClassGallery()
    {
        if (galleryBuilt || arena == null || Switcher == null) return;
        galleryBuilt = true;
        var chars = Switcher.Characters;
        float[] rowZ = { HalfZ - 5.5f, HalfZ - 2.3f };
        const int perRow = 4;
        for (int i = 0; i < chars.Count; i++)
        {
            int row = i / perRow, inRow = i % perRow;
            int n = Mathf.Min(perRow, chars.Count - row * perRow);
            float cx = (inRow - (n - 1) / 2f) * 6.3f;
            float z = rowZ[Mathf.Min(row, rowZ.Length - 1)];
            int placed = 0;
            for (int k = 0; k < 4; k++)
            {
                string key = chars[i].name + "/" + SkillSystem.WeaponKeys[k];
                if (SkillSystem.WeaponSkill(key) == null) continue;
                SkillShowcase.Create(arena, new Vector3(cx + (k - 1.5f) * 1.35f, 0f, z), null, showcaseMarble, showcaseGold, key);
                placed++;
            }
            if (placed == 0) continue;
            Material strip = Mats.Get("train_class", new Color(0.55f, 0.55f, 0.62f), 0.4f, new Color(0.15f, 0.15f, 0.2f));
            Mats.Shape(PrimitiveType.Cube, arena, new Vector3(cx, 0.015f, z), new Vector3(5.6f, 0.02f, 1.2f), strip, false, "ClassStrip");
            galleryLabels.Add((Origin + new Vector3(cx, 0.1f, z - 0.95f), $"{chars[i].DisplayName} · {chars[i].WeaponName}"));
        }
    }

    // The pedestal within reach. A support: 1 / 2 / 3 / 4 socket it on Q / E / R / right click, 5 puts it in the bag.
    // A class skill: 1 takes up that class.
    private readonly int[] replaceNext = new int[SkillSystem.Mains];
    private void ShowcaseKeys(Keyboard kb)
    {
        SkillShowcase near = SkillShowcase.Nearest(Player.transform.position, 1.8f);
        if (near == null) return;
        int key = kb.digit1Key.wasPressedThisFrame ? 0 : kb.digit2Key.wasPressedThisFrame ? 1 : kb.digit3Key.wasPressedThisFrame ? 2
            : kb.digit4Key.wasPressedThisFrame ? 3 : kb.digit5Key.wasPressedThisFrame ? 4 : -1;
        if (key < 0) return;
        if (near.Def == null)
        {
            if (key != 0 || Switcher == null) return;
            var w = SkillSystem.WeaponSkill(near.WeaponKey);
            var chars = Switcher.Characters;
            for (int i = 0; i < chars.Count; i++)
                if (w != null && chars[i].name == w.fighter) { Switcher.Equip(i); Toast($"직업 변경: {chars[i].DisplayName}", new Color(0.7f, 0.9f, 1f)); }
            return;
        }
        var s = new SkillInstance(near.Def);
        if (key < 4) Socket(key, s);
        else
        {
            int free = System.Array.IndexOf(SkillSystem.Bag, null);
            if (free < 0) { Toast("가방이 가득 찼습니다", new Color(1f, 0.6f, 0.5f)); return; }
            SkillSystem.Bag[free] = s;
            Toast($"{s.Title} — 가방에 넣었습니다", s.def.Color);
        }
    }

    // Into a class skill's free support socket, or over the older of its two
    private void Socket(int main, SkillInstance s)
    {
        int a = main * 2, b = a + 1;
        int at = SkillSystem.Sockets[a] == null ? a : SkillSystem.Sockets[b] == null ? b : a + (replaceNext[main]++ % 2);
        SkillInstance old = SkillSystem.Sockets[at];
        if (old != null && old.def != s.def) s.level = old.level;
        SkillSystem.Sockets[at] = s;
        SkillSystem.RefreshSynergies();
        Toast($"{SkillSystem.MainKeys[main]} 보조 {at - a + 1} — {s.Title}", s.def.Color);
    }
    // Names over the pedestals close by, and what the keys do at the one within reach
    private void DrawShowcase()
    {
        Camera cam = Camera.main;
        if (cam == null || Player == null) return;
        Vector3 p = Player.transform.position;
        SkillShowcase near = SkillShowcase.Nearest(p, 1.8f);
        foreach (var s in SkillShowcase.All)
        {
            if (s == null) continue;
            float dist = Vector3.Distance(new Vector3(s.transform.position.x, 0f, s.transform.position.z), new Vector3(p.x, 0f, p.z));
            if (dist > 5f) continue;
            Vector3 sp = cam.WorldToScreenPoint(s.transform.position + Vector3.up * 2.05f);
            if (sp.z <= 0f) continue;
            SkillDef d = s.Def;
            string label = s.Label;
            var r = new Rect(sp.x - 70f, Screen.height - sp.y - 9f, 140f, 18f);
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((5f - dist) / 2f));
            UiFont.Fill(new Rect(r.x + 18f, r.y, r.width - 36f, r.height), new Color(0f, 0f, 0f, s == near ? 0.8f : 0.45f));
            GUI.color = d != null ? Color.Lerp(Color.white, d.Color, 0.5f) : Color.white;
            GUI.Label(r, label, tiny);
        }
        GUI.color = Color.white;
        // the class names over their groups
        foreach (var g in galleryLabels)
        {
            float gd = Vector2.Distance(new Vector2(g.at.x, g.at.z), new Vector2(p.x, p.z));
            if (gd > 12f) continue;
            Vector3 gp = cam.WorldToScreenPoint(g.at);
            if (gp.z <= 0f) continue;
            var gr = new Rect(gp.x - 90f, Screen.height - gp.y - 11f, 180f, 22f);
            UiFont.Fill(gr, new Color(0.05f, 0.05f, 0.1f, 0.75f));
            GUI.Label(gr, g.title, center);
        }
        if (gathering)
        {
            var wr = new Rect((Screen.width - 260f) / 2f, Screen.height * 0.3f, 260f, 26f);
            UiFont.Fill(wr, new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(wr, "직업 진열장 준비 중…", center);
        }
        if (near == null) return;
        SkillDef nd = near.Def;
        string msg = nd != null ? near.Label + "   보조로: [1] Q  [2] E  [3] R  [4] 우클릭  [5] 가방" : near.Label + "   [1] 이 직업으로 바꾸기";
        var pr = new Rect((Screen.width - 520f) / 2f, Screen.height * 0.58f, 520f, 30f);
        UiFont.Fill(pr, new Color(0f, 0f, 0f, 0.75f));
        GUI.Label(pr, msg, center);
        if (nd != null)
            GUI.Label(new Rect(pr.x, pr.yMax + 2f, pr.width, 18f), nd.desc, UiFont.Style(12, TextAnchor.MiddleCenter, FontStyle.Normal));
    }

    // ═══════════ Dummies ═══════════
    private void ClearDummies()
    {
        foreach (var d in dummies) if (d != null) Destroy(d.gameObject);
        dummies.Clear();
    }

    // 0 a row of three, 1 a crowd of six, 2 one walking back and forth, 3 one boss-type (no freezing)
    private void Lay(int which)
    {
        layout = which;
        ClearDummies();
        Vector3 c = Origin + new Vector3(0f, 0f, 3f);
        switch (which)
        {
            case 0:
                for (int i = -1; i <= 1; i++) Spawn(c + new Vector3(i * 4f, 0f, 0f), false, 0f, "허수아비");
                break;
            case 1:
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI * 2f / 6f;
                    Spawn(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.2f, false, 0f, "허수아비");
                }
                break;
            case 2:
                Spawn(c, false, 6f, "움직이는 허수아비");
                break;
            case 3:
                Spawn(c, true, 0f, "보스형 허수아비");
                break;
        }
    }

    private void Spawn(Vector3 at, bool boss, float sway, string label)
    {
        var d = TrainingDummy.Create(at, dummyHealth, boss, sway, label, Gm);
        if (d != null) dummies.Add(d);
    }

    // ═══════════ What the dummies took ═══════════
    public static void Record(TrainingDummy d, int amount)
    {
        string source = Time.time - SkillSystem.LastCastTime < 3f && SkillSystem.LastCastName != null ? SkillSystem.LastCastName : "무기 공격";
        if (firstHitTime < 0f) firstHitTime = Time.time;
        hits.Add(new HitRecord { time = Time.time, amount = amount, source = source });
        totalDamage += amount;
        hitCount++;
        biggest = Mathf.Max(biggest, amount);
        bySource.TryGetValue(source, out var s);
        bySource[source] = (s.total + amount, s.count + 1, Mathf.Max(s.max, amount));
        Color col = amount >= 100 ? new Color(1f, 0.55f, 0.2f) : amount >= 30 ? new Color(1f, 0.85f, 0.35f) : Color.white;
        floaters.Add(new Floater { at = d.transform.position + Vector3.up * 1.3f + Vector3.right * Random.Range(-0.5f, 0.5f), amount = amount, time = Time.time, color = col });
    }

    private static void ResetStats()
    {
        hits.Clear();
        bySource.Clear();
        floaters.Clear();
        totalDamage = biggest = hitCount = 0;
        firstHitTime = -1f;
    }

    private static float Dps(float window)
    {
        float since = Time.time - window, sum = 0f;
        foreach (var h in hits) if (h.time >= since) sum += h.amount;
        return sum / window;
    }

    // ═══════════ Driver ═══════════
    private void Update()
    {
        if (!inside) return;
        // left some other way (the map, a cutscene, a death): tidy up
        if (Player == null || Gm == null || Gm.InHub || (Player.transform.position - Origin).magnitude > 80f)
        {
            DoExit(false);
            return;
        }
        hits.RemoveAll(h => Time.time - h.time > 30f);
        floaters.RemoveAll(f => Time.time - f.time > 1.1f);
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (panelOpen)
        {
            if (kb.escapeKey.wasPressedThisFrame || kb.tKey.wasPressedThisFrame) ClosePanel();
            return;
        }
        // 쿨타임 없음: every skill is ready again the moment it's used
        if (noCooldowns) CooldownReset.ResetAll(Player.gameObject);
        if (kb.tKey.wasPressedThisFrame && !SkillSystem.Blocking && GameManager.IsRunActive) OpenPanel();
        else if (kb.backspaceKey.wasPressedThisFrame && !SkillSystem.Blocking) ResetCooldowns();
        else if (!SkillSystem.Blocking && GameManager.IsRunActive && Time.timeScale > 0f) ShowcaseKeys(kb);
    }

    // ═══════════ Cooldowns ═══════════
    private static bool noCooldowns;

    private static void ResetCooldowns()
    {
        if (Player == null) return;
        CooldownReset.ResetAll(Player.gameObject);
        Toast("쿨타임 초기화 — 모든 스킬을 바로 쓸 수 있습니다", new Color(0.6f, 0.9f, 1f));
    }

    private void OpenPanel()
    {
        panelOpen = true;
        savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ClosePanel()
    {
        if (!panelOpen) return;
        panelOpen = false;
        Time.timeScale = savedTimeScale;
    }

    // ═══════════ Drawing ═══════════
    private GUIStyle title, text, small, center, tiny, button, num;
    private float uiScale = 1f;
    private float VW => Screen.width / uiScale;
    private float VH => Screen.height / uiScale;
    private int pickSlot;

    private void Styles()
    {
        if (title != null) return;
        title = UiFont.Style(22, TextAnchor.MiddleCenter, FontStyle.Bold);
        text = UiFont.Style(14, TextAnchor.MiddleLeft, FontStyle.Normal);
        small = UiFont.Style(12, TextAnchor.MiddleLeft, FontStyle.Normal);
        center = UiFont.Style(14, TextAnchor.MiddleCenter, FontStyle.Bold);
        tiny = UiFont.Style(10, TextAnchor.MiddleCenter, FontStyle.Bold);
        num = UiFont.Style(20, TextAnchor.MiddleCenter, FontStyle.Bold);
        button = UiArt.ButtonStyle(12);
        text.wordWrap = small.wordWrap = true;
        foreach (var st in new[] { title, text, small, center, tiny, num }) st.normal.textColor = Color.white;
    }

    private void OnGUI()
    {
        if (!inside) return;
        Styles();
        GUI.depth = -55;
        DrawFloaters();
        if (panelOpen)
        {
            uiScale = Mathf.Min(1f, Screen.width / 940f, Screen.height / 720f);
            Matrix4x4 keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            UiFont.Fill(new Rect(0f, 0f, VW, VH), new Color(0f, 0f, 0f, 0.55f));
            DrawPanel();
            GUI.matrix = keep;
            return;
        }
        if (Time.timeScale <= 0f) return;
        DrawShowcase();
        DrawBoard();
        DrawDummyBars();
    }

    private void DrawFloaters()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        foreach (var f in floaters)
        {
            float k = (Time.time - f.time) / 1.1f;
            Vector3 sp = cam.WorldToScreenPoint(f.at + Vector3.up * k * 1.2f);
            if (sp.z <= 0f) continue;
            num.fontSize = f.amount >= 100 ? 26 : f.amount >= 30 ? 22 : 18;
            var r = new Rect(sp.x - 60f, Screen.height - sp.y - 15f, 120f, 30f);
            GUI.color = new Color(0f, 0f, 0f, 0.8f * (1f - k));
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), f.amount.ToString(), num);
            GUI.color = new Color(f.color.r, f.color.g, f.color.b, 1f - k * k);
            GUI.Label(r, f.amount.ToString(), num);
        }
        GUI.color = Color.white;
        num.fontSize = 20;
    }

    // The board at the top: totals, DPS, biggest hit, and damage by skill
    private void DrawBoard()
    {
        // (bottom left, beside the skill bar: clear of the dummies' labels and the toasts)
        float w = Mathf.Min(300f, Screen.width / 2f - 150f);
        float h = 84f + Mathf.Min(6, bySource.Count) * 18f;
        var r = new Rect(8f, Screen.height - 34f - h, w, h);
        UiArt.Tip(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f));
        float span = firstHitTime < 0f ? 0f : Mathf.Max(1f, Time.time - firstHitTime);
        small.wordWrap = false;
        GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 18f), "훈련장 · [T] 설정 · [Backspace] 쿨타임 초기화" + (noCooldowns ? " · 쿨타임 없음" : "") + (SkillSystem.NoCooldown ? " · 기력 무한" : ""), small);
        GUI.color = new Color(0.7f, 0.9f, 1f);
        GUI.Label(new Rect(r.x + 10f, r.y + 22f, r.width - 20f, 20f), $"DPS(5초) {Dps(5f):0.0}   총 피해 {totalDamage}", text);
        GUI.color = Color.white;
        GUI.Label(new Rect(r.x + 10f, r.y + 42f, r.width - 20f, 18f), $"타수 {hitCount}   최대 한 방 {biggest}", small);
        GUI.Label(new Rect(r.x + 10f, r.y + 60f, r.width - 20f, 18f), $"평균 DPS {(span > 0f ? totalDamage / span : 0f):0.0}", small);
        float y = r.y + 80f;
        var list = new List<KeyValuePair<string, (int total, int count, int max)>>(bySource);
        list.Sort((a, b) => b.Value.total.CompareTo(a.Value.total));
        for (int i = 0; i < list.Count && i < 6; i++)
        {
            var kv = list[i];
            float share = totalDamage > 0 ? (float)kv.Value.total / totalDamage : 0f;
            UiFont.Fill(new Rect(r.x + 10f, y + 3f, (r.width - 20f) * share, 12f), new Color(0.6f, 0.85f, 1f, 0.25f));
            GUI.Label(new Rect(r.x + 12f, y, r.width - 20f, 18f), $"{kv.Key}  {kv.Value.total}  ({kv.Value.count}타 · 최대 {kv.Value.max})", small);
            y += 18f;
        }
        small.wordWrap = true;
    }

    // Each dummy's health and its own DPS over its head
    private void DrawDummyBars()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        foreach (var d in dummies)
        {
            if (d == null) continue;
            Vector3 sp = cam.WorldToScreenPoint(d.transform.position + Vector3.up * (d.IsBoss ? 3.6f : 2.7f));
            if (sp.z <= 0f) continue;
            var r = new Rect(sp.x - 50f, Screen.height - sp.y, 100f, 8f);
            UiFont.Fill(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, 0.7f));
            UiFont.Fill(new Rect(r.x, r.y, r.width * d.Health / Mathf.Max(1f, d.MaxHealth), r.height), d.IsBoss ? new Color(0.95f, 0.6f, 0.2f) : new Color(0.85f, 0.25f, 0.25f));
            string status = d.StatusText;
            GUI.Label(new Rect(r.x - 30f, r.y - 18f, r.width + 60f, 16f), d.Label + (status.Length > 0 ? "  " + status : ""), tiny);
            GUI.Label(new Rect(r.x - 30f, r.y + 9f, r.width + 60f, 16f), $"{d.Health}/{d.MaxHealth}  ·  DPS {d.Dps(5f):0.0}", tiny);
        }
    }

    private Rect Box(float w, float h)
    {
        var r = new Rect((VW - w) / 2f, (VH - h) / 2f, w, h);
        UiArt.Panel(r);
        return r;
    }

    // The setup: any support in any socket, its level; a class skill's special version; stamina; dummies; the class
    private int pickSocket;
    private void DrawPanel()
    {
        Rect box = Box(920f, 700f);
        UiArt.Banner(new Rect(box.center.x - 180f, box.y - 28f, 360f, 66f), "훈련장 설정", title);
        GUI.Label(new Rect(box.x, box.y + 44f, box.width, 18f), "보조스킬을 누르면 선택한 칸에 바로 장착됩니다  ·  [T] / [Esc] 닫기", UiFont.Style(12, TextAnchor.MiddleCenter, FontStyle.Normal));

        // ── left: every support, by element ──
        float gx = box.x + 20f, gy = box.y + 70f, cell = 50f, gap = 6f;
        for (int e = 0; e < 8; e++)
            UiArt.Emblem(new Rect(gx + 2f, gy + e * (cell + gap) + 8f, 34f, 34f), e);
        SkillInstance cur = SkillSystem.Sockets[pickSocket];
        for (int i = 0; i < SkillCatalog.All.Count; i++)
        {
            SkillDef d = SkillCatalog.All[i];
            int row = (int)d.element, col = i % 4;
            var r = new Rect(gx + 42f + col * (cell + gap), gy + row * (cell + gap), cell, cell);
            UiArt.Slot(r, cur != null && cur.def == d ? new Color(1.3f, 1.25f, 1.1f) : new Color(0.75f, 0.7f, 0.65f));
            if (d.Icon != null) GUI.DrawTexture(new Rect(r.x + 5f, r.y + 5f, r.width - 10f, r.height - 10f), d.Icon);
            if (GUI.Button(r, new GUIContent("", d.name + " — " + d.desc), GUIStyle.none))
            {
                var s = new SkillInstance(d);
                if (cur != null) s.level = cur.level;
                SkillSystem.Sockets[pickSocket] = s;
                SkillSystem.RefreshSynergies();
            }
        }
        if (!string.IsNullOrEmpty(GUI.tooltip))
            GUI.Label(new Rect(gx, box.yMax - 92f, 290f, 44f), GUI.tooltip, small);

        // ── right: the four class skills and their sockets ──
        float rx = box.x + 330f, ry = box.y + 70f, rw = box.width - 350f;
        GUI.Label(new Rect(rx, ry, 300f, 20f), "직업 스킬과 보조 칸 (칸을 눌러 고르기)", text);
        // 특별 강화: free here — each class skill's button below cycles its versions, [F] lists them all; this takes them all back
        if (GUI.Button(new Rect(rx + rw - 170f, ry - 4f, 170f, 26f), "특별 강화 초기화", button))
            SkillSystem.ClearSpecials();
        for (int m = 0; m < SkillSystem.Mains; m++)
        {
            var w = SkillSystem.MainSkill(m);
            float x = rx + m * 140f;
            var head = new Rect(x, ry + 24f, 132f, 40f);
            UiArt.Inset(head, 0.45f);
            if (w != null && w.icon != null) GUI.DrawTexture(new Rect(head.x + 4f, head.y + 4f, 32f, 32f), w.icon);
            GUI.Label(new Rect(head.x + 40f, head.y + 2f, head.width - 42f, 36f), $"{SkillSystem.MainKeys[m]}\n{(w != null ? w.name : "-")}", UiFont.Style(11, TextAnchor.MiddleLeft, FontStyle.Bold));
            for (int k = 0; k < 2; k++)
            {
                int idx = m * 2 + k;
                var r = new Rect(x + k * 68f, ry + 70f, 62f, 62f);
                SkillInstance s = SkillSystem.Sockets[idx];
                UiArt.Slot(r, pickSocket == idx ? new Color(1.3f, 1.25f, 1.1f) : s != null ? Color.Lerp(Color.white, s.def.Color, 0.35f) : new Color(0.6f, 0.56f, 0.52f));
                if (s != null && s.def.Icon != null) GUI.DrawTexture(new Rect(r.x + 6f, r.y + 6f, r.width - 12f, r.height - 12f), s.def.Icon);
                else GUI.Label(r, "보조", tiny);
                if (s != null && s.level > 0) GUI.Label(new Rect(r.xMax - 26f, r.yMax - 16f, 24f, 14f), "+" + s.level, tiny);
                if (GUI.Button(r, "", GUIStyle.none)) pickSocket = idx;
            }
            // the class skill's special version (four, effects still to come)
            if (w != null)
            {
                int have = SkillSystem.SpecialOf(w.Key);
                if (GUI.Button(new Rect(x, ry + 138f, 132f, 24f), have < 0 ? "특수 강화: 없음" : SkillSystem.SpecialVersionName(w.Key, have), button))
                    SkillSystem.SetSpecial(w.Key, have >= SkillSystem.SpecialVersions - 1 ? -1 : have + 1);
            }
        }
        float y = ry + 172f;
        if (cur != null)
        {
            GUI.color = cur.def.Color;
            GUI.Label(new Rect(rx, y, 200f, 24f), cur.Title, text);
            GUI.color = Color.white;
            if (GUI.Button(new Rect(rx + 200f, y, 40f, 26f), "-", button) && cur.level > 0) { cur.level--; SkillSystem.RefreshSynergies(); }
            if (GUI.Button(new Rect(rx + 244f, y, 40f, 26f), "+", button)) { cur.level++; SkillSystem.RefreshSynergies(); }
            if (GUI.Button(new Rect(rx + 288f, y, 60f, 26f), "+10", button)) { cur.level += 10; SkillSystem.RefreshSynergies(); }
            if (GUI.Button(new Rect(rx + 356f, y, 90f, 26f), "칸 비우기", button)) { SkillSystem.Sockets[pickSocket] = null; SkillSystem.RefreshSynergies(); }
            y += 30f;
            var st = SkillSystem.StatsOf(cur);
            GUI.Label(new Rect(rx, y, rw, 36f), $"피해 ×{cur.LevelDamage:0.00}  ·  {SkillSystem.StatText(st[0].stat, st[0].value)}  ·  {SkillSystem.StatText(st[1].stat, st[1].value)}", small);
        }
        else GUI.Label(new Rect(rx, y, rw, 24f), "왼쪽에서 보조스킬을 골라 이 칸에 장착하세요", small);
        y += 40f;

        // ── options ──
        UiFont.Fill(new Rect(rx, y, rw, 1f), new Color(1f, 1f, 1f, 0.15f));
        y += 10f;
        GUI.color = SkillSystem.NoCooldown ? new Color(0.6f, 0.85f, 1f) : Color.white;
        if (GUI.Button(new Rect(rx, y, 150f, 30f), SkillSystem.NoCooldown ? "기력 무한: 켜짐" : "기력 무한: 꺼짐", button)) SkillSystem.NoCooldown = !SkillSystem.NoCooldown;
        GUI.color = DebugCheats.Invincible ? new Color(0.6f, 0.85f, 1f) : Color.white;
        if (GUI.Button(new Rect(rx + 158f, y, 120f, 30f), DebugCheats.Invincible ? "무적: 켜짐" : "무적: 꺼짐", button)) DebugCheats.Invincible = !DebugCheats.Invincible;
        GUI.color = Color.white;
        if (GUI.Button(new Rect(rx + 286f, y, 130f, 30f), "기록 초기화", button)) ResetStats();
        if (GUI.Button(new Rect(rx + 424f, y, 130f, 30f), "레벨 업 보상", button)) RunState.AddXp(RunState.XpToNext - RunState.Xp);
        y += 36f;
        // 쿨타임: put every skill back now, or keep them all ready while training
        if (GUI.Button(new Rect(rx, y, 150f, 30f), "쿨타임 초기화", button)) ResetCooldowns();
        GUI.color = noCooldowns ? new Color(0.6f, 0.85f, 1f) : Color.white;
        if (GUI.Button(new Rect(rx + 158f, y, 150f, 30f), noCooldowns ? "쿨타임 없음: 켜짐" : "쿨타임 없음: 꺼짐", button)) noCooldowns = !noCooldowns;
        GUI.color = Color.white;
        GUI.Label(new Rect(rx + 316f, y + 6f, rw - 316f, 20f), "[Backspace] 바로 초기화", small);
        y += 40f;

        GUI.Label(new Rect(rx, y, 200f, 22f), "허수아비 배치", text);
        y += 24f;
        string[] layouts = { "일렬 3개", "무리 6개", "움직이는 1개", "보스형 1개" };
        for (int i = 0; i < layouts.Length; i++)
        {
            GUI.color = layout == i ? new Color(0.6f, 0.85f, 1f) : Color.white;
            if (GUI.Button(new Rect(rx + i * 138f, y, 132f, 30f), layouts[i], button)) { Lay(i); ResetStats(); }
        }
        GUI.color = Color.white;
        y += 36f;
        GUI.Label(new Rect(rx, y + 4f, 120f, 22f), "허수아비 체력", small);
        int[] hps = { 500, 5000, 50000 };
        for (int i = 0; i < hps.Length; i++)
        {
            GUI.color = dummyHealth == hps[i] ? new Color(0.6f, 0.85f, 1f) : Color.white;
            if (GUI.Button(new Rect(rx + 110f + i * 96f, y, 90f, 28f), hps[i].ToString(), button)) { dummyHealth = hps[i]; Lay(layout); }
        }
        GUI.color = Color.white;
        GUI.Label(new Rect(rx, y + 32f, rw, 34f), "보스형은 넘어지지 않고 빙결이 짧습니다. 체력이 0이 되면 다시 차오르고 처치 횟수만 셉니다.", small);

        // ── the class in hand ──
        float cy = box.yMax - 96f;
        GUI.Label(new Rect(rx, cy - 22f, 300f, 20f), "직업 바꾸기", small);
        var sw = Switcher;
        if (sw != null)
        {
            var chars = sw.Characters;
            float bw = Mathf.Min(84f, (rw - 6f * (chars.Count - 1)) / chars.Count);
            for (int i = 0; i < chars.Count; i++)
            {
                GUI.color = chars[i] == sw.Current ? new Color(0.6f, 0.85f, 1f) : Color.white;
                if (GUI.Button(new Rect(rx + i * (bw + 6f), cy, bw, 30f), new GUIContent(chars[i].WeaponName, chars[i].DisplayName), button)) sw.Equip(i);
            }
            GUI.color = Color.white;
        }
        if (GUI.Button(new Rect(box.xMax - 300f, box.yMax - 46f, 140f, 34f), "훈련장 나가기", button)) { DoExit(true); return; }
        if (GUI.Button(new Rect(box.xMax - 150f, box.yMax - 46f, 130f, 34f), "닫기 [T]", button)) ClosePanel();
    }
}
// A training dummy: an enemy that never strikes back and never falls (its health fills up again)
public class TrainingDummy : Enemy
{
    private int health, maxHealth;
    private float sway;
    private Vector3 home;
    private float downUntil, flashUntil;
    private int felled;
    private readonly List<(float time, int amount)> taken = new List<(float, int)>();
    private Transform body;
    public string Label { get; private set; }

    public override int Health => health;
    public override int MaxHealth => maxHealth;

    public static TrainingDummy Create(Vector3 at, int hp, bool boss, float sway, string label, GameManager gm)
    {
        var go = new GameObject("TrainingDummy");
        go.SetActive(false);
        go.transform.position = at;
        go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        var d = go.AddComponent<TrainingDummy>();
        d.health = d.maxHealth = hp;
        d.IsBoss = boss;
        d.sway = sway;
        d.home = at;
        d.Label = label;
        d.GoldDrop = 0;
        d.XpDrop = 0;
        d.DisplayName = label;
        var col = go.AddComponent<CapsuleCollider>();
        col.center = Vector3.up * 1f;
        col.height = 2f;
        col.radius = 0.45f;
        // the body: the VARCO training dummy (Resources/Training/TrainingDummy), else an einherjar's figure stood still in stone
        d.body = Model(go.transform);
        if (d.body == null)
        {
            Enemy prefab = gm != null ? typeof(GameManager).GetField("einherjarPrefab", (System.Reflection.BindingFlags)36)?.GetValue(gm) as Enemy : null;
            d.body = prefab != null ? Strip(prefab.gameObject, go.transform) : null;
            d.stone = true;
        }
        if (d.body == null)
        {
            Material straw = Mats.Get("train_straw", new Color(0.8f, 0.68f, 0.4f));
            d.body = Mats.Shape(PrimitiveType.Capsule, go.transform, Vector3.up * 1f, new Vector3(0.8f, 1f, 0.8f), straw).transform;
        }
        if (boss) go.transform.localScale = Vector3.one * 1.45f;
        // a post and base under it, and a ring on the floor
        Material wood = Mats.Get("train_wood", new Color(0.45f, 0.3f, 0.18f), 0.25f);
        if (d.stone) Mats.Shape(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.06f, 0f), new Vector3(1.3f, 0.06f, 1.3f), wood, false, "Base");
        Ambient.RuneHalo(go.transform, Vector3.up * 0.13f, boss ? 1.6f : 1.1f, boss ? new Color(1f, 0.6f, 0.2f) * 0.7f : new Color(0.6f, 0.8f, 1f) * 0.5f);
        go.SetActive(true);
        if (d.body != null) d.bodyRest = d.body.localRotation;
        if (d.stone) d.SetBaseTint(boss ? new Color(0.95f, 0.72f, 0.45f) : new Color(0.78f, 0.74f, 0.66f));
        else if (boss) d.SetBaseTint(new Color(1f, 0.8f, 0.6f));
        return d;
    }

    private bool stone;
    private Quaternion bodyRest = Quaternion.identity;
    private static GameObject model;
    private static bool modelLooked;

    // The VARCO model, stood on the floor and sized to a man's height (2 m)
    private static Transform Model(Transform parent)
    {
        if (!modelLooked) { modelLooked = true; model = Resources.Load<GameObject>("Training/TrainingDummy"); }
        if (model == null) return null;
        GameObject copy = Instantiate(model, parent);
        copy.name = "Body";
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = model.transform.localRotation;   // (an FBX root keeps its own axis turn)
        var rs = copy.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float k = b.size.y > 0.01f ? 2f / b.size.y : 1f;
            copy.transform.localScale *= k;
            // feet on the floor
            b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            copy.transform.position += Vector3.up * (parent.position.y - b.min.y);
        }
        foreach (var c in copy.GetComponentsInChildren<Collider>()) Destroy(c);
        return copy.transform;
    }

    // A copy of the prefab with its scripts, colliders and agents taken off (only the looks and the animator stay)
    private static Transform Strip(GameObject prefab, Transform parent)
    {
        var holder = new GameObject("hold");
        holder.SetActive(false);
        GameObject copy = Instantiate(prefab, holder.transform);
        copy.name = "Body";
        // (last added first: a script that requires another is usually added after it; a few passes catch the rest)
        for (int pass = 0; pass < 4; pass++)
        {
            var all = copy.GetComponentsInChildren<Behaviour>(true);
            bool any = false;
            for (int i = all.Length - 1; i >= 0; i--)
                if (all[i] != null && !(all[i] is Animator)) { DestroyImmediate(all[i]); any = true; }
            if (!any) break;
        }
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        Destroy(holder);
        return copy.transform;
    }

    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            EnemyStatus st = EnemyStatus.Of(this);
            if (st != null)
            {
                if (st.Burning) parts.Add("화상");
                if (st.Frozen) parts.Add("빙결");
                if (st.Shocked) parts.Add("감전");
            }
            if (Time.time < downUntil) parts.Add("넘어짐");
            if (felled > 0) parts.Add($"처치 {felled}");
            return string.Join(" · ", parts);
        }
    }

    public float Dps(float window)
    {
        float since = Time.time - window, sum = 0f;
        foreach (var t in taken) if (t.time >= since) sum += t.amount;
        return sum / window;
    }

    public override void TakeHit(Vector3 from, int damage)
    {
        if (damage <= 0) return;
        damage = Mitigate(damage);
        health -= damage;
        taken.Add((Time.time, damage));
        TrainingRoom.Record(this, damage);
        FlashTint(new Color(1.6f, 1.6f, 1.6f));
        flashUntil = Time.time + 0.07f;
        if (body != null) body.localRotation = Quaternion.Euler(-8f, 0f, 0f) * bodyRest;
        if (health <= 0)
        {
            felled++;
            health = maxHealth;
        }
    }

    public override void Knockdown(float seconds)
    {
        if (IsBoss) return;
        downUntil = Mathf.Max(downUntil, Time.time + seconds);
    }

    public override void Scale(float healthMultiplier, float damageMultiplier) { }

    private void Update()
    {
        taken.RemoveAll(t => Time.time - t.time > 30f);
        if (flashUntil > 0f && Time.time > flashUntil) { flashUntil = 0f; ClearFlash(); }
        if (body != null)
        {
            // knocked over while down, else easing back upright from a hit's lean
            Quaternion want = (Time.time < downUntil ? Quaternion.Euler(-70f, 0f, 0f) : Quaternion.identity) * bodyRest;
            body.localRotation = Quaternion.Slerp(body.localRotation, want, Time.deltaTime * 10f);
        }
        if (sway > 0f && Time.time >= downUntil)
            transform.position = home + Vector3.right * Mathf.Sin(Time.time * 0.9f) * sway;
    }
}

// One pedestal of the training room's showcase: a skill's icon turning to face the camera over a marble plinth.
// def null = a class's own skill (weaponKey "fighter/Q"), its name and icon from that class's skill bar.
public class SkillShowcase : MonoBehaviour
{
    private static readonly List<SkillShowcase> all = new List<SkillShowcase>();
    public static List<SkillShowcase> All => all;
    private SkillDef def;
    private string weaponKey;
    private Transform icon;
    private Material iconMat;
    private Vector3 iconHome;

    public SkillDef Def => def;
    public string WeaponKey => weaponKey;

    public string Label
    {
        get
        {
            if (def != null) return def.name;
            var w = SkillSystem.WeaponSkill(weaponKey);
            return w == null ? "직업 스킬" : $"{SkillSystem.WeaponKeyName(w.key)} · {w.name}";
        }
    }

    public static SkillShowcase Create(Transform parent, Vector3 local, SkillDef def, Material marble, Material gold, string weaponKey = null)
    {
        var go = new GameObject(def != null ? "Showcase_" + def.id : "Showcase_" + weaponKey);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var s = go.AddComponent<SkillShowcase>();
        s.def = def;
        s.weaponKey = weaponKey;
        Color c = def != null ? def.Color : new Color(0.9f, 0.9f, 1f);
        Mats.Shape(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.35f, 0f), new Vector3(0.75f, 0.35f, 0.75f), marble, true, "Plinth");
        Mats.Shape(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.72f, 0f), new Vector3(0.9f, 0.03f, 0.9f), gold, false, "PlinthCap");
        // the icon
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(q.GetComponent<Collider>());
        q.name = "Icon";
        q.transform.SetParent(go.transform, false);
        q.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        q.transform.localScale = Vector3.one * 0.75f;
        s.iconMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        Texture2D tex = def != null ? def.Icon : SkillSystem.WeaponSkill(weaponKey)?.icon;
        if (tex != null) s.iconMat.SetTexture("_BaseMap", tex);
        q.GetComponent<Renderer>().sharedMaterial = s.iconMat;
        s.icon = q.transform;
        s.iconHome = q.transform.localPosition;
        Vfx.Glow(go.transform, new Vector3(0f, 1.35f, 0f), 1.5f, c * 0.6f, "glow", 0f);
        return s;
    }

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    private void Update()
    {
        Camera cam = Camera.main;
        if (icon == null) return;
        if (cam != null) icon.rotation = cam.transform.rotation;
        icon.localPosition = iconHome + Vector3.up * Mathf.Sin(Time.time * 2f + transform.position.x) * 0.05f;
        // a class skill's icon, once its class's bar has been seen
        if (def == null && iconMat.GetTexture("_BaseMap") == null)
        {
            Texture2D t = SkillSystem.WeaponSkill(weaponKey)?.icon;
            if (t != null) iconMat.SetTexture("_BaseMap", t);
        }
    }

    public static SkillShowcase Nearest(Vector3 p, float within)
    {
        SkillShowcase best = null;
        float bestD = within;
        foreach (var s in all)
        {
            if (s == null) continue;
            Vector3 d = s.transform.position - p;
            d.y = 0f;
            if (d.magnitude < bestD) { bestD = d.magnitude; best = s; }
        }
        return best;
    }
}