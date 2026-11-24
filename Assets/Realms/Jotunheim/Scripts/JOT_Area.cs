using System;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Area damage shared by Jotunheim's gimmicks and boss attacks.
    public static class JOT_Area
    {
        // Every real enemy (not a breakable prop) whose position passes the test takes the hit.
        // Ordinary enemies can be killed outright; the guardian can be left groggy instead.
        public static void HitEnemies(Func<Vector3, bool> inside, Vector3 from, int damage, Enemy except = null,
            bool killMinions = false, float bossGroggy = 0f)
        {
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy == except || enemy.IsDead || enemy is JOT_HittableProp || enemy is JOT_Vine)
                    continue;
                if (!inside(enemy.transform.position))
                    continue;

                if (killMinions && !enemy.IsBoss)
                {
                    enemy.TakeHit(from, 9999);
                    continue;
                }
                if (damage > 0)
                    enemy.TakeHit(from, damage);
                if (bossGroggy > 0f && enemy.IsBoss && !enemy.IsDead)
                {
                    if (enemy is JOT_LastGuardian guardian)
                        guardian.Groggy(bossGroggy);
                    else
                        enemy.Knockdown(bossGroggy);
                }
            }
        }

        // Attacks with a source (the guardian's pound, its thrown rocks) can be parried facing that source;
        // sourceless hazards (falling trees and weapons, debris) can't
        public static bool HurtPlayer(Func<Vector3, bool> inside, float damage, Enemy source = null)
        {
            PlayerStats player = UnityEngine.Object.FindAnyObjectByType<PlayerStats>();
            if (player == null || player.IsDead || !inside(player.transform.position))
                return false;
            if (source != null && JOT_Parry.TryParry(source.transform.position, source))
                return false;
            player.TakeDamage(damage, source);
            return true;
        }

        public static Func<Vector3, bool> Circle(Vector3 center, float radius) =>
            p => Flat(p - center).magnitude <= radius;

        public static Func<Vector3, bool> Ring(Vector3 center, float inner, float outer) =>
            p => { float d = Flat(p - center).magnitude; return d >= inner && d <= outer; };

        // A pie slice in front of a point (a sweeping blow)
        public static Func<Vector3, bool> Fan(Vector3 origin, Vector3 forward, float radius, float arcDegrees) =>
            p =>
            {
                Vector3 offset = Flat(p - origin);
                return offset.magnitude <= radius && (offset.sqrMagnitude < 0.04f || Vector3.Angle(Flat(forward), offset) <= arcDegrees / 2f);
            };

        // A strip from a point along a direction (a falling tree, a laser)
        public static Func<Vector3, bool> Strip(Vector3 from, Vector3 direction, float length, float width) =>
            p =>
            {
                Vector3 offset = Flat(p - from);
                float along = Vector3.Dot(offset, direction);
                return along >= 0f && along <= length && (offset - direction * along).magnitude <= width / 2f;
            };

        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
