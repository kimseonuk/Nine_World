using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Týr's patterns after the user's design notes (티르 패턴):
//  끊어진 맹세  — he drives his sword into the floor; golden rune seals burst up one after another along lines racing at
//                the player, chained together. Caught, the player is held fast while he leaps on them with a slam.
//  늑대의 굴레  — he raises his arm and the spectral wolf (Fenrir, the beast that took his hand) appears and charges
//                straight at the player; when its charge ends Týr runs the same lane after it and cuts.
//  승리의 룬    — he raises his sword to the sky: rune swords form round him and fly at the player one by one; the last
//                he hurls himself, a heavy blow that bursts where it lands.
public partial class TyrBoss
{
    private static readonly Color OathGold = new Color(1f, 0.82f, 0.4f);

    // ───────────── 끊어진 맹세 ─────────────

    private IEnumerator BrokenOath()
    {
        Warned?.Invoke("끊어진 맹세! 룬의 사슬이 뻗어옵니다 — 붙잡히면 움직일 수 없습니다");
        FacePlayer();
        float plant = Cast(SlamHit + 0.1f);
        warnings.Add(TyrFx.Seal(transform.position, 6f, OathGold, 40f));
        yield return Strike(SlamState, SlamHit, plant);
        ClearWarnings();
        GameAudio.Play("ty_chain", transform.position, 1f);
        Vector3 origin = Flat(transform.position + transform.forward * 1f * S);
        TyrFx.Blast(origin, 3f);
        Shockwave.Spawn(origin, 3f, OathGold, 14);
        CameraShake.Shake(0.3f, 0.25f);

        // the lines the seals will run along: at the player and either side of them (marked first)
        int lines = phase == 2 ? 5 : 3;
        float spread = phase == 2 ? 22f : 26f;
        Vector3 aim = Flat(player.position - origin).normalized;
        const float reach = 14f, step = 1.4f;
        var dirs = new List<Vector3>();
        float mark = Cast(0.5f);
        for (int i = 0; i < lines; i++)
        {
            Vector3 d = Quaternion.Euler(0f, (i - (lines - 1) / 2f) * spread, 0f) * aim;
            dirs.Add(d);
            warnings.Add(Telegraph.Line(origin, d, reach, 1.6f, mark));
        }
        yield return new WaitForSeconds(mark);
        ClearWarnings();

        // the seals rise one after another down each line, the chain drawn through them
        var chains = new List<GoldChain>();
        var points = new List<List<Vector3>>();
        foreach (Vector3 d in dirs)
        {
            var c = GoldChain.Make(0.5f, OathGold);
            chains.Add(c);
            warnings.Add(c.gameObject);
            points.Add(new List<Vector3> { origin + Vector3.up * 1.2f });
        }
        bool caught = false;
        int steps = Mathf.CeilToInt(reach / step);
        for (int s = 1; s <= steps && !dead && !caught; s++)
        {
            for (int i = 0; i < dirs.Count; i++)
            {
                Vector3 at = InRoom(origin + dirs[i] * s * step, 0.8f);
                points[i].Add(at + Vector3.up * 1.2f);
                chains[i].SetPath(points[i]);
                VfxPiece seal = Vfx.Sprite("ty_runechain", at + Vector3.up * 1.2f, 1.6f, OathGold * 1.5f, 1.4f, 0.3f, 1f, 30f);
                warnings.Add(seal.gameObject);
                if (s % 3 == 0)
                    Vfx.Flash(at + Vector3.up, 1f, OathGold, 0.12f);
                if (!caught && Flat(player.position - at).magnitude <= 1.1f)
                    caught = true;
            }
            yield return new WaitForSeconds(0.045f);
        }
        if (!caught || dead)
        {
            foreach (GoldChain c in chains) if (c != null) c.FadeOut(0.3f);
            yield return new WaitForSeconds(Cast(0.35f));
            yield break;
        }

        // caught: held fast, chains drawn round them; he leaps and comes down on them
        const float held = 1.35f;
        PlayerEffects.Root(held);
        GameAudio.Play("hm_chain_bind", player.position, 1f);
        Warned?.Invoke("구속! 맹세의 사슬에 묶였습니다");
        foreach (GoldChain c in chains) if (c != null) c.FadeOut(0.25f);
        var binds = new List<GoldChain>();
        for (int i = 0; i < 4; i++)
        {
            var c = GoldChain.Make(0.4f, OathGold);
            binds.Add(c);
            warnings.Add(c.gameObject);
        }
        Vector3 target = Flat(player.position);
        const float radius = 4.5f;
        float leap = held - 0.3f;
        warnings.Add(Telegraph.Circle(target, radius, leap));
        Play(LeapState, 0.06f);
        Vector3 from = transform.position;
        float y = from.y;
        controller.enabled = false;
        dashing = true;
        for (float t = 0f; t < leap && !dead; t += Time.deltaTime)
        {
            float k = t / leap;
            transform.position = Vector3.Lerp(from, new Vector3(target.x, y, target.z), k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 6f;
            if (k > 0.8f) Play(FinalState, 0.05f);
            Vector3 p = player.position + Vector3.up * 0.8f;
            for (int i = 0; i < binds.Count; i++)
            {
                float a = (i * 90f + t * 50f) * Mathf.Deg2Rad;
                binds[i].SetLine(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.2f - Vector3.up * 0.75f, p);
            }
            yield return null;
        }
        transform.position = new Vector3(target.x, y, target.z);
        controller.enabled = true;
        dashing = false;
        ClearWarnings();
        GameAudio.Play("tyr_slam", target, 1f);
        TyrFx.Blast(target, radius);
        TyrFx.Impact(target, radius, 1f);
        Shockwave.Spawn(target, radius, Crimson, 14);
        CameraShake.Shake(0.4f, 0.35f);
        if (InCircle(target, radius))
            HitPlayer(damage * 1.6f, Away(target) * 11f);
        yield return new WaitForSeconds(Cast(0.45f));
        yield return FollowUp();
    }

    // ───────────── 늑대의 굴레 ─────────────

    private IEnumerator WolfFetter()
    {
        Warned?.Invoke("늑대의 굴레! 늑대가 먼저 돌진하고, 티르가 그 뒤를 따라 벱니다");
        FacePlayer();
        Play(RoarState, 0.15f);
        GameAudio.Play("ty_wolf_howl", transform.position, 1f);
        // the wolf takes shape beside him
        Vector3 side = Vector3.Cross(Vector3.up, transform.forward);
        Vector3 spawn = InRoom(Flat(transform.position) + side * 2.5f, 1.5f);
        GameObject wolf = SpectralWolf(spawn, transform.rotation);
        if (wolf != null) Destroy(wolf, 8f);   // (gone even if he's cut off mid-move)
        Vfx.Sprite("tyr_gather", spawn + Vector3.up * 1.2f, 4f, Crimson * 1.4f, 0.8f, 0.4f, 1.1f, 60f);
        warnings.Add(TyrFx.Seal(spawn, 4f, Crimson, 60f));
        yield return new WaitForSeconds(Cast(0.55f));

        int wolves = 1;
        for (int w = 0; w < wolves && !dead; w++)
        {
            // the wolf's lane: from it, through the player and on
            Vector3 from = wolf != null ? Flat(wolf.transform.position) : spawn;
            Vector3 dir = Flat(player.position - from).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            float length = Mathf.Clamp(Flat(player.position - from).magnitude + 6f, 10f, 22f);
            Vector3 to = InRoom(from + dir * length, 1.2f);
            length = Flat(to - from).magnitude;
            const float width = 3.4f;
            float mark = Cast(0.75f);
            warnings.Add(Telegraph.Line(from, dir, length, width, mark));
            if (wolf != null) wolf.transform.rotation = Quaternion.LookRotation(dir);
            yield return new WaitForSeconds(mark);
            ClearWarnings();

            // the wolf's charge
            GameAudio.Play("ty_wolf_charge", from, 1f);
            float time = Mathf.Max(0.25f, length / 24f);
            bool hit = false;
            for (float t = 0f; t < time && !dead; t += Time.deltaTime)
            {
                Vector3 p = Vector3.Lerp(from, to, t / time);
                if (wolf != null)
                {
                    wolf.transform.position = p + Vector3.up * Mathf.Abs(Mathf.Sin(t * 18f)) * 0.35f;
                    if (Random.value < 0.5f)
                        Vfx.Burst(p + Vector3.up * 0.8f - dir * 1.5f, Crimson, 4, 4f, 0.16f, 0.3f);
                }
                if (!hit && DistanceToSegment(Flat(player.position), from, p) <= width / 2f + Body)
                {
                    hit = true;
                    Vector3 across = Vector3.Cross(Vector3.up, dir);
                    HitPlayer(damage * 1.15f, (across * Mathf.Sign(Vector3.Dot(Flat(player.position) - p, across)) + dir) * 9f);
                }
                yield return null;
            }
            TyrFx.Impact(to, 2.5f, 0.6f);
            if (wolf != null)
            {
                Vfx.Sprite("tyr_gather", to + Vector3.up * 1.4f, 5f, Crimson * 1.6f, 0.5f, 1.2f, 0.4f, 90f);
                Vfx.Burst(to + Vector3.up, Crimson, 18, 7f, 0.2f, 0.5f);
                Destroy(wolf);
                wolf = null;
            }
            if (dead) yield break;

            // Týr after it, down the same lane (re-aimed a little at where the player went)
            FacePlayer();
            Vector3 tFrom = Flat(transform.position);
            Vector3 tDir = Vector3.Slerp(dir, Flat(player.position - tFrom).normalized, 0.6f).normalized;
            float tLen = Mathf.Clamp(Flat(player.position - tFrom).magnitude + 4f, 8f, 22f);
            Vector3 tTo = InRoom(tFrom + tDir * tLen, 1.5f);
            float tMark = Cast(0.4f);
            warnings.Add(Telegraph.Line(tFrom, tDir, Flat(tTo - tFrom).magnitude, 3f, tMark));
            transform.rotation = Quaternion.LookRotation(tDir);
            yield return new WaitForSeconds(tMark);
            ClearWarnings();
            Play(DashState, 0.05f);
            GameAudio.Play("tyr_charge", transform.position, 1f);
            var streak = new GameObject("TyrStreak");
            streak.transform.SetParent(transform, false);
            streak.transform.localPosition = Vector3.up;
            Vfx.Trail(streak, Crimson, 1.6f, 0.4f);
            yield return Dash(tFrom, tTo, 30f, 3f, damage * 1.2f, 9f);
            Destroy(streak, 0.4f);
            // the cut at the end of the run: a short fan of fire
            Vector3 fwd = transform.forward;
            Play(CleaveState, 0.05f);
            float cut = Cast(0.3f);
            warnings.Add(SectorTelegraph.Spawn(transform.position, fwd, 0.5f, 5.5f, 70f, cut));
            yield return new WaitForSeconds(cut);
            ClearWarnings();
            TyrFx.Cleave(transform.position, fwd, 5.5f, 1.2f);
            CameraShake.Shake(0.25f, 0.2f);
            if (InSector(transform.position, fwd, 5.5f, 70f))
                HitPlayer(damage * 1.1f, fwd * 8f);
            Play(LocoState, 0.2f);
        }
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // Fenrir in spectral form (VARCO's ty_wolfspirit painting): a lunging wolf of crimson flame, as big as a horse,
    // always shown side-on to the camera and turned the way it runs, with a light of its own
    private GameObject SpectralWolf(Vector3 at, Quaternion facing)
    {
        var wolf = new GameObject("SpectralWolf");
        wolf.transform.SetPositionAndRotation(at, facing);
        var body = new GameObject("Spirit");
        body.transform.SetParent(wolf.transform, false);
        body.transform.localPosition = Vector3.up * 1.6f;
        body.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
        var r = body.AddComponent<MeshRenderer>();
        r.sharedMaterial = Vfx.Material("ty_wolfspirit", true);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        body.AddComponent<WolfSpirit>().Init(r, wolf.transform, 6.4f, 3.2f, Crimson);
        var light = new GameObject("WolfLight").AddComponent<Light>();
        light.transform.SetParent(wolf.transform, false);
        light.transform.localPosition = Vector3.up * 1.5f;
        light.type = LightType.Point;
        light.color = Crimson;
        light.range = 8f;
        light.intensity = 3f;
        Vfx.Sparkle(wolf, Crimson, 8f, 0.2f);
        return wolf;
    }

    // ───────────── 승리의 룬 ─────────────

    private IEnumerator VictoryRune()
    {
        Warned?.Invoke("승리의 룬! 룬 검이 차례로 날아오고, 마지막 검은 티르가 직접 던집니다");
        FacePlayer();
        Play(RoarState, 0.15f);
        GameAudio.Play("ty_runeswords", transform.position, 1f);
        int count = phase == 2 ? 6 : 4;
        var swords = new List<GameObject>();
        for (int i = 0; i < count; i++)
        {
            GameObject sw = BossSkillFx.SwordModel("TyrRuneSword", 3.2f);
            if (sw == null) break;
            // rune-fire round each blade, so they read against the dark floor
            Vfx.Glow(sw.transform, Vector3.up * 0.55f, 0.55f, Crimson * 1.8f, "glow");
            Vfx.Trail(sw, Crimson, 0.5f, 0.25f);
            Vfx.Sparkle(sw, Gold, 6f, 0.12f);
            Vfx.Sprite("tyr_gather", sw.transform.position + Vector3.up * 2f, 2.4f, Crimson * 1.4f, 0.6f, 0.4f, 1.2f, 90f);
            swords.Add(sw);
            warnings.Add(sw);
        }
        // they form in an arc behind and above him, points toward the player
        float form = Cast(0.7f);
        for (float t = 0f; t < form + 0.01f && !dead; t += Time.deltaTime)
        {
            ArrangeRuneSwords(swords, Mathf.Clamp01(t / form));
            yield return null;
        }
        // one by one at the player
        float gap = Cast(phase == 2 ? 0.32f : 0.42f);
        for (int i = 0; i < swords.Count && !dead; i++)
        {
            GameObject sw = swords[i];
            if (sw == null) continue;
            Vector3 from = sw.transform.position;
            Vector3 target = Flat(player.position);
            Vector3 dir = (target - Flat(from)).normalized;
            Vector3 to = InRoom(target + dir * 3f, 0.5f) + Vector3.up * 0.4f;
            float mark = Cast(0.38f);
            GameObject line = Telegraph.Line(Flat(from), dir, Flat(to - from).magnitude, 1.2f, mark);
            warnings.Add(line);
            for (float t = 0f; t < mark && !dead; t += Time.deltaTime)
            {
                ArrangeRuneSwords(swords, 1f, i);
                sw.transform.rotation = Quaternion.FromToRotation(Vector3.up, -(to - from).normalized);
                yield return null;
            }
            warnings.Remove(line);
            Destroy(line);
            warnings.Remove(sw);
            swords[i] = null;
            GameAudio.Play("ty_rune_fly", from, 0.9f);
            FlyingBlade.Fling(sw, from, to, 30f, 1.2f, Crimson, this, playerStats, damage * 0.9f);
            if (i + 1 < swords.Count)
                yield return new WaitForSeconds(Mathf.Max(0f, gap - mark));
        }
        if (dead) yield break;

        // the last one is his own throw: a great rune blade hurled from his hand, bursting where it lands
        FacePlayer();
        Play(ChargeState, 0.1f);
        GameObject big = BossSkillFx.SwordModel("TyrRuneSword", 3.6f);
        Vector3 hand = transform.position + transform.forward * 0.4f * S + Vector3.up * 2.4f * S;
        if (big != null)
        {
            Vfx.Glow(big.transform, Vector3.up * 0.5f, 1.2f, Gold * 1.4f, "glow");
            warnings.Add(big);
        }
        Vector3 landing = InRoom(Flat(player.position), 1f);
        const float burst = 3.6f;
        float aim = Cast(0.75f);
        GameAudio.Play("tyw_throw", transform.position, 1f);
        warnings.Add(Telegraph.Line(Flat(transform.position), Flat(landing - transform.position).normalized, Flat(landing - transform.position).magnitude, 1.6f, aim));
        warnings.Add(Telegraph.Circle(landing, burst, aim + 0.25f));
        TyrFx.Gather(transform, Vector3.up * 2.2f * S, 2f, aim);
        for (float t = 0f; t < aim && !dead; t += Time.deltaTime)
        {
            if (big != null)
            {
                big.transform.position = hand;
                big.transform.rotation = Quaternion.FromToRotation(Vector3.up, -(landing + Vector3.up * 0.4f - hand).normalized);
            }
            yield return null;
        }
        if (dead) yield break;
        Play(BlastState, 0.05f);
        GameAudio.Play("ty_throw", transform.position, 1f);
        if (big != null)
        {
            warnings.Remove(big);
            FlyingBlade.Fling(big, hand, landing + Vector3.up * 0.4f, 24f, 1.6f, Gold, this, playerStats, damage * 1.5f, burst);
        }
        yield return new WaitForSeconds(Flat(landing - hand).magnitude / 24f + 0.05f);
        ClearWarnings();
        TyrFx.Blast(landing, burst);
        TyrFx.Impact(landing, burst, 0.9f);
        yield return new WaitForSeconds(Cast(0.4f));
        yield return FollowUp();
    }

    private void ArrangeRuneSwords(List<GameObject> swords, float k, int aiming = -1)
    {
        int n = swords.Count;
        Vector3 back = -transform.forward, side = transform.right;
        for (int i = 0; i < n; i++)
        {
            GameObject sw = swords[i];
            if (sw == null) continue;
            float a = (i - (n - 1) / 2f) / Mathf.Max(1f, (n - 1) / 2f);   // -1 … 1 across the arc
            Vector3 spot = transform.position + back * 0.6f * S + side * a * 3.4f * S
                + Vector3.up * (2.6f + (1f - a * a) * 1.0f) * S;
            Vector3 rise = spot - Vector3.up * 2f;
            sw.transform.position = Vector3.Lerp(rise, spot, Mathf.SmoothStep(0f, 1f, k));
            if (i == aiming) continue;
            Vector3 toPlayer = player.position + Vector3.up * 0.5f - sw.transform.position;
            sw.transform.rotation = Quaternion.FromToRotation(Vector3.up, -toPlayer.normalized);
        }
    }

#if UNITY_EDITOR
    public string EditorTestPattern(string name)
    {
        if (player == null) return "no player";
        IEnumerator r = name switch
        {
            "BrokenOath" => BrokenOath(),
            "Wolf" => WolfFetter(),
            "Rune" => VictoryRune(),
            _ => null
        };
        if (r == null) return "unknown " + name;
        testHold = true;
        StopAction();
        Act(r, true);
        return "started " + name + " (phase " + phase + ")";
    }

    public string EditorTestHold()
    {
        testHold = true;
        StopAction();
        return "held";
    }

    public void EditorTestResume() { testHold = false; restTimer = 0.3f; }
#endif
}

// The spirit wolf's painting: turned to the camera, flipped to face the way its owner runs, breathing and flickering
public class WolfSpirit : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private Renderer r;
    private Transform owner;
    private float length, height, t;
    private Color color;
    private MaterialPropertyBlock block;

    public void Init(Renderer renderer, Transform runner, float len, float tall, Color c)
    {
        r = renderer; owner = runner; length = len; height = tall; color = c;
        block = new MaterialPropertyBlock();
    }

    private void LateUpdate()
    {
        t += Time.deltaTime;
        Camera cam = Camera.main;
        if (cam == null || owner == null) return;
        transform.rotation = cam.transform.rotation;
        float side = Vector3.Dot(owner.forward, cam.transform.right) >= 0f ? 1f : -1f;
        float breathe = 1f + Mathf.Sin(t * 14f) * 0.05f;
        float grow = Mathf.Clamp01(t / 0.35f);
        transform.localScale = new Vector3(length * side * breathe, height * (2f - breathe), 1f) * Mathf.Lerp(0.3f, 1f, grow);
        block.SetColor(ColorId, Color.Lerp(Color.white, color, 0.35f) * (1.5f + Mathf.Sin(t * 23f) * 0.2f) * grow);
        r.SetPropertyBlock(block);
    }
}
