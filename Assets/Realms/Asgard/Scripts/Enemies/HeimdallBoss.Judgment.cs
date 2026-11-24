using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Heimdall's patterns after the user's design notes (헤임달 스킬패턴):
//  천리안 (reworked)  — he raises Hofund, the arena darkens and time seems to stall (the player keeps their own pace),
//                      then he blinks round the player and cuts three or four times, each cut marked a beat before,
//                      and is left open for a moment after the last
//  빛 3갈래           — three bolts of light at once; strengthened, they part left and right after a distance; varied,
//                      the middle flies straight and the outer two bend toward the player
//  천상의 낙인        — a brand of light set on the player follows them, then fixes and bursts in a pillar; dodging it at
//                      the last moment leaves him reeling
//  심판의 사슬        — Hofund planted in the floor, golden chains race out after the player; caught, they're held fast
//                      while he comes down on them with a great cut
//  천상의 십자        — a horizontal cut, a vertical cut, and the cross they make bursts with light
//  황금의 검진        — six golden swords wheel round him as he advances, then fly outward together
//  빛의 분열          — a great orb splits in three, and the three converge on the player's spot
//  심판의 포효        — Gjallarhorn shakes the whole arena (a brief stagger, a short slow), then he charges as it fades
//  라그나로크         — (last phase) from the middle he sounds the final horn: walls of light close in from the rim,
//                      each broken by safe gaps, and the middle itself bursts at the end
public partial class HeimdallBoss
{
    private float openUntil;            // the moment after 천리안's last cut: hits land harder
    private bool OpenToHits => Time.time < openUntil;

    private PlayerMovement Mover => player != null ? player.GetComponent<PlayerMovement>() : null;

    private void Hurt(float amount, Vector3 push)
    {
        if (playerStats == null)
            return;
        playerStats.TakeDamage(amount, this);
        if (push.sqrMagnitude > 0.01f)
            Mover?.Push(push);
    }

    private Vector3 AwayFrom(Vector3 from)
    {
        Vector3 d = Flat(player.position - from);
        return d.sqrMagnitude > 0.01f ? d.normalized : transform.forward;
    }

    // The player is rolling or dashing right now (their own untouchable moment)
    private bool PlayerDodging()
    {
        if (player == null) return false;
        PlayerMovement m = Mover;
        if (m != null && m.IsDashing) return true;
        DualBlades blades = player.GetComponent<DualBlades>();
        return blades != null && blades.IsActive && blades.Invulnerable;
    }

    private void Place(Vector3 at)
    {
        bool was = controller.enabled;
        controller.enabled = false;
        transform.position = new Vector3(at.x, transform.position.y, at.z);
        controller.enabled = was;
    }

    // ───────────── 천리안 (reworked) ─────────────

    private IEnumerator Foresight()
    {
        GameAudio.Play("heim_sight", transform.position, 0.9f);
        GameAudio.Play("hm_timeslow", transform.position, 1f);
        Warned?.Invoke("천리안! 헤임달이 베어낼 방향을 미리 봅니다 — 표시된 참격 방향을 읽고 피하세요");
        var gold = HeimdallFx.Gold;
        FacePlayer();
        // 1. Hofund raised; 2. the world dims and stalls
        if (animator != null)
            animator.SetTrigger(CastHash);
        float windup = Cast(1.1f);
        WorldDim.Run(0.72f, 0.35f, windup + 2.6f, 0.6f);
        GameObject eye = Vfx.Glow(transform, Vector3.up * 5.5f, 3.6f, Color.white * 1.1f, "heimdalleye");
        warnings.Add(eye);
        VfxPiece clock = Vfx.Decal(Flat(player.position) + Vector3.up * 0.06f, 6f, Color.white * 1.4f, "timerune", -20f);
        warnings.Add(clock.gameObject);
        Vfx.Gather(transform.position + Vector3.up * 2.4f * S, 3.5f, gold, windup, 70f);
        // his own motion drags as the moment stretches
        if (animator != null) animator.speed = 0.35f;
        for (float t = 0f; t < windup; t += Time.deltaTime)
        {
            if (clock != null)
            {
                clock.transform.position = Flat(player.position) + Vector3.up * 0.06f;
                clock.Set(Color.white * Mathf.Lerp(1.8f, 0.9f, t / windup), Mathf.Lerp(7f, 4.5f, t / windup));
            }
            yield return null;
        }
        if (animator != null) animator.speed = 1f;
        if (clock != null) Destroy(clock.gameObject);

        // 3–4. round the player: blink, mark the cut, cut
        int cuts = phase == 3 ? 4 : 3;
        float last = Random.Range(0f, 360f);
        for (int i = 0; i < cuts && !dead; i++)
        {
            float angle = last + Random.Range(110f, 170f) * (Random.value < 0.5f ? -1f : 1f);
            last = angle;
            Vector3 around = Flat(player.position) + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 3.2f;
            Vector3 spot = InArena(around, 1.2f);
            Blink(transform.position, spot, gold);
            GameAudio.Play("hm_tpslash", spot, 0.9f);
            Place(spot);
            FacePlayer();
            Vector3 dir = transform.forward;
            Vector3 from = Flat(transform.position) - dir * 1f;
            const float length = 9f, width = 2.6f;
            float mark = Cast(i == 0 ? 0.62f : phase == 3 ? 0.4f : 0.46f);
            GameAudio.Play("hmw_cut", spot, 1f);
            warnings.Add(Telegraph.Line(from, dir, length, width, mark));
            Vfx.Gather(transform.position + Vector3.up * 1.4f * S, 1.4f * S, gold, mark, 50f);
            yield return new WaitForSeconds(mark);
            ClearWarning();
            if (animator != null) animator.SetTrigger(SlashHash);
            BossSkillFx.SlashLine(from, dir, length, width, gold, 0.4f);
            SwordBeam.Fire(from + Vector3.up * S, dir, length, width * 0.6f, gold);
            CameraShake.Shake(0.15f, 0.2f);
            if (DistanceToSegment(Flat(player.position), from, from + dir * length) <= width / 2f + 0.35f)
                Hurt(damage * 1.05f, Vector3.Cross(Vector3.up, dir) * 7f);
            yield return new WaitForSeconds(Cast(0.12f));
        }
        if (eye != null) Destroy(eye);
        WorldDim.Release();

        // 5. the opening: he lowers Hofund, spent for a moment
        Warned?.Invoke("빈틈! 지금 공격하세요");
        openUntil = Time.time + 1.5f;
        PlayState(StaggerState, 0.15f);
        HeimdallFx.CircleFlash(transform.position, 4f, new Color(0.7f, 0.85f, 1f), 1.2f);
        Vfx.Sprite("glad_lock", transform.position + Vector3.up * 2.6f * S, 1.6f, Color.white, 1.4f, 0.8f, 1f, 0f);
        yield return new WaitForSeconds(1.5f);
        PlayState(LocomotionState, 0.2f);
    }

