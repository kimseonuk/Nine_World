using System;
using System.Collections.Generic;
using UnityEngine;

// The spear-keeper's thrown spear: flies straight, piercing every enemy in its path once,
// and with 귀환의 창 turns round at the end of its flight and comes back to the thrower, striking again.
public class ThrownSpear : MonoBehaviour
{
    private const float Speed = 30f;
    private const float HitRadius = 1.1f;

    private static readonly Color Steel = new Color(0.75f, 0.85f, 1f);

    private Transform thrower;
    private Vector3 direction;
    private float range, travelled;
    private bool returning, recall;
    private Action<Enemy, bool> onHit;   // (enemy, on the way back)
    private readonly HashSet<Enemy> struck = new HashSet<Enemy>();

    public event Action Finished;

    public static ThrownSpear Launch(Transform thrower, Vector3 from, Vector3 direction, float range, bool recall, Action<Enemy, bool> onHit)
    {
        var go = new GameObject("ThrownSpear");
        go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(direction));
        Material shaft = Mats.Get("player_spear_shaft", new Color(0.45f, 0.3f, 0.18f), 0.3f);
        Material tip = Mats.Get("player_spear_tip", Steel, 0.9f, Steel * 1.4f);
        Mats.Shape(PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(0.08f, 0.08f, 2f), shaft);
        Mats.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, 1.1f), new Vector3(0.18f, 0.05f, 0.4f), tip);
        var spear = go.AddComponent<ThrownSpear>();
        spear.thrower = thrower;
        spear.direction = direction;
        spear.range = range;
        spear.recall = recall;
        spear.onHit = onHit;
        return spear;
    }

    private void Update()
    {
        float step = Speed * Time.deltaTime;
        if (!returning)
        {
            transform.position += direction * step;
            travelled += step;
            if (travelled >= range)
            {
                if (!recall || thrower == null)
                {
                    Finish();
                    return;
                }
                returning = true;
                struck.Clear();   // every enemy can be struck again on the way back
            }
        }
        else
        {
            Vector3 to = thrower.position + Vector3.up * 1.1f - transform.position;
            if (to.magnitude <= step + 0.5f)
            {
                Finish();
                return;
            }
            // flies back tail-first, the way a recalled spear would
            transform.position += to.normalized * step;
            transform.rotation = Quaternion.LookRotation(-to);
        }

        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead || struck.Contains(enemy))
                continue;
            Vector3 offset = enemy.transform.position - transform.position;
            offset.y = 0f;
            if (offset.magnitude > HitRadius)
                continue;
            struck.Add(enemy);
            onHit?.Invoke(enemy, returning);
        }
    }

    private void Finish()
    {
        Shockwave.Spawn(new Vector3(transform.position.x, 0f, transform.position.z), 0.8f, Steel, 8);
        Finished?.Invoke();
        Destroy(gameObject);
    }
}
