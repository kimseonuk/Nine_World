using UnityEngine;

// The lightning kit's look: electric blue with a fifth of golden yellow running through it. The painted pieces are
// VARCO sprites (Resources/Vfx/storm_*: a falling bolt, an arc, a ground impact, a spinning storm ring), drawn
// additively so their black backgrounds vanish; the arcs between targets are crackling line bolts in the same colours.
public static class StormFx
{
    public static readonly Color Blue = new Color(0.4f, 0.72f, 1f);
    public static readonly Color Gold = new Color(1f, 0.82f, 0.35f);

    // A crackling arc from a to b: a wide blue bolt with a thin second strand beside it (gold one time in five)
    public static void Arc(Vector3 a, Vector3 b, float width = 0.45f)
    {
        Vfx.Bolt(a, b, Blue, width, 0.24f);
        Color second = Random.value < 0.2f ? Gold : Color.Lerp(Blue, Color.white, 0.45f);
        Vfx.Bolt(a + Random.insideUnitSphere * 0.15f, b + Random.insideUnitSphere * 0.15f, second, width * 0.45f, 0.18f);
        Vfx.Burst(b, Blue, 8, 5f, 0.12f, 0.4f);
        Vfx.Burst(b, Gold, 3, 4f, 0.1f, 0.4f);
    }

    // A bolt falling from the sky onto 'ground': the painted bolt standing, turned to the camera, over a line bolt
    public static void Strike(Vector3 ground, float height, float width)
    {
        Vector3 toCam = Camera.main != null ? Camera.main.transform.position - ground : Vector3.back;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 0.01f) toCam = Vector3.back;
        Quaternion facing = Quaternion.LookRotation(-toCam.normalized);
        Vfx.Sprite("storm_bolt", ground + Vector3.up * height * 0.5f, height, Color.white * 1.6f, 0.3f, 0.95f, 1.05f, 0f, facing);
        Vfx.Bolt(ground + Vector3.up * height, ground, Blue, width * 0.5f, 0.2f);
        Vfx.Flash(ground + Vector3.up, width * 3f, Blue, 0.18f);
    }

    // A burst of electricity flat on the ground: the painted shockwave, sparks and a flash of light
    public static void Impact(Vector3 ground, float size, float seconds = 0.45f)
    {
        Vfx.Sprite("storm_impact", new Vector3(ground.x, ground.y + 0.07f, ground.z), size, Color.white * 1.5f, seconds, 0.4f, 1.1f,
            Random.Range(-60f, 60f), Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        Vfx.Burst(ground + Vector3.up * 0.3f, Blue, 14, 7f, 0.14f, 0.5f);
        Vfx.Burst(ground + Vector3.up * 0.3f, Gold, 4, 6f, 0.12f, 0.5f);
        Light(ground + Vector3.up * 1.5f, size * 1.6f, 5f, 0.3f);
    }

    // A fork of lightning lashing sideways from a to b: a bright jagged main strand with two thinner ones twisting round
    // it, little branches snapping off the side, and a crackle where it lands
    public static void Fork(Vector3 a, Vector3 b, float width = 0.4f)
    {
        Vfx.Bolt(a, b, Color.Lerp(Blue, Color.white, 0.35f), width, 0.32f);
        Vfx.Bolt(a + Random.insideUnitSphere * 0.25f, b + Random.insideUnitSphere * 0.25f, Blue, width * 0.6f, 0.28f);
        Vfx.Bolt(a + Random.insideUnitSphere * 0.25f, b + Random.insideUnitSphere * 0.25f, Random.value < 0.25f ? Gold : Blue, width * 0.4f, 0.24f);
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, Random.Range(0.25f, 0.8f));
            Vfx.Bolt(p, p + Random.onUnitSphere * Random.Range(0.6f, 1.2f), Blue, width * 0.35f, 0.18f);
        }
        Vfx.Burst(b, Blue, 10, 5f, 0.12f, 0.4f);
        Vfx.Flash(b, 1.8f, Blue, 0.14f);
        Vfx.Flash(Vector3.Lerp(a, b, 0.5f), 1.4f, Blue, 0.12f);
    }

    // A bolt running flat out from a to b, racing along the ground's height: it crackles out in a few hops (each
    // segment a little later than the last) rather than appearing all at once
    public static System.Collections.IEnumerator Run(Vector3 a, Vector3 b, float width = 0.6f, int hops = 4)
    {
        Vector3 prev = a;
        for (int i = 1; i <= hops; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)hops);
            if (i < hops) p += new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.2f, 0.2f), Random.Range(-0.35f, 0.35f));
            Vfx.Bolt(prev, p, Color.Lerp(Blue, Color.white, 0.3f), width, 0.26f);
            Vfx.Bolt(prev + Random.insideUnitSphere * 0.15f, p + Random.insideUnitSphere * 0.15f, Random.value < 0.2f ? Gold : Blue, width * 0.45f, 0.2f);
            Vfx.Burst(p, Blue, 3, 3f, 0.08f, 0.2f);
            prev = p;
            yield return null;
        }
        Vfx.Flash(b, width * 3f, Blue, 0.14f);
        Vfx.Burst(b, Blue, 10, 6f, 0.12f, 0.4f);
        Vfx.Burst(b, Gold, 3, 5f, 0.1f, 0.4f);
        Light(b, 5f, 4f, 0.2f);
    }

    // Lightning crawling outward over the ground from 'ground': the painted web of cracks (VARCO storm_spread) opening
    // up flat, with arcs running out along the floor
    public static void Spread(Vector3 ground, float size, float seconds = 0.5f)
    {
        Vector3 at = new Vector3(ground.x, ground.y + 0.06f, ground.z);
        // (tinted a little blue: the painting's gold runs a bit over the fifth)
        Vfx.Sprite("storm_spread", at, size, new Color(0.8f, 1f, 1.25f) * 1.4f, seconds, 0.25f, 1.15f,
            Random.Range(-30f, 30f), Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        for (int i = 0; i < 5; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 72f + Random.Range(-20f, 20f), 0f) * Vector3.forward;
            Vfx.Bolt(at + Vector3.up * 0.05f, at + dir * size * 0.45f + Vector3.up * 0.05f, i == 0 ? Gold : Blue, 0.18f, 0.22f);
        }
        Vfx.Burst(ground + Vector3.up * 0.2f, Blue, 12, 6f, 0.12f, 0.6f);
        Light(ground + Vector3.up * 1f, size * 1.4f, 4f, 0.25f);
    }

    // A storm turning flat round 'at' that stays until destroyed (the whirlwind's)
    public static VfxPiece Ring(Vector3 at, float size)
    {
        return Vfx.Sprite("storm_ring", at, size, Color.white * 1.4f, 0f, 1f, 1f, -620f, Quaternion.Euler(90f, 0f, 0f)).OpenUp(0.15f);
    }

    // A short burst of electric light
    public static void Light(Vector3 at, float range, float intensity, float seconds)
    {
        var go = new GameObject("StormLight");
        go.transform.position = at;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Random.value < 0.2f ? Gold : Blue;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        Object.Destroy(go, seconds);
    }
}
