using System.Collections.Generic;
using UnityEngine;

// Keeps the monsters of a generated hall from bunching up. They all head for the player, so within seconds of a wave
// arriving they would pile into one lump on the player's side of the hall. Each frame, any two closer than their
// bodies plus Gap push gently apart (harder the closer they are), so a crowd spreads out round the player in a ring
// and over the floor instead. Walkers are moved through their controller (walls and props still stop them), flyers
// straight, kept over the floor. A boss pushes the others aside but is never pushed itself.
// Added to every monster by Enemy.OnEnable; realm scenes (Jotunheim, Muspelheim) and the hub are left alone.
[DisallowMultipleComponent]
public class CrowdSpacing : MonoBehaviour
{
    public const float Gap = 1.4f;          // the clear space wanted between two monsters' bodies, in metres
    private const float PushSpeed = 3.5f;   // the fastest a monster is eased aside, metres a second

    private static readonly List<CrowdSpacing> all = new List<CrowdSpacing>();

    private Enemy enemy;
    private CharacterController cc;

    public static void Attach(Enemy e)
    {
        if (e != null && e.GetComponent<CrowdSpacing>() == null)
            e.gameObject.AddComponent<CrowdSpacing>();
    }

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
        cc = GetComponent<CharacterController>();
    }

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    // The body's reach across the floor (a controller's radius, scaled; about half a metre for the rest)
    private float Radius => cc != null ? cc.radius * Mathf.Abs(transform.lossyScale.x) : 0.5f;

    private void LateUpdate()
    {
        if (enemy == null || enemy.IsBoss || enemy.IsDead || !GameManager.IsRunActive || !FeetOnFloor.InGeneratedHall)
            return;
        Vector3 me = transform.position;
        float r = Radius;
        Vector3 push = Vector3.zero;
        foreach (CrowdSpacing other in all)
        {
            if (other == this || other.enemy == null || other.enemy.IsDead)
                continue;
            Vector3 d = me - other.transform.position;
            d.y = 0f;
            float want = r + other.Radius + Gap;
            float dist = d.magnitude;
            if (dist >= want)
                continue;
            // (right on top of each other: apart in some direction of its own)
            Vector3 away = dist > 0.01f ? d / dist : Quaternion.Euler(0f, (GetInstanceID() & 359), 0f) * Vector3.forward;
            push += away * ((want - dist) / want);
        }
        if (push.sqrMagnitude < 0.0001f)
            return;
        Vector3 step = Vector3.ClampMagnitude(push * PushSpeed, PushSpeed) * Time.deltaTime;
        if (cc != null && cc.enabled)
            cc.Move(step);
        else
        {
            Vector3 to = me + step;
            if (enemy.HomeRoom != null)
                to = enemy.HomeRoom.ClampToFloor(to, 1f);
            transform.position = to;
        }
    }
}
