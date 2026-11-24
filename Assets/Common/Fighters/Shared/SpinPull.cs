using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 질풍 회전 (right click — 묠니르 and the gauntlets alike): a short dash the way he faces while turning once round, a
// whirl of lightning dragging every foe nearby in to him, then one blow on the whole gathered pack.
// The weapons play their own body (the hammer spun at arm's length, the brawler's spinning backfist); this holds the
// numbers, the drag and the lightning they share.
public static class SpinPull
{
    public const float Duration = 0.42f;    // the dash and the turn
    public const float Distance = 4.2f;     // how far forward
    public const float Radius = 6f;         // how far the lightning reaches out to drag them
    public const float Keep = 1.4f;         // they are packed this close round him, not into him
    public const float Cooldown = 4f;
    public const float Damage = 1.3f;       // the blow at the end
    public const float HitRadius = 3.2f;

    // one frame of the drag: every (non-boss) foe within reach slides toward 'center', faster as the dash goes on.
    // Returns those it touched.
    public static void Drag(Vector3 center, float radius, float step, List<Enemy> touched)
    {
        foreach (Enemy e in Enemy.Alive.ToArray())
        {
            if (e == null || e.IsDead || !e.isActiveAndEnabled || e.IsBoss) continue;
            Vector3 d = center - e.transform.position; d.y = 0f;
            float dist = d.magnitude;
            if (dist > radius) continue;
            if (touched != null && !touched.Contains(e))
            {
                touched.Add(e);
                StormFx.Arc(center + Vector3.up * 1.2f, e.transform.position + Vector3.up, 0.3f);
            }
            if (dist <= Keep) continue;
            Vector3 move = d / dist * Mathf.Min(step, dist - Keep);
            var agent = e.GetComponent<NavMeshAgent>();
            var cc = e.GetComponent<CharacterController>();
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(move);
            else if (cc != null && cc.enabled) cc.Move(move);
            else e.transform.position += move;
        }
    }

    // while dashing: a ring of lightning round him and arcs out to the foes being dragged
    public static void Crackle(Vector3 center, List<Enemy> touched, float k)
    {
        Vector3 c = center + Vector3.up * 1.1f;
        float a = Random.Range(0f, Mathf.PI * 2f);
        Vector3 rim = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(1.2f, 2.4f);
        Vfx.Bolt(c, rim, Random.value < 0.2f ? StormFx.Gold : StormFx.Blue, 0.22f, 0.08f);
        if (touched != null && touched.Count > 0 && Random.value < 0.5f)
        {
            Enemy e = touched[Random.Range(0, touched.Count)];
            if (e != null && !e.IsDead) Vfx.Bolt(c, e.transform.position + Vector3.up, StormFx.Blue, 0.18f, 0.07f);
        }
    }

    // the start: lightning gathers, a ring flares at his feet
    public static void Begin(Vector3 ground, Vector3 forward)
    {
        Vfx.Gather(ground + Vector3.up * 1.1f, Radius * 0.8f, StormFx.Blue, Duration, 120f);
        Vfx.GroundRing(ground, Radius * 0.6f, StormFx.Blue, 0.35f);
        StormFx.Light(ground + Vector3.up * 1.5f, 10f, 4f, Duration + 0.2f);
        Vfx.Dust(ground, 1.2f, 6, new Color(0.3f, 0.28f, 0.3f, 0.5f));
    }

    // the end: the whole pack struck at once
    public static void Finish(Vector3 ground, Vector3 forward)
    {
        StormFx.Impact(ground, HitRadius * 2.4f, 0.4f);
        StormFx.Spread(ground, HitRadius * 2.6f, 0.4f);
        Shockwave.Spawn(ground, HitRadius, StormFx.Blue, 10);
        Vfx.Slash(ground + Vector3.up * 1.1f, forward, HitRadius * 2.1f, StormFx.Blue, 0f, 360f, 0.26f);
        Vfx.Flash(ground + Vector3.up, HitRadius * 1.4f, Color.white, 0.12f);
        Vfx.Burst(ground + Vector3.up, StormFx.Blue, 18, 7f, 0.12f, 0.5f);
        CameraShake.Shake(0.18f, 0.2f);
    }
}