    // ───────────── 빛 3갈래 ─────────────

    // mode 0: three at once; 1: they part left and right after a distance; 2: the middle straight, the outer two bend in
    private IEnumerator TriLight(Vector3 center, Color gold, int mode)
    {
        GameAudio.Play("hmw_tri", transform.position, 1f);
        Vector3 aim = Flat(player.position - transform.position).normalized;
        if (aim.sqrMagnitude < 0.01f) aim = transform.forward;
        transform.rotation = Quaternion.LookRotation(aim);
        float spread = mode == 2 ? 28f : 24f;
        float windup = Cast(phase == 3 ? 0.6f : 0.75f);
        float length = arenaRadius * 2f;
        foreach (float a in new[] { -spread, 0f, spread })
            warnings.Add(Telegraph.Line(transform.position, Quaternion.Euler(0f, a, 0f) * aim, mode == 1 ? 7f : length * 0.5f, 1.6f, windup));
        if (mode == 1)
            Warned?.Invoke("빛 3갈래 · 분열! 날아가던 빛이 좌우로 갈라집니다");
        else if (mode == 2)
            Warned?.Invoke("빛 3갈래 · 굴절! 양쪽 빛이 휘어 들어옵니다");
        if (animator != null) animator.SetTrigger(CastHash);
        Vfx.Gather(transform.position + Vector3.up * 1.6f * S, 2.5f, gold, windup, 60f);
        yield return new WaitForSeconds(windup);
        ClearWarning();
        if (animator != null) animator.SetTrigger(SlashHash);
        Vector3 from = transform.position + aim * 1.2f * S;
        GameAudio.Play("hm_orb_fire", transform.position, 1f);
        GameAudio.Play("heim_beam", transform.position, 0.8f);
        foreach (float a in new[] { -spread, 0f, spread })
        {
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * aim;
            LightBolt bolt = LightBolt.Fire(from, dir, 13f, 2.6f, 1.9f, gold, this, playerStats, damage * 0.95f);
            if (mode == 1)
                bolt.SplitAfter(6.5f, 32f);
            else if (mode == 2 && a != 0f)
                bolt.Bend(55f, 38f);
        }
        HeimdallFx.Impact(from + Vector3.up * S, 3f, gold);
        CameraShake.Shake(0.2f, 0.15f);
        yield return new WaitForSeconds(Cast(0.55f));
    }

    // ───────────── 천상의 낙인 ─────────────

