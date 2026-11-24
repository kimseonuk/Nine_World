using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // The team's painted flipbook effects (VARCO sheets in the shared Vfx folder: slash / spin / roar / dash / hit /
    // shadow smoke / flurry / mark), laid over our own procedural skill effects. Only calls the shared code.
    public static class JOT_SheetFx
    {
        private const float Glow = 1.6f;

        // Feet height under a body whose pivot is at its middle (the player: CharacterController centre at the root)
        public static Vector3 Feet(Transform body) => new Vector3(body.position.x, body.position.y - 0.95f, body.position.z);

        // A painted burst of speed lines along a dash, lying on the ground
        public static void Streak(Vector3 from, Vector3 to, Color color, float seconds = 0.4f)
        {
            Vector3 d = to - from; d.y = 0f;
            float length = d.magnitude;
            if (length < 0.2f) return;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            DualBlades.Cut("dashsheet", (from + to) * 0.5f - Vector3.up * 0.45f, length * 1.25f, color * Glow, seconds, 0.9f, 1.05f, 0f,
                Quaternion.Euler(90f, yaw - 90f, 0f), sheet: 4);
        }

        // A ring of curved cuts whirling round a point (a spin)
        public static void Whirl(Vector3 center, float radius, Color color, bool clockwise = true, float seconds = 0.38f)
        {
            DualBlades.Cut("spinsheet", center - Vector3.up * 0.4f, radius * 2.4f, color * Glow, seconds, 0.85f, 1.1f, clockwise ? 220f : -220f,
                Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
        }

        // A painted shockwave across the ground (a roar or a slam)
        public static void Roar(Vector3 feet, float radius, Color color, float seconds = 0.6f)
        {
            DualBlades.Cut("roarsheet", feet + Vector3.up * 0.05f, radius * 2f, color * 1.5f, seconds, 0.5f, 1.15f, 0f,
                Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
        }

        // A burst where a blow lands
        public static void Hit(Vector3 at, float size, Color color)
        {
            Vfx.Sprite("hitsheet", at, size, color * Glow, 0.3f, 0.85f, 1.15f, Random.Range(-60f, 60f)).Sheet(4, 4);
        }

        // A crescent through the swing's plane, in front of the body
        public static void Crescent(Vector3 at, Vector3 forward, float size, Color color, float tilt = 0f, bool flip = false)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            Quaternion plane = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 0f, tilt) * Quaternion.Euler(90f, 0f, 0f)
                * Quaternion.Euler(0f, flip ? 180f : 0f, 0f) * Quaternion.Euler(0f, 0f, -90f);
            DualBlades.Cut("slashsheet", at, size, color * Glow, 0.3f, 0.9f, 1.1f, 0f, plane, sheet: 4);
        }

        // A flurry of painted cuts (many quick blows)
        public static void Flurry(Vector3 at, float size, Color color)
        {
            DualBlades.Cut("flurrysheet", at, size, color * 1.8f, 0.35f, 0.9f, 1.1f, Random.Range(-90f, 90f), sheet: 4);
        }

        // A burning sigil over a target
        public static void Mark(Vector3 at, float size, Color color, float seconds = 0.6f)
        {
            Vfx.Sprite("marksheet", at, size, color * 1.8f, seconds, 0.7f, 1.4f, 0f).Sheet(4, 4);
        }

        // Painted smoke (not additive: reads on bright floors too)
        public static void Smoke(Vector3 at, float size, Color color, float seconds = 0.55f)
        {
            Vfx.Sprite("shadowsmoke", at, size, color, seconds, 0.7f, 1.3f, 0f, additive: false).Sheet(4, 4);
        }

        // A looping painted ring at the feet that stays until destroyed (a buff)
        public static VfxPiece GroundLoop(Vector3 feet, float size, Color color)
        {
            return Vfx.Decal(feet + Vector3.up * 0.04f, size, color * 1.2f, "roarsheet", 0f).Sheet(4, 4);
        }
    }
}
