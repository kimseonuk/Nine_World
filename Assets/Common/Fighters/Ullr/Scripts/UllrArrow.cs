using System;
using System.Collections.Generic;
using UnityEngine;

// 울르의 활's arrow: a dark shaft with an ice-blue head and white fletching, flying straight and fast with a frost trail.
// It strikes the first enemy it meets (or, piercing, every enemy on its line once) and stops at walls and at the end of
// its range. Made of plain shapes so any number can fly at once.
public class UllrArrow : MonoBehaviour
{
    public static readonly Color Frost = new Color(0.5f, 1f, 0.74f);   // (wind-green now, as the bow is)

    private Vector3 direction;
    private float speed, range, travelled, radius;
    private bool pierce;
    private Action<Enemy> onHit;
    private Action<Vector3> onEnd;
    private readonly HashSet<Enemy> struck = new HashSet<Enemy>();
    private bool done;

    // scale: 1 for a plain arrow, larger for the charged and finishing shots
    public static UllrArrow Launch(Vector3 from, Vector3 direction, float range, float speed, float scale, bool pierce, Color glow,
        Action<Enemy> onHit, Action<Vector3> onEnd = null)
    {
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
        GameObject go = Model(scale, glow);
        go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(direction));
        var arrow = go.AddComponent<UllrArrow>();
        arrow.direction = direction;
        arrow.range = range;
        arrow.speed = speed;
        arrow.radius = 0.55f + 0.25f * (scale - 1f);
        arrow.pierce = pierce;
        arrow.onHit = onHit;
        arrow.onEnd = onEnd;
        Vfx.Trail(go, glow * 0.8f, 0.16f * scale, 0.14f);
        Destroy(go, range / speed + 1f);   // (gone whatever happens)
        return arrow;
    }

    // The arrow itself, pointing down +z with its head at the front: shaft, head and fletching
    public static GameObject Model(float scale, Color glow)
    {
        var go = new GameObject("UllrArrow");
        Material shaft = Mats.Get("ullr_arrow_shaft", new Color(0.24f, 0.17f, 0.12f), 0.3f);
        Material head = Mats.Get("ullr_arrow_head_" + ColorUtility.ToHtmlStringRGB(glow), Color.Lerp(glow, Color.white, 0.3f), 0.9f, glow * 1.6f);
        Material feather = Mats.Get("ullr_arrow_feather", new Color(0.92f, 0.94f, 0.97f), 0.2f);
        float s = 0.62f * scale;
        Mats.Shape(PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(0.035f, 0.035f, 1.1f) * s, shaft);
        GameObject tip = Mats.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, 0.6f * s), new Vector3(0.1f, 0.1f, 0.2f) * s, head);
        tip.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        for (int i = 0; i < 3; i++)
        {
            GameObject f = Mats.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, -0.45f * s), new Vector3(0.01f, 0.13f, 0.22f) * s, feather);
            f.transform.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
        }
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    private void Update()
    {
        if (done)
            return;
        float step = speed * Time.deltaTime;
        // a wall in the way ends the flight where it stands (enemies are struck by the check below, not by their colliders)
        if (Physics.Raycast(transform.position, direction, out RaycastHit wall, step, ~0, QueryTriggerInteraction.Ignore)
            && wall.collider.GetComponentInParent<Enemy>() == null && wall.collider.GetComponentInParent<PlayerMovement>() == null
            && !wall.collider.isTrigger && wall.collider.bounds.size.y > 0.6f)
        {
            transform.position = wall.point;
            End(true);
            return;
        }
        transform.position += direction * step;
        travelled += step;

        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead || struck.Contains(enemy))
                continue;
            Collider c = enemy.GetComponent<Collider>();
            Vector3 near = c != null ? c.ClosestPoint(transform.position) : enemy.transform.position;
            Vector3 off = near - transform.position;
            off.y = 0f;
            if (off.magnitude > radius)
                continue;
            struck.Add(enemy);
            onHit?.Invoke(enemy);
            Vfx.Burst(transform.position, Frost, 6, 4f, 0.1f, 0.6f);
            if (!pierce)
            {
                End(false);
                return;
            }
        }
        if (travelled >= range)
            End(true);
    }

    private void End(bool burst)
    {
        done = true;
        if (burst)
            Vfx.Burst(transform.position, Frost, 5, 3f, 0.08f, 0.6f);
        onEnd?.Invoke(transform.position);
        // the trail fades out behind it rather than vanishing with it
        foreach (Renderer r in GetComponentsInChildren<MeshRenderer>())
            r.enabled = false;
        Destroy(gameObject, 0.2f);
    }
}
