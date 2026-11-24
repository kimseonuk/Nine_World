using UnityEngine;

// Keeps the camera at a fixed offset from the target, looking down at it.
// Inside a walled room it stops short of the edges, so the view never reaches past the walls.
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 7.5f, -5.5f);
    [SerializeField] private float smoothTime = 0.15f;
    [SerializeField] private float bossZoom = 1.3f;    // how much further back it sits while a guardian is being fought

    private Vector3 velocity;
    private float zoom = 1f;

    private Vector3 Offset => offset * zoom;

    private static bool BossFight
    {
        get
        {
            Room room = GameManager.Instance != null ? GameManager.Instance.CurrentRoom : null;
            return room != null && (room.RoomKind == Room.Kind.Boss || room.IsMidBossFight) && room.IsFighting;
        }
    }
    private Vector3 focus;          // the point the camera looks at (the target, held inside the room)

    // The room the camera is shut in: wall faces on the west/east/north, the open edge on the south
    private bool enclosed;
    private float west, east, north, south, floor, wallTop;

    public void Enclose(Vector3 center, float halfX, float halfZ, float wallSetback, float wallHeight)
    {
        enclosed = true;
        west = center.x - halfX - wallSetback;
        east = center.x + halfX + wallSetback;
        north = center.z + halfZ + wallSetback;
        south = center.z - halfZ - 0.6f;   // just past the open edge's balustrade
        floor = center.y;
        wallTop = center.y + wallHeight;
    }

    public void Release() => enclosed = false;

    private void LateUpdate()
    {
        if (target == null)
            return;

        // Pulls back smoothly for the guardian's fight, so the whole arena and his attacks fit on screen
        zoom = Mathf.MoveTowards(zoom, BossFight ? bossZoom : 1f, Time.deltaTime * 0.4f);
        Vector3 wanted = Limit(target.position);
        focus = Vector3.SmoothDamp(focus, wanted, ref velocity, smoothTime);
        transform.position = focus + Offset;
        transform.LookAt(focus);

        // A boss cutscene takes the camera over, blending in and back out
        Camera cam = GetComponent<Camera>();
        if (cam != null && baseFov <= 0f)
            baseFov = cam.fieldOfView;
        float cut = BossCutscene.CameraWeight;
        if (cut > 0f)
        {
            transform.position = Vector3.Lerp(transform.position, BossCutscene.CameraPosition, cut);
            transform.rotation = Quaternion.Slerp(transform.rotation, BossCutscene.CameraRotation, cut);
        }
        if (cam != null)
            cam.fieldOfView = Mathf.Lerp(baseFov, BossCutscene.CameraFov, cut);
        transform.position += CameraShake.Offset;
    }

    private float baseFov;

    // Jump straight to the target (after a teleport) instead of gliding across the map
    public void Snap()
    {
        if (target == null)
            return;
        zoom = BossFight ? bossZoom : 1f;
        focus = Limit(target.position);
        transform.position = focus + Offset;
        transform.LookAt(focus);
        velocity = Vector3.zero;
    }

    // The nearest look-at point to p from which every edge of the screen lands on the floor or a wall
    // below its top: never over a wall, and never past the open south edge
    private Vector3 Limit(Vector3 p)
    {
        Camera cam = GetComponent<Camera>();
        if (!enclosed || cam == null)
            return p;

        float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * cam.aspect;
        Vector3 offset = Offset;
        Quaternion look = Quaternion.LookRotation(-offset);
        float eye = p.y + offset.y;            // camera height
        float above = eye - wallTop;           // how far the camera rides above the wall tops

        float xMin = float.MinValue, xMax = float.MaxValue, zMin = float.MinValue, zMax = float.MaxValue;
        for (int sx = -1; sx <= 1; sx++)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 d = look * new Vector3(sx * tanH, sy * tanV, 1f);
                if (d.y >= -0.01f)
                    continue;   // a ray at or above the horizon can't be held in (the offset keeps them all below)
                float drop = -d.y;
                // Sides and far wall: where the ray reaches the wall's face it must still be below the top
                if (d.x > 0f) xMax = Mathf.Min(xMax, east - above * d.x / drop - offset.x);
                if (d.x < 0f) xMin = Mathf.Max(xMin, west - above * d.x / drop - offset.x);
                if (d.z > 0f) zMax = Mathf.Min(zMax, north - above * d.z / drop - offset.z);
                // The open south side: where the ray meets the floor must still be inside the room
                zMin = Mathf.Max(zMin, south - d.z * (eye - floor) / drop - offset.z);
            }

        p.x = xMin <= xMax ? Mathf.Clamp(p.x, xMin, xMax) : (xMin + xMax) / 2f;
        p.z = zMin <= zMax ? Mathf.Clamp(p.z, zMin, zMax) : (zMin + zMax) / 2f;
        return p;
    }
}

// Short screen shakes for heavy hits; the follow camera adds the offset
public static class CameraShake
{
    private static float until;
    private static float duration;
    private static float strength;

    public static void Shake(float seconds, float amount)
    {
        // A weaker shake never cuts a stronger one short
        strength = Mathf.Max(amount, Time.time < until ? strength : 0f);
        duration = seconds;
        until = Time.time + seconds;
    }

    // A punch the way a blow went: the view jolts along it and springs back (real time, through a hit-stop)
    private static Vector3 kick;
    private static float kickAt = -10f;

    public static void Kick(Vector3 direction, float amount)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        float t = Time.unscaledTime - kickAt;
        Vector3 left = t < 0.6f ? kick * Mathf.Exp(-t * 14f) : Vector3.zero;
        kick = Vector3.ClampMagnitude(left * 0.5f + direction.normalized * amount, 0.5f);
        kickAt = Time.unscaledTime;
    }

    public static Vector3 Offset
    {
        get
        {
            Vector3 k = Vector3.zero;
            float kt = Time.unscaledTime - kickAt;
            if (kt < 0.6f)
                k = kick * Mathf.Exp(-kt * 14f) * Mathf.Cos(kt * 38f);
            float left = until - Time.time;
            if (left <= 0f)
                return k;
            return k + Random.insideUnitSphere * strength * (left / duration);
        }
    }
}
