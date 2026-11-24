using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Something flung in an arc onto a warned circle: the guardian's rocks, its thrown weapon,
    // and debris shaken loose from above (launched from straight overhead).
    public class JOT_Projectile : MonoBehaviour
    {
        private Vector3 from, to;
        private float flightTime, arcHeight, radius, playerDamage;
        private int enemyDamage;
        private Enemy source;
        private bool stayOnGround;
        private float t;
        private GameObject warning;
        private Vector3 spin;

        public static JOT_Projectile Launch(GameObject body, Vector3 from, Vector3 to, float flightTime, float arcHeight,
            float radius, float playerDamage, int enemyDamage = 0, Enemy source = null, bool stayOnGround = false)
        {
            foreach (Collider c in body.GetComponentsInChildren<Collider>())
                Destroy(c);
            body.transform.position = from;
            var p = body.AddComponent<JOT_Projectile>();
            p.from = from;
            p.to = new Vector3(to.x, 0f, to.z);
            p.flightTime = Mathf.Max(0.1f, flightTime);
            p.arcHeight = arcHeight;
            p.radius = radius;
            p.playerDamage = playerDamage;
            p.enemyDamage = enemyDamage;
            p.source = source;
            p.stayOnGround = stayOnGround;
            p.spin = Random.onUnitSphere * 360f;
            p.warning = JOT_Fx.Circle(p.to, radius, flightTime);
            return p;
        }

        private void Update()
        {
            t += Time.deltaTime / flightTime;
            float k = Mathf.Clamp01(t);
            transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (arcHeight * 4f * k * (1f - k));
            if (!stayOnGround)
                transform.Rotate(spin * Time.deltaTime, Space.World);
            if (k < 1f)
                return;

            if (warning != null)
                Destroy(warning);
            JOT_EarthBurst.Spawn(to, 0f, radius, Mathf.RoundToInt(6 + radius * 4), Mathf.Clamp(radius / 2f, 0.7f, 1.8f));
            CameraShake.Shake(0.15f, 0.15f);
            if (playerDamage > 0f)
                JOT_Area.HurtPlayer(JOT_Area.Circle(to, radius), playerDamage, source);
            if (enemyDamage > 0)
                JOT_Area.HitEnemies(JOT_Area.Circle(to, radius), to, enemyDamage, source);

            enabled = false;
            if (stayOnGround)
                JOT_SinkAway.Begin(gameObject, 1.0f, 1.2f); // a thrown giant weapon lies there a moment, then sinks away
            else
                Destroy(gameObject, 1.2f);
        }

        private void OnDestroy()
        {
            if (warning != null)
                Destroy(warning);
        }
    }
}
