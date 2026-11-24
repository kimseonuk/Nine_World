using UnityEngine;

// Gold, mead and experience lying on the floor: they hop out, then fly to the player when close.
// Experience doesn't wait to be fetched: it streams to the player wherever they are.
public class Pickup : MonoBehaviour
{
    public enum Kind { Gold, Heal, Xp }

    [SerializeField] private Kind kind;
    [SerializeField] private int amount = 1;
    [SerializeField] private float magnetRange = 3f;

    private static PlayerStats player;
    private Vector3 velocity;
    private float age;
    private float spinPhase;

    public static void SpawnGold(Vector3 position, int total)
    {
        // A few coins rather than one, so drops feel generous
        int coins = Mathf.Clamp(total / 3, 1, 6);
        for (int i = 0; i < coins; i++)
        {
            int share = total / coins + (i < total % coins ? 1 : 0);
            Spawn(Kind.Gold, position, share, Mats.Get("pickup_gold", new Color(1f, 0.78f, 0.2f), 0.8f, new Color(0.5f, 0.35f, 0f)),
                PrimitiveType.Cylinder, new Vector3(0.28f, 0.04f, 0.28f));
        }
    }

    public static void SpawnHeal(Vector3 position, int healAmount)
    {
        Spawn(Kind.Heal, position, healAmount, Mats.Get("pickup_mead", new Color(0.9f, 0.25f, 0.3f), 0.6f, new Color(0.5f, 0.05f, 0.1f)),
            PrimitiveType.Sphere, Vector3.one * 0.3f);
    }

    public static void SpawnXp(Vector3 position, int total)
    {
        int orbs = Mathf.Clamp(total / 4, 1, 6);
        for (int i = 0; i < orbs; i++)
        {
            int share = total / orbs + (i < total % orbs ? 1 : 0);
            Spawn(Kind.Xp, position, share, Mats.Get("pickup_xp", new Color(0.45f, 1f, 0.75f), 0.8f, new Color(0.6f, 2.2f, 1.3f)),
                PrimitiveType.Sphere, Vector3.one * 0.18f);
        }
    }

    private static void Spawn(Kind kind, Vector3 position, int amount, Material mat, PrimitiveType shape, Vector3 size)
    {
        var go = new GameObject(kind == Kind.Gold ? "Gold" : kind == Kind.Xp ? "Xp" : "Mead");
        go.transform.position = position;
        Mats.Shape(shape, go.transform, Vector3.zero, size, mat);
        var pickup = go.AddComponent<Pickup>();
        pickup.kind = kind;
        pickup.amount = amount;
        if (kind == Kind.Xp)
            pickup.magnetRange = float.MaxValue;
        Vector2 spread = Random.insideUnitCircle * 2f;
        pickup.velocity = new Vector3(spread.x, 4f, spread.y);
        pickup.spinPhase = Random.Range(0f, 360f);
    }

    // Leaving an area for the next: whatever still lies on the floor is the player's
    public static void CollectAll()
    {
        if (player == null)
            player = FindAnyObjectByType<PlayerStats>();
        foreach (Pickup p in FindObjectsByType<Pickup>())
        {
            if (player != null) p.Collect();
            else Destroy(p.gameObject);
        }
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (player == null)
            player = FindAnyObjectByType<PlayerStats>();

        Vector3 pos = transform.position;
        Vector3 target = player != null ? player.transform.position : pos;
        float distance = Vector3.Distance(pos, target);

        if (player != null && age > 0.5f && distance < magnetRange && !player.IsDead)
        {
            // Fly to the player, faster the closer it gets
            // (experience from across the zone races in, so none is left behind)
            pos = Vector3.MoveTowards(pos, target, (8f + 12f / Mathf.Max(0.3f, distance) + distance) * Time.deltaTime);
            if (Vector3.Distance(pos, target) < 0.5f)
            {
                Collect();
                return;
            }
        }
        else
        {
            // Hop out, then rest just above the floor
            velocity.y -= 15f * Time.deltaTime;
            pos += velocity * Time.deltaTime;
            if (pos.y < 0.25f)
            {
                pos.y = 0.25f;
                velocity = Vector3.zero;
            }
        }

        transform.position = pos;
        transform.rotation = Quaternion.Euler(kind == Kind.Gold ? 90f : 0f, spinPhase + age * 180f, 0f);
    }

    private void Collect()
    {
        if (kind == Kind.Gold)
        {
            RunState.AddGold(amount);
            GameAudio.Play("coin", null, 1f, 1f + Random.Range(0f, 0.15f));
        }
        else if (kind == Kind.Xp)
        {
            RunState.AddXp(amount);
            GameAudio.Play("xp");
        }
        else
        {
            player.Heal(amount);
            GameAudio.Play("heal_orb");
            if (Random.value < 0.4f) GameAudio.Play("pv_heal", null, 0.8f);
        }
        Destroy(gameObject);
    }
}