    private IEnumerator CelestialBrand()
    {
        int brands = phase == 3 ? 2 : 1;
        for (int b = 0; b < brands && !dead; b++)
        {
            Warned?.Invoke(b == 0 ? "천상의 낙인! 낙인이 따라옵니다 — 터지기 직전에 회피하면 빈틈이 생깁니다" : "두 번째 낙인!");
            GameAudio.Play("hm_brand_mark", player.position, 1f);
            FacePlayer();
            if (animator != null) animator.SetTrigger(CastHash);
            var gold = HeimdallFx.Gold;
            Vfx.Bolt(transform.position + Vector3.up * 2f * S, player.position + Vector3.up, gold, 0.4f, 0.25f);
            const float radius = 2.4f;
            float follow = Cast(phase == 3 ? 2.1f : 2.6f), fixedFor = 0.5f;
            VfxPiece brand = BossSkillFx.Floor("hm_brand", player.position, radius * 2.4f, gold, 0f, 1f, 1f, 70f);
            brand.OpenUp(0.3f);
            warnings.Add(brand.gameObject);
            // a shrinking ring of warning closes on the brand as its time runs out
            float nextTick = 0f;
            Vector3 at = Flat(player.position);
            for (float t = 0f; t < follow && !dead; t += Time.deltaTime)
            {
                at = Vector3.MoveTowards(at, Flat(player.position), 9f * Time.deltaTime);
                float k = t / follow;
                if (brand != null)
                {
                    brand.transform.position = new Vector3(at.x, 0.09f, at.z);
                    brand.Set(gold * Mathf.Lerp(1f, 2.2f, k), radius * 2.4f * Mathf.Lerp(1.15f, 1f, k));
                }
                if (t >= nextTick)
                {
                    nextTick = t + Mathf.Lerp(0.55f, 0.12f, k);
                    GameAudio.Play("hm_brand_tick", at, 0.5f + 0.4f * k);
                    HeimdallFx.ShockRing(at, radius * Mathf.Lerp(1.6f, 1f, k), gold * 0.7f, 0.3f);
                }
                yield return null;
            }
            // fixed in place: a beat to roll clear — at the very last moment
            warnings.Add(Telegraph.Circle(at, radius, fixedFor));
            bool dodged = false;
            for (float t = 0f; t < fixedFor && !dead; t += Time.deltaTime)
            {
                if (brand != null) brand.Set(Color.Lerp(gold, Color.white, t / fixedFor) * 2.6f, radius * 2.4f);
                if (t >= fixedFor - 0.45f && PlayerDodging())
                    dodged = true;
                yield return null;
            }
            ClearWarning();
            // the burst: a pillar of light from the brand
            GameAudio.Play("hm_brand_boom", at, 1f);
            LightPillar.Spawn(at, radius, 12f, gold);
            BossSkillFx.Beam("hm_mark_beam", at, radius * 1.3f, 12f, gold, 0.7f);
            BossSkillFx.Floor("hm_brand", at, radius * 3f, gold * 1.8f, 0.6f, 1f, 1.4f, 120f);
            HeimdallFx.Impact(at + Vector3.up, radius * 1.6f, gold);
            CameraShake.Shake(0.3f, 0.25f);
            bool hit = Flat(player.position - at).magnitude <= radius + 0.35f;
            if (hit)
                Hurt(damage * 1.3f, AwayFrom(at) * 6f);
            else if (dodged)
            {
                // the timing read: he's thrown off balance
                Warned?.Invoke("완벽한 회피! 헤임달이 흔들립니다");
                GameAudio.Play("boss_stagger", transform.position, 1f);
                openUntil = Time.time + 1.6f;
                PlayState(StaggerState, 0.08f);
                HeimdallFx.CircleFlash(transform.position, 4f, new Color(0.7f, 0.85f, 1f), 1.2f);
                yield return new WaitForSeconds(1.6f);
                PlayState(LocomotionState, 0.2f);
                yield break;
            }
            yield return new WaitForSeconds(Cast(0.35f));
        }
    }

    // ───────────── 심판의 사슬 ─────────────

