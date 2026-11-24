using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Concentric bands of ground that erupt one after another, each warned in advance.
    // The gaps between the bands are safe; standing in a band when it erupts hurts.
    // Used by the guardian's ground pound and by a chained giant weapon crashing down.
    public class JOT_RingWaves : MonoBehaviour
    {
        private readonly List<GameObject> warnings = new List<GameObject>();

        public static JOT_RingWaves Spawn(Vector3 center, Vector2[] bands, float warnTime, float interval,
            float playerDamage, int enemyDamage, Enemy source)
        {
            var go = new GameObject("JOT_RingWaves");
            go.transform.position = center;
            var waves = go.AddComponent<JOT_RingWaves>();
            waves.StartCoroutine(waves.Run(center, bands, warnTime, interval, playerDamage, enemyDamage, source));
            return waves;
        }

        private IEnumerator Run(Vector3 center, Vector2[] bands, float warnTime, float interval,
            float playerDamage, int enemyDamage, Enemy source)
        {
            // Each band fills up and is full the moment it erupts, so the timing can be read off the ground
            for (int i = 0; i < bands.Length; i++)
                // (the innermost disc, where the blow itself lands, shows the ground splitting)
                warnings.Add(JOT_Fx.Ring(center, bands[i].x, bands[i].y, warnTime + interval * i, bands[i].x <= 0.05f ? JOT_Fx.CrackDecal : null));

            yield return new WaitForSeconds(warnTime);
            for (int i = 0; i < bands.Length; i++)
            {
                if (warnings[i] != null)
                    Destroy(warnings[i]);
                Vector2 band = bands[i];
                JOT_GroundHeave.Spawn(center, band.x, band.y);
                CameraShake.Shake(0.12f, 0.15f);
                if (playerDamage > 0f)
                    JOT_Area.HurtPlayer(JOT_Area.Ring(center, band.x, band.y), playerDamage, source);
                if (enemyDamage > 0)
                    JOT_Area.HitEnemies(JOT_Area.Ring(center, band.x, band.y), center, enemyDamage, source);
                yield return new WaitForSeconds(interval);
            }
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            foreach (GameObject w in warnings)
                if (w != null)
                    Destroy(w);
        }
    }
}
