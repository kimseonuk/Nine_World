using UnityEngine;

// A path leading out of a zone. Walking off the end of it takes the player to the neighbouring zone,
// unless the zone is still sealed by a fight.
public class ZoneExit : MonoBehaviour
{
    private Room room;
    private Vector2Int side;
    private Transform player;

    public void Init(Room owner, Vector2Int direction, Transform playerTransform)
    {
        room = owner;
        side = direction;
        player = playerTransform;
    }

    private void Update()
    {
        if (player == null || room == null || GameManager.Instance == null || room.IsFighting || !GameManager.IsRunActive || GameManager.Instance.CurrentRoom != room)
            return;

        // Past the zone's edge along this exit's direction, within the path's width
        Vector3 local = player.position - room.transform.position;
        float along = side.x != 0 ? local.x * side.x : local.z * side.y;
        float across = side.x != 0 ? local.z : local.x;
        float edge = (side.x != 0 ? room.Size.x : room.Size.y) / 2f;
        if (along > edge + 0.8f && Mathf.Abs(across) < 3f)
            GameManager.Instance.Travel(room, side);
    }
}
