using UnityEngine;

// Keeps a character's feet on the floor. Added to the player (PlayerMovement) and every monster (Enemy.OnEnable).
//
// Every model checked has its soles at the bottom of its controller's capsule as the prefab sets it up, but where the
// transform sits differs: a monster's is at its feet, the player's in the middle of its body. So everything here works
// from the soles (the transform plus that original capsule-bottom offset), never from the transform itself.
//
// 1. A CharacterController never quite touches the ground: it rests a little above the floor's collider (some of its
//    skin width; how much depends on how it is driven and how big it is), so the model hovers. Standing on the floor,
//    the real gap between the soles and the visible floor (1.5 cm above the Ground collider's top) is measured every
//    few frames and taken out by moving the capsule within the body.
//
// 2. In the generated halls everything walkable is flat floor at the zone's height (even the stepped daises are meant
//    as obstacles), yet a controller steps up anything under 0.3 m (0.75 m on the giant) and walks up 45° slopes, so
//    the crowd climbed rubble, dais tiers, prop bases and each other. There the step and slope are cut right down, and
//    anyone who still ends up standing on something (landing a leap on it, pushed up by the crowd) is put straight
//    back on the nearest free spot of the floor.
//    Realm scenes with their own terrain (Jotunheim, Muspelheim) and the hub keep the controller's own settings.
[DisallowMultipleComponent]
public class FeetOnFloor : MonoBehaviour
{
    // how far the visible floor (the marble tiles, a hall's painted floor) stands above the floor collider's top
    public const float VisibleFloorAbove = 0.015f;
    // in the generated halls: the highest lip a character may step over, in metres (an exit path's 2 cm edge, the
    // 3 cm rings and ~7 cm gold bands of Nidhogg's lair; rubble stands 21 cm and up, a dais tier 24 cm)
    private const float HallStep = 0.1f;
    private const float HallSlope = 10f;
    // soles this far above the zone's floor while resting on something: standing on a structure (Nidhogg's stepped
    // lair floor tops out at 16 cm)
    private const float OnTopAbove = 0.2f;

    private CharacterController cc;
    private float ownStep, ownSlope;
    private float soleLocal;   // the soles' height in the transform's own units (the prefab's capsule bottom)
    private bool flat;

    public static void Attach(GameObject go)
    {
        if (go != null && go.GetComponent<CharacterController>() != null && go.GetComponent<FeetOnFloor>() == null)
            go.AddComponent<FeetOnFloor>();
    }

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (cc == null) return;
        ownStep = cc.stepOffset;
        ownSlope = cc.slopeLimit;
        soleLocal = cc.center.y - cc.height / 2f;
    }

    private float Scale => Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));

    // Where the soles are in the world (scaled now, so a monster grown after it spawned is measured right)
    private float Soles => transform.position.y + soleLocal * Scale;

    // A zone of a generated realm (Asgard's halls and the like), not a realm scene or the hub
    public static bool InGeneratedHall
    {
        get
        {
            GameManager gm = GameManager.Instance;
            return gm != null && !gm.InHub && !StageHost.Hosted && gm.CurrentRealm != null && gm.CurrentRealm.scenePath == null;
        }
    }

    private void Update()
    {
        if (cc == null) return;
        bool want = InGeneratedHall;
        if (want != flat)
        {
            flat = want;
            cc.stepOffset = flat ? Mathf.Min(ownStep, HallStep / Scale) : ownStep;
            cc.slopeLimit = flat ? Mathf.Min(ownSlope, HallSlope) : ownSlope;
        }
    }

    private void LateUpdate()
    {
        if (cc == null || !cc.enabled || !cc.isGrounded) return;
        if (!flat)
        {
            Calibrate();
            return;
        }
        Room room = RoomAt(transform.position);
        float floorY = room != null ? room.transform.position.y : 0f;
        if (Soles - floorY < OnTopAbove)
        {
            Calibrate();
            return;
        }
        // No standing on anything in a generated hall: resting on top of something (a structure, a prop, another
        // character), it is put back down on the nearest free spot of the floor, the same frame
        Vector3 under = new Vector3(transform.position.x, floorY, transform.position.z);
        Vector3 spot = room != null ? room.FreeSpot(under) : under;
        // (the soles on the floor: the transform that far above them; a little high, the controller settles itself)
        spot.y = floorY + VisibleFloorAbove + 0.05f - soleLocal * Scale;
        // (a CharacterController keeps its own copy of the position: move it with the controller off)
        cc.enabled = false;
        transform.position = spot;
        cc.enabled = true;
    }

    // Standing on the floor: the soles brought level with the visible floor. Measured every few frames' rest and
    // corrected by moving the capsule within the body - for as long as the character lives, since some are grown
    // after they spawn (the giants), which changes how high their controller rests.
    private int groundedFrames;

    private void Calibrate()
    {
        if (++groundedFrames < 4) return;
        groundedFrames = 0;
        float soles = Soles;
        Vector3 from = new Vector3(transform.position.x, soles + 0.5f, transform.position.z);
        RaycastHit best = default;
        bool found = false;
        foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore))
            if (!h.collider.transform.IsChildOf(transform) && h.collider.GetComponent<CharacterController>() == null
                && (!found || h.point.y > best.point.y))
            {
                best = h;
                found = true;
            }
        if (!found) return;
        // a hall's Ground slab (Ground, or GroundColliders under a shaped hall) has its painted floor 1.5 cm above its
        // top; any other floor is its own surface
        float target = best.point.y + (best.collider.name.StartsWith("Ground") ? VisibleFloorAbove : 0f);
        float gap = soles - target;
        // (level already, or not standing on the floor here: leave it)
        if (Mathf.Abs(gap) < 0.004f || Mathf.Abs(gap) > 0.25f)
            return;
        // soles too high: the capsule moves up the body (the body settles lower), and the other way round
        cc.center += Vector3.up * (gap / Scale);
    }

    // The zone this point is in: the monster's own, else whichever shown zone holds it
    private Room RoomAt(Vector3 p)
    {
        var enemy = GetComponent<Enemy>();
        if (enemy != null && enemy.HomeRoom != null && enemy.HomeRoom.isActiveAndEnabled)
            return enemy.HomeRoom;
        foreach (Room r in Room.Shown)
            if (r != null && r.isActiveAndEnabled && r.Contains(p, -3f))
                return r;
        return null;
    }
}