    private IEnumerator ChainsOfJudgment()
    {
        Warned?.Invoke("심판의 사슬! 황금 사슬이 쫓아옵니다 — 붙잡히면 움직일 수 없습니다");
        var gold = HeimdallFx.Gold;
        // a middling distance to throw them from
        if (Flat(player.position - transform.position).magnitude < 5f)
            yield return BlinkTo(Flat(player.position) + (Flat(transform.position) - Flat(player.position)).normalized * 8f);
        FacePlayer();
        PlayState(GroundSlamState, 0.15f);
        yield return new WaitForSeconds(SlamImpact);
        GameAudio.Play("hm_chain_cast", transform.position, 1f);
        Vector3 anchor = Flat(transform.position + transform.forward * 1.1f * S);
        HeimdallFx.Sword(anchor, 1.2f, 0.08f, 2.4f, gold);
        VfxPiece circle = BossSkillFx.Floor("hm_chaincircle", anchor, 7f, gold * 1.3f, 0f, 1f, 1f, 30f);
        circle.OpenUp(0.3f);
        warnings.Add(circle.gameObject);
        Shockwave.Spawn(anchor, 3f, gold, 16);
        CameraShake.Shake(0.3f, 0.25f);

        int count = phase == 3 ? 8 : 6;
        float baseAngle = Vector3.SignedAngle(Vector3.forward, AwayFrom(anchor), Vector3.up);
        var heads = new List<ChainHead>();
        for (int i = 0; i < count; i++)
        {
            float a = baseAngle + (i - (count - 1) / 2f) * (200f / count);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            heads.Add(ChainHead.Launch(anchor, dir, phase == 3 ? 10.5f : 9f, phase == 3 ? 95f : 80f, 2.4f, player, gold));
        }
        // they race until one catches the player or all run out
        ChainHead caught = null;
        for (float t = 0f; t < 2.5f && !dead && caught == null; t += Time.deltaTime)
        {
            foreach (ChainHead h in heads)
                if (h != null && h.Touched) { caught = h; break; }
            yield return null;
        }
        if (caught == null || dead)
        {
            foreach (ChainHead h in heads) if (h != null) h.Retract();
            Destroy(circle.gameObject);
            yield return new WaitForSeconds(Cast(0.4f));
            yield break;
        }

        // caught: held fast, the chain pulled taut, the others snapping round them too
        const float held = 1.25f;
        PlayerEffects.Root(held);
        GameAudio.Play("hm_chain_bind", player.position, 1f);
        Warned?.Invoke("구속! 사슬에 묶였습니다");
        foreach (ChainHead h in heads) if (h != null && h != caught) h.Retract();
        var binds = new List<GoldChain>();
        for (int i = 0; i < 4; i++)
        {
            var c = GoldChain.Make(0.4f, gold);
            binds.Add(c);
            warnings.Add(c.gameObject);
        }
        Vector3 bound = Flat(player.position);
        // he comes down on them: blink beside, the cut marked, the blow
        yield return new WaitForSeconds(0.15f);
        Vector3 side = InArena(bound - AwayFrom(anchor) * 3.4f, 1.2f);
        Blink(transform.position, side, gold);
        Place(side);
        FacePlayer();
        Vector3 dir2 = transform.forward;
        float windup = held - 0.35f;
        warnings.Add(Telegraph.Line(Flat(side) - dir2, dir2, 8f, 3.2f, windup));
        PlayState(GroundSlamState, 0.1f);
        for (float t = 0f; t < windup && !dead; t += Time.deltaTime)
        {
            Vector3 p = player.position + Vector3.up * 0.8f;
            for (int i = 0; i < binds.Count; i++)
            {
                float a = (i * 90f + t * 40f) * Mathf.Deg2Rad;
                binds[i].SetLine(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.4f - Vector3.up * 0.7f, p);
            }
            caught.transform.position = p;
            yield return null;
        }
        ClearWarning();
        caught.Retract();
        BossSkillFx.SlashLine(Flat(side) - dir2, dir2, 8f, 3.2f, gold, 0.45f);
        HeimdallFx.Impact(bound + Vector3.up, 4f, gold);
        Shockwave.Spawn(bound, 3f, gold, 20);
        CameraShake.Shake(0.4f, 0.35f);
        GameAudio.Play("heim_slam", bound, 1f);
        if (DistanceToSegment(Flat(player.position), Flat(side) - dir2, Flat(side) + dir2 * 7f) <= 1.95f)
            Hurt(damage * 1.6f, dir2 * 10f);
        yield return new WaitForSeconds(Cast(0.5f));
    }

    // ───────────── 천상의 십자 ─────────────

