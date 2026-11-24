using UnityEngine;

// Where the player's blow will land, drawn on the ground: a fan for the blade's reach and arc
// (filling up through a swing's wind-up, flashing when it lands) and, while charging, rings for
// the shockwaves the slam would send out if released now.
public class AttackRangeFx : MonoBehaviour
{
    private const float GroundY = 0.04f;
    private const float FlashTime = 0.22f;

    private Transform owner;
    private MeshRenderer fan, fanEdge, shockRing, plungeRing;

    private float windupStart, windupEnd = -1f;
    private float flashStart = -1f;
    private Color flashColor;
    private float reach = 1.5f;

    private int chargeFrame = -1;
    private Color chargeColor;
    private float shockRadius, plungeRadius;

    public static AttackRangeFx Create(Transform player, float arc)
    {
        var fx = new GameObject("AttackRange").AddComponent<AttackRangeFx>();
        fx.owner = player;
        fx.fan = fx.Part("Fan", AnnulusSector(arc, 0f, 28), 3000);
        fx.fanEdge = fx.Part("FanEdge", AnnulusSector(arc, 0.9f, 28), 3001);
        fx.shockRing = fx.Part("ShockRing", AnnulusSector(360f, 0.95f, 64), 3002);
        fx.plungeRing = fx.Part("PlungeRing", AnnulusSector(360f, 0.95f, 64), 3002);
        return fx;
    }

    // A tap swing: the fan grows to full reach by the moment the blade lands
    public void Windup(float bladeReach, float seconds)
    {
        reach = bladeReach;
        windupStart = Time.time;
        windupEnd = Time.time + seconds;
    }

    // The blade landed: the whole fan lights up briefly
    public void Flash(float bladeReach, Color color)
    {
        reach = bladeReach;
        windupEnd = -1f;
        flashStart = Time.time;
        flashColor = color;
    }

    // Called every frame while charging (a radius of 0 hides that ring)
    public void Charging(float bladeReach, Color color, float shock, float plunge)
    {
        reach = bladeReach;
        chargeFrame = Time.frameCount;
        chargeColor = color;
        shockRadius = shock;
        plungeRadius = plunge;
    }

    private void LateUpdate()
    {
        if (owner == null)
        {
            Destroy(gameObject);
            return;
        }
        transform.SetPositionAndRotation(new Vector3(owner.position.x, GroundY, owner.position.z),
            Quaternion.Euler(0f, owner.eulerAngles.y, 0f));

        bool charging = chargeFrame >= Time.frameCount - 1;
        float flash = flashStart >= 0f ? 1f - (Time.time - flashStart) / FlashTime : 0f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);

        if (flash > 0f)
        {
            Show(fan, reach, flashColor, 0.45f * flash);
            Show(fanEdge, reach, flashColor, 0.9f * flash);
        }
        else if (charging)
        {
            Show(fan, reach, chargeColor, 0.24f + 0.08f * pulse);
            Show(fanEdge, reach, chargeColor, 0.85f);
        }
        else if (Time.time < windupEnd)
        {
            float k = Mathf.InverseLerp(windupStart, windupEnd, Time.time);
            Show(fan, reach * Mathf.Lerp(0.3f, 1f, k), SwingColor, Mathf.Lerp(0.15f, 0.38f, k));
            Show(fanEdge, reach, SwingColor, 0.45f + 0.4f * k);
        }
        else
        {
            fan.enabled = fanEdge.enabled = false;
        }

        Show(shockRing, charging ? shockRadius : 0f, chargeColor, 0.55f + 0.25f * pulse);
        Show(plungeRing, charging ? plungeRadius : 0f, chargeColor, 0.35f + 0.2f * pulse);
    }

    public static readonly Color SwingColor = new Color(1f, 0.92f, 0.7f);

    private static void Show(MeshRenderer part, float radius, Color color, float alpha)
    {
        part.enabled = radius > 0.01f && alpha > 0.01f;
        if (!part.enabled)
            return;
        part.transform.localScale = new Vector3(radius, 1f, radius);
        color.a = alpha;
        part.sharedMaterial.color = color;
    }

    private MeshRenderer Part(string partName, Mesh mesh, int queue)
    {
        var go = new GameObject(partName);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        // Unlit, see-through and double-sided: just a tint on the ground
        renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { renderQueue = queue };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        return renderer;
    }

    // A flat band between radius inner and 1, spanning arc degrees centred on +Z (inner 0 = a filled fan)
    private static Mesh AnnulusSector(float arc, float inner, int segments)
    {
        var vertices = new Vector3[(segments + 1) * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Deg2Rad * (-arc / 2f + arc * i / segments);
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            vertices[i * 2] = dir * inner;
            vertices[i * 2 + 1] = dir;
            if (i == segments)
                break;
            int v = i * 2, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 3;
            triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
        }
        var mesh = new Mesh { vertices = vertices, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }
}
