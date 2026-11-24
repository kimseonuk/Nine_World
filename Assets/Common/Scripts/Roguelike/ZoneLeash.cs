using System.Collections.Generic;
using UnityEngine;

// Keeps a zone's monster where the player can reach it: on the floor of its zone (never out over the void, past the
// balustrade or beyond the walls) and out of the solid things in it (the fountain, the obelisk, pillars, statues,
// the market's stalls). Flyers and blinkers move by hand and the walkers' dashes ignore the scenery, so every frame
// after they have moved, whatever they ended up inside of pushes them back out.
public class ZoneLeash : MonoBehaviour
{
    private const float FloorMargin = 0.8f;   // how far in from the floor's edge it must stay
    private const float ProbeHeight = 2.2f;   // the probe reaches this high above the floor (flyers hover lower)

    private static readonly Collider[] hits = new Collider[24];
    private static readonly Dictionary<int, bool> ignored = new Dictionary<int, bool>();

    private Enemy enemy;
    private Room room;
    private CharacterController controller;
    private float radius = 0.5f;

    // (Enemy adds it to every monster as it wakes; this only makes sure it knows its zone)
    public static void Attach(Enemy enemy)
    {
        if (enemy == null)
            return;
        var leash = enemy.GetComponent<ZoneLeash>() ?? enemy.gameObject.AddComponent<ZoneLeash>();
        leash.enemy = enemy;
        leash.room = enemy.HomeRoom;
    }

    private void Start()
    {
        enemy ??= GetComponent<Enemy>();
        room ??= enemy != null ? enemy.HomeRoom : null;
        room ??= RoomAt(transform.position);   // split-off motes and summons: the zone they appeared in
        controller = GetComponent<CharacterController>();
        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        if (controller != null)
            radius = controller.radius * scale;
        else if (GetComponent<SphereCollider>() is SphereCollider sphere)
            radius = sphere.radius * scale;
        radius = Mathf.Clamp(radius, 0.35f, 1.4f);
    }

    private void LateUpdate()
    {
        // (guardians and mid-bosses move as scripted)
        if (enemy == null || enemy.IsDead || enemy.IsBoss)
            return;
        if (room == null)
        {
            if (Time.frameCount % 30 != 0 || (room = RoomAt(transform.position)) == null)
                return;
        }
        Vector3 pos = transform.position;
        Vector3 fixedPos = pos;

        // 1) Out of the solid scenery it walked, flew or dashed into
        float floorY = room.transform.position.y;
        Vector3 bottom = new Vector3(fixedPos.x, floorY + 0.35f, fixedPos.z), top = new Vector3(fixedPos.x, floorY + ProbeHeight, fixedPos.z);
        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, hits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i];
            if (Ignore(c, floorY))
                continue;
            Vector3 probe = new Vector3(fixedPos.x, Mathf.Clamp(c.bounds.center.y, bottom.y, top.y), fixedPos.z);
            Vector3 closest = c.ClosestPoint(probe);
            Vector3 away = probe - closest;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist > 0.001f)
            {
                // touching from outside: out to its radius
                if (dist < radius)
                    fixedPos += away / dist * (radius - dist + 0.02f);
            }
            else
            {
                // inside it (or a shape ClosestPoint can't do): out of its bounds the short way
                Bounds b = c.bounds;
                Vector3 d = fixedPos - b.center;
                float outX = b.extents.x + radius - Mathf.Abs(d.x), outZ = b.extents.z + radius - Mathf.Abs(d.z);
                if (outX < outZ)
                    fixedPos.x += (d.x >= 0f ? 1f : -1f) * (outX + 0.02f);
                else
                    fixedPos.z += (d.z >= 0f ? 1f : -1f) * (outZ + 0.02f);
            }
        }

        // 2) Back onto its zone's floor
        if (room.Radius > 0f)
        {
            if (!room.Contains(fixedPos, FloorMargin))
            {
                Vector3 d = fixedPos - room.transform.position;
                Vector2 flat = Vector2.ClampMagnitude(new Vector2(d.x, d.z), room.Radius - FloorMargin - 0.05f);
                fixedPos = room.transform.position + new Vector3(flat.x, d.y, flat.y);
            }
        }
        else
        {
            // (only once it is off the floor: inside it, even right at a seam between two of its parts, it is free)
            Vector3 local = fixedPos - room.transform.position;
            if (!room.OnFloorLocal(new Vector2(local.x, local.z), room.Floor != null ? -0.05f : 0.4f))
                fixedPos = room.ClampToFloor(fixedPos, FloorMargin);
        }

        if ((fixedPos - pos).sqrMagnitude < 0.000001f)
            return;
        if (controller != null && controller.enabled)
        {
            controller.enabled = false;
            transform.position = fixedPos;
            controller.enabled = true;
        }
        else
            transform.position = fixedPos;
    }

    private static Room RoomAt(Vector3 p)
    {
        foreach (Room r in FindObjectsByType<Room>(FindObjectsSortMode.None))
            if (r.isActiveAndEnabled && r.Contains(p, -1f))
                return r;
        return null;
    }

    // Not scenery: the floor itself, triggers, monsters, the player, loose things that move
    private bool Ignore(Collider c, float floorY)
    {
        if (c.transform.IsChildOf(transform))
            return true;
        if (c.bounds.max.y < floorY + 0.3f)
            return true;   // the floor, rugs, low decals
        if (c.bounds.size.x > 9f || c.bounds.size.z > 9f)
            return true;   // the hall's own shell and long edge walls (the floor check keeps it off those)
        int id = c.GetInstanceID();
        if (ignored.TryGetValue(id, out bool skip))
            return skip;
        skip = c.GetComponentInParent<Enemy>() != null
            || c.GetComponentInParent<PlayerStats>() != null
            || c.CompareTag("Player")
            || (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic)
            || c is CharacterController
            || c.GetComponentInParent<Pickup>() != null;
        if (ignored.Count > 4096)
            ignored.Clear();
        ignored[id] = skip;
        return skip;
    }
}