    private IEnumerator CelestialCross()
    {
        Warned?.Invoke("천상의 십자! 가로, 세로로 베고 — 두 줄기가 교차해 터집니다");
        var gold = HeimdallFx.Gold;
        // close enough that the cross covers the player
        if (Flat(player.position - transform.position).magnitude > 6.5f)
            yield return BlinkTo(Flat(player.position) + (Flat(transform.position) - Flat(player.position)).normalized * 5f);
        FacePlayer();
        Vector3 center = Flat(player.position);
        Vector3 fwd = transform.forward;
        Vector3 across = Vector3.Cross(Vector3.up, fwd);
        float len = arenaRadius * 1.6f, width = 3f;
        Vector3 hFrom = center - across * len / 2f, vFrom = center - fwd * len / 2f;
        float w1 = Cast(0.8f);
        warnings.Add(Telegraph.Line(hFrom, across, len, width, w1));
        if (animator != null) animator.SetTrigger(SlashHash);
        Vfx.Gather(transform.position + Vector3.up * 1.6f * S, 2.2f, gold, w1, 60f);
        yield return new WaitForSeconds(w1);
        ClearWarning();
        // the horizontal cut (the vertical already marked)
        GameAudio.Play("hm_cross_slash", transform.position, 1f);
        BossSkillFx.SlashLine(hFrom, across, len, width, gold, 0.5f);
        CameraShake.Shake(0.2f, 0.2f);
        if (DistanceToSegment(Flat(player.position), hFrom, hFrom + across * len) <= width / 2f + 0.35f)
            Hurt(damage, fwd * 6f);
        float w2 = Cast(0.42f);
        warnings.Add(Telegraph.Line(vFrom, fwd, len, width, w2));
        yield return new WaitForSeconds(w2);
        ClearWarning();
        if (animator != null) animator.SetTrigger(SlashHash);
        GameAudio.Play("hm_cross_slash", transform.position, 1f);
        BossSkillFx.SlashLine(vFrom, fwd, len, width, gold, 0.5f);
        if (DistanceToSegment(Flat(player.position), vFrom, vFrom + fwd * len) <= width / 2f + 0.35f)
            Hurt(damage, across * 6f);
        // the cross they leave burns brighter, then bursts along both lines
        float w3 = Cast(0.5f);
        warnings.Add(Telegraph.Line(hFrom, across, len, width * 1.15f, w3));
        warnings.Add(Telegraph.Line(vFrom, fwd, len, width * 1.15f, w3));
        warnings.Add(BossSkillFx.Floor("hm_cross", center, 6f, gold * 1.4f, 0f, 1f, 1f, 0f, Quaternion.LookRotation(fwd).eulerAngles.y).gameObject);
        yield return new WaitForSeconds(w3);
        ClearWarning();
        GameAudio.Play("hm_cross_boom", center, 1f);
        BossSkillFx.Floor("hm_cross", center, len, gold * 2f, 0.6f, 0.4f, 1.05f, 0f, Quaternion.LookRotation(fwd).eulerAngles.y + 45f);
        BossSkillFx.SlashLine(hFrom, across, len, width * 1.4f, Color.white, 0.4f);
        BossSkillFx.SlashLine(vFrom, fwd, len, width * 1.4f, Color.white, 0.4f);
        HeimdallFx.Impact(center + Vector3.up, 6f, gold);
        for (int i = -3; i <= 3; i++)
        {
            Vfx.Sprite("hd_burst", center + across * i * 3f + Vector3.up * 0.1f, 3.2f, Color.white * 1.3f, 0.6f, 0.8f, 1.05f, 30f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Sheet(4, 4);
            Vfx.Sprite("hd_burst", center + fwd * i * 3f + Vector3.up * 0.1f, 3.2f, Color.white * 1.3f, 0.6f, 0.8f, 1.05f, 30f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Sheet(4, 4);
        }
        CameraShake.Shake(0.45f, 0.35f);
        Vector3 pp = Flat(player.position);
        if (DistanceToSegment(pp, hFrom, hFrom + across * len) <= width * 0.575f + 0.35f || DistanceToSegment(pp, vFrom, vFrom + fwd * len) <= width * 0.575f + 0.35f)
            Hurt(damage * 1.3f, AwayFrom(center) * 7f);
        yield return new WaitForSeconds(Cast(0.45f));
    }

    // ───────────── 황금의 검진 ─────────────

    private IEnumerator GoldenSwordFormation()
    {
        Warned?.Invoke("황금의 검진! 회전하는 검에 닿지 마세요 — 마지막에 바깥으로 날아갑니다");
        var gold = HeimdallFx.Gold;
        GameAudio.Play("hm_swords_summon", transform.position, 1f);
        if (animator != null) animator.SetTrigger(CastHash);
        int count = phase == 3 ? 8 : 6;
        const float radius = 3.9f;
        Vfx.Gather(transform.position + Vector3.up * 1.6f * S, 3.5f, gold, 0.6f, 70f);
        HeimdallFx.CircleFlash(transform.position, radius * 2.4f, gold, 0.8f);
        BladeWheel wheel = BladeWheel.Make(transform, count, radius, phase == 3 ? 230f : 190f, 2.9f, "GoldenSwordHm", gold, this, playerStats, damage * 0.8f);
        warnings.Add(wheel.gameObject);
        yield return new WaitForSeconds(0.6f);
        // he walks the wheel at the player
        float march = Cast(phase == 3 ? 3.2f : 2.8f), nextHum = 0f;
        for (float t = 0f; t < march && !dead; t += Time.deltaTime)
        {
            Vector3 to = Flat(player.position - transform.position);
            if (to.magnitude > 1.5f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 300f * Time.deltaTime);
                actionMove = to.normalized * footSpeed * 1.55f;
            }
            else actionMove = Vector3.zero;
            if (t >= nextHum) { nextHum = t + 0.7f; GameAudio.Play("hm_swords_spin", transform.position, 0.5f); }
            yield return null;
        }
        actionMove = Vector3.zero;
        if (dead || wheel == null) yield break;
        // the lanes outward are marked, then they all fly
        float mark = Cast(0.55f);
        Vector3 c = Flat(transform.position);
        float startAngle = wheel.Angle;
        for (int i = 0; i < count; i++)
        {
            float a = (startAngle + i * 360f / count) * Mathf.Deg2Rad;
            warnings.Add(Telegraph.Line(c, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), 15f, 1.3f, mark));
        }
        // (the wheel stops turning while they aim)
        var held = new List<(Transform b, Vector3 dir)>();
        List<Transform> blades = wheel.Release();
        warnings.Remove(wheel.gameObject);
        for (int i = 0; i < blades.Count; i++)
        {
            float a = (startAngle + i * 360f / count) * Mathf.Deg2Rad;
            held.Add((blades[i], new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))));
        }
        for (float t = 0f; t < mark; t += Time.deltaTime)
        {
            foreach (var (b, dir) in held)
                if (b != null)
                {
                    b.position = c + dir * radius + Vector3.up * 0.6f;
                    b.rotation = Quaternion.FromToRotation(Vector3.up, -dir);
                }
            yield return null;
        }
        ClearWarning();
        GameAudio.Play("hm_swords_fire", transform.position, 1f);
        foreach (var (b, dir) in held)
        {
            if (b == null) continue;
            Vector3 from = c + dir * radius + Vector3.up * 0.6f;
            FlyingBlade.Fling(b.gameObject, from, InArena(c + dir * 15f, 0.5f) + Vector3.up * 0.6f, 26f, 1.3f, gold, this, playerStats, damage * 1.0f);
        }
        HeimdallFx.Impact(transform.position + Vector3.up * S, 4f, gold);
        CameraShake.Shake(0.3f, 0.25f);
        yield return new WaitForSeconds(Cast(0.6f));
    }

    // ───────────── 빛의 분열 ─────────────

    private IEnumerator SplittingLight()
    {
        Warned?.Invoke("빛의 분열! 구체가 셋으로 갈라진 뒤 당신이 있던 자리로 모입니다");
        var gold = HeimdallFx.Gold;
        int orbs = phase == 3 ? 2 : 1;
        for (int o = 0; o < orbs && !dead; o++)
        {
            FacePlayer();
            if (animator != null) animator.SetTrigger(CastHash);
            float windup = Cast(0.7f);
            Vfx.Gather(transform.position + Vector3.up * 1.8f * S, 3f, gold, windup, 70f);
            GameObject charge = Vfx.Glow(transform, Vector3.up * 2.2f * S + Vector3.forward * 1f, 1.8f, gold * 1.6f, "hm_orb", 120f);
            warnings.Add(charge);
            warnings.Add(Telegraph.Line(transform.position, transform.forward, 7f, 2.6f, windup));
            yield return new WaitForSeconds(windup);
            ClearWarning();
            if (animator != null) animator.SetTrigger(SlashHash);
            GameAudio.Play("hm_orb_fire", transform.position, 1f);
            Vector3 dir = transform.forward;
            if (o == 1) dir = Quaternion.Euler(0f, Random.value < 0.5f ? -25f : 25f, 0f) * dir;
            // it parts about halfway to the player, so the three have room to swing round
            float splitAt = Mathf.Clamp(Flat(player.position - transform.position).magnitude * 0.5f, 3.5f, 6.5f);
            SplitOrb orb = SplitOrb.Fire(transform.position + dir * 1.5f, dir, 9f, splitAt, 2.6f, gold, this, playerStats, damage);
            if (o + 1 < orbs)
                yield return new WaitForSeconds(Cast(0.9f));
            else
                for (float t = 0f; t < 3.5f && orb != null && !orb.Finished; t += Time.deltaTime)
                    yield return null;
        }
        yield return new WaitForSeconds(Cast(0.3f));
    }

    // ───────────── 심판의 포효 ─────────────

    private IEnumerator RoarOfJudgment()
    {
        Warned?.Invoke("심판의 포효! 경기장이 흔들립니다 — 곧 돌진합니다");
        var sonic = new Color(0.8f, 0.92f, 1f);
        var gold = HeimdallFx.Gold;
        if (animator != null) animator.SetTrigger(HornHash);
        float breath = Cast(0.75f);
        Vfx.Gather(transform.position + Vector3.up * 2f * S, 3.5f, sonic, breath, 60f);
        warnings.Add(HeimdallFx.Circle(transform.position, 7f, sonic, 140f).gameObject);
        yield return new WaitForSeconds(breath);
        ClearWarning();
        GameAudio.Play("hm_roar", transform.position, 1f);
        // the whole arena shakes: a short stagger and a dragging step (brief on purpose)
        Vector3 c = Flat(transform.position);
        for (int i = 0; i < 3; i++)
            Vfx.Sprite("hm_quake", new Vector3(c.x, 0.1f, c.z), arenaRadius * 2.4f, sonic * (1.6f - i * 0.4f), 0.8f + i * 0.2f, 0.2f + i * 0.1f, 1.1f, 40f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        HeimdallFx.Impact(transform.position + Vector3.up * 2f * S, 5f, sonic);
        EnemyFeel.Roar(transform, sonic, 7f * S, 0.4f);
        CameraShake.Shake(0.9f, 0.45f);
        PlayerEffects.Root(0.3f);
        PlayerEffects.ApplyBind(0.55f, 1.2f);
        BossSkillFx.Floor("hm_quake", player.position, 3f, sonic * 1.6f, 1.2f, 0.6f, 1.1f, 200f);
        // as it fades he charges — the line is marked while they're still slowed
        float aimTime = Cast(0.9f);
        for (float t = 0f; t < aimTime && !dead; t += Time.deltaTime)
        {
            FacePlayer();
            yield return null;
        }
        Vector3 from = Flat(transform.position), dir = transform.forward;
        float length = Mathf.Clamp(Flat(player.position - transform.position).magnitude + 6f, 8f, arenaRadius * 1.8f);
        Vector3 to = InArena(from + dir * length, 1.2f);
        length = Flat(to - from).magnitude;
        float mark = Cast(0.42f);
        warnings.Add(Telegraph.Line(from, dir, length, 2.8f, mark));
        yield return new WaitForSeconds(mark);
        ClearWarning();
        PlayState(RushState, 0.05f);
        GameAudio.Play("heim_bifrost", transform.position, 0.8f);
        Blink(transform.position, transform.position, gold);
        var streak = new GameObject("RoarStreak");
        streak.transform.SetParent(transform, false);
        streak.transform.localPosition = Vector3.up;
        Vfx.Trail(streak, gold, 1.5f, 0.35f);
        controller.enabled = false;
        dashing = true;
        float y = transform.position.y, time = Mathf.Max(0.2f, length / 30f);
        bool hit = false;
        for (float t = 0f; t < time && !dead; t += Time.deltaTime)
        {
            Vector3 p = Vector3.Lerp(from, to, t / time);
            transform.position = new Vector3(p.x, y, p.z);
            if (!hit && DistanceToSegment(Flat(player.position), from, p) <= 1.75f)
            {
                hit = true;
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                Hurt(damage * 1.25f, side * Mathf.Sign(Vector3.Dot(Flat(player.position) - p, side)) * 9f + dir * 4f);
            }
            yield return null;
        }
        transform.position = new Vector3(to.x, y, to.z);
        controller.enabled = true;
        dashing = false;
        Destroy(streak, 0.4f);
        PlayState(LocomotionState, 0.12f);
        HeimdallFx.Impact(transform.position + Vector3.up, 3f, gold);
        CameraShake.Shake(0.2f, 0.2f);
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // ───────────── 라그나로크 ─────────────

    private IEnumerator Ragnarok()
    {
        GameAudio.Play("boss_warn");
        Warned?.Invoke("라그나로크! 바깥에서 빛의 장벽이 밀려옵니다 — 푸른 안전지대로 피하세요");
        var gold = HeimdallFx.Gold;
        var safeColor = new Color(0.6f, 0.9f, 1f);
        yield return BlinkTo(ArenaCenter);
        Vector3 c = Flat(ArenaCenter);
        FacePlayer();
        invulnerableUntil = Time.time + 1.2f;
        if (animator != null) { animator.SetTrigger(HornHash); animator.SetBool(ChargingHash, true); }
        GameAudio.Play("hm_ragna_horn", transform.position, 1f);
        WorldDim.Run(0.6f, 0.8f, 9f, 1f);
        // the arena wakes: golden swords rise round the rim, the floor's rune circle lights up
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            HeimdallFx.Sword(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (arenaRadius - 0.5f), 1.3f, 0.3f + i * 0.06f, 9f, gold);
        }
        VfxPiece floor = HeimdallFx.Circle(c, arenaRadius * 2.1f, gold * 0.32f, 20f);
        warnings.Add(floor.gameObject);
        GameObject eye = Vfx.Glow(transform, Vector3.up * 6f, 4.2f, Color.white, "heimdalleye");
        warnings.Add(eye);
        yield return new WaitForSeconds(Cast(1.3f));

        // walls of light closing in, each with two safe gaps (marked blue, lanes from the rim)
        int waves = 3;
        float gapHalf = 16f;
        float turn = Random.Range(0f, 360f);
        for (int w = 0; w < waves && !dead; w++)
        {
            float g0 = turn + w * 67f;
            float[] gaps = { g0, g0 + 180f };
            float mark = Cast(w == 0 ? 1.1f : 0.85f);
            var lanes = new List<GameObject>();
            foreach (float g in gaps)
            {
                Vector3 d = Quaternion.Euler(0f, g, 0f) * Vector3.forward;
                lanes.Add(Telegraph.SafeLane(c + d * 2f, d, arenaRadius, 3.4f, mark + 1.6f));
                // sanctuary sigils down the lane, so the way out reads at a glance
                foreach (float along in new[] { 0.3f, 0.58f, 0.86f })
                    lanes.Add(BossSkillFx.Floor("hm_safe", c + d * (arenaRadius * along), 3.6f, safeColor * 1.5f, 0f, 1f, 1f, 30f).gameObject);
            }
            warnings.AddRange(lanes);
            yield return new WaitForSeconds(mark);
            GameAudio.Play("hm_ragna_wave", c, 1f);
            // the wall: from the rim to the middle
            const float travel = 1.6f;
            float outer = arenaRadius + 2f, inner = 2.5f;
            VfxPiece wall = Vfx.Sprite("hm_ragnawave", new Vector3(c.x, 0.15f, c.z), outer * 2.2f, gold * 1.8f, 0f, 1f, 1f, 25f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
            wall.Hold();
            warnings.Add(wall.gameObject);
            bool hit = false;
            for (float t = 0f; t < travel && !dead; t += Time.deltaTime)
            {
                float r = Mathf.Lerp(outer, inner, t / travel);
                wall.Set(gold * 1.8f, r * 2.2f);
                if (Random.value < 0.25f) CameraShake.Shake(0.1f, 0.12f);
                Vector3 rel = Flat(player.position) - c;
                float pr = rel.magnitude;
                if (!hit && Mathf.Abs(pr - r) < 1.2f)
                {
                    float ang = Vector3.SignedAngle(Vector3.forward, rel, Vector3.up);
                    bool safe = false;
                    foreach (float g in gaps)
                        if (Mathf.Abs(Mathf.DeltaAngle(ang, g)) <= gapHalf) safe = true;
                    if (!safe)
                    {
                        hit = true;
                        Hurt(damage * 1.15f, -rel.normalized * 6f);
                        HeimdallFx.Impact(player.position + Vector3.up, 2f, gold);
                    }
                }
                yield return null;
            }
            warnings.Remove(wall.gameObject);
            Destroy(wall.gameObject);
            foreach (GameObject l in lanes) { warnings.Remove(l); if (l != null) Destroy(l); }
            HeimdallFx.ShockRing(c, 3f, gold, 0.4f);
            yield return new WaitForSeconds(Cast(0.2f));
        }
        if (dead) yield break;

        // the end: the middle bursts — get out of it
        const float core = 6.5f;
        float last = Cast(1.1f);
        warnings.Add(Telegraph.Circle(c, core, last));
        Vfx.Gather(transform.position + Vector3.up * 3f, 6f, gold, last, 90f);
        yield return new WaitForSeconds(last);
        ClearWarning();
        if (animator != null) animator.SetBool(ChargingHash, false);
        GameAudio.Play("hm_ragna_boom", c, 1f);
        BossCutscene.Flash(0.7f);
        HeimdallFx.Impact(c + Vector3.up * 1.5f, core * 1.8f, gold);
        Vfx.Sprite("hm_cross", new Vector3(c.x, 0.12f, c.z), core * 3.2f, gold * 2f, 0.8f, 0.5f, 1.15f, 0f, Quaternion.Euler(90f, 45f, 0f));
        LightPillar.Spawn(c, core * 0.6f, 16f, gold);
        Shockwave.Spawn(c, core, gold, 32);
        HeimdallFx.ShockRing(c, core * 1.3f, gold, 0.6f);
        CameraShake.Shake(0.8f, 0.5f);
        if (Flat(player.position - c).magnitude <= core + 0.4f)
            Hurt(damage * 1.8f, AwayFrom(c) * 12f);
        WorldDim.Release();
        yield return new WaitForSeconds(Cast(0.7f));
    }

#if UNITY_EDITOR
    // Test hook: starts a pattern by name at once (phase 2 or 3 must be set first with EditorTestPhase)
    public string EditorTestPattern(string name)
    {
        if (player == null) return "no player";
        IEnumerator r = name switch
        {
            "Foresight" => Foresight(),
            "Tri0" => TriLight(ArenaCenter, HeimdallFx.Gold, 0),
            "Tri1" => TriLight(ArenaCenter, HeimdallFx.Gold, 1),
            "Tri2" => TriLight(ArenaCenter, HeimdallFx.Gold, 2),
            "Brand" => CelestialBrand(),
            "Chains" => ChainsOfJudgment(),
            "Cross" => CelestialCross(),
            "Swords" => GoldenSwordFormation(),
            "Orb" => SplittingLight(),
            "Roar" => RoarOfJudgment(),
            "Ragnarok" => Ragnarok(),
            _ => null
        };
        if (r == null) return "unknown " + name;
        testHold = true;
        StopAction();
        busy = false;
        Act(r, true);
        return "started " + name + " (phase " + phase + ")";
    }

    public string EditorTestPhase(int p)
    {
        // phase 1 → 2 plays the real dismount (a few seconds); 2 → 3 skips straight to the last phase
        if (p >= 2 && phase == 1)
        {
            TakeHit(transform.position, lionHealth + 1);
            return "dismounting";
        }
        if (p == 3 && phase == 2)
            EnterPhaseThree();
        StopAction();
        restTimer = 999f;
        return "phase " + phase;
    }

    public void EditorTestResume() { testHold = false; restTimer = 0.5f; }
#endif
}
