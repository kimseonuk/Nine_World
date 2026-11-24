using System.Collections;
using UnityEngine;

// 후긴과 무닌: Odin's giant ravens. They hover out of reach, shoot homing feathers,
// and take a short flight away whenever the player gets close.
public class Raven : Enemy
{
    [SerializeField] private Transform model;
    [SerializeField] private int maxHealth = 5;
    [SerializeField] private float hoverHeight = 1.8f;
    [SerializeField] private float preferredDistance = 7f;
    [SerializeField] private float tooClose = 4.5f;      // closer than this: fly off
    [SerializeField] private float driftSpeed = 1.6f;

    [Header("Short flight")]
    [SerializeField] private float flightDistance = 6f;
    [SerializeField] private float flightTime = 0.6f;
    [SerializeField] private float flightCooldown = 2.2f;

    [Header("Feathers")]
    [SerializeField] private float volleyInterval = 2.4f;
    [SerializeField] private int feathersPerVolley = 2;
    [SerializeField] private float featherDamage = 7f;

    [Header("Getting hit")]
    [SerializeField] private Color hitTint = new Color(1f, 0.45f, 0.45f);

    private Transform player;
    private int health;
    private bool dead;
    private bool flying;
    private float flightTimer;
    private float volleyTimer;
    private float stunnedUntil;
    private float phase;
    private Color featherColor = Color.white;
    private Vector3 knockback;
    private WindSway wings;      // the wings themselves beating (null if the mesh can't be bent)
    private int wingPhase, wingGroup = -1;

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;

    private void Awake()
    {
        if (model == null && transform.childCount > 0)
            model = transform.GetChild(0);
        health = maxHealth;
        phase = Random.Range(0f, 10f);
        volleyTimer = Random.Range(1f, volleyInterval);
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null)
            player = movement.transform;
        // Muninn is the dark one: its feathers match
        featherColor = BaseTint == Color.white ? new Color(0.95f, 0.95f, 1f) : new Color(0.15f, 0.12f, 0.2f);
        if (model != null)
        {
            wingPhase = Random.Range(0, WindSway.FlapGroups);
            wings = WindSway.Add(model.gameObject, WindSway.Kind.Wings, 1f, wingPhase);
            wingGroup = wingPhase;
        }
    }

    public override void Scale(float healthMultiplier, float damageMultiplier)
    {
        maxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * healthMultiplier));
        health = maxHealth;
        featherDamage *= damageMultiplier;
    }

    private void Update()
    {
        if (dead)
            return;
        phase += Time.deltaTime;
        bool stunned = Time.time < stunnedUntil;

        // Hover: bob gently, or lie on the ground while knocked down
        Vector3 pos = transform.position;
        float targetHeight = stunned ? 0.35f : hoverHeight + Mathf.Sin(phase * 2.2f) * 0.2f;
        pos.y = Mathf.MoveTowards(pos.y, targetHeight, (stunned ? 6f : 2f) * Time.deltaTime);

        knockback = Vector3.Lerp(knockback, Vector3.zero, 6f * Time.deltaTime);
        Vector3 move = knockback;
        if (!stunned && !flying && player != null && GameManager.IsRunActive)
        {
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            Face(to);

            flightTimer -= Time.deltaTime;
            volleyTimer -= Time.deltaTime;
            if (distance < tooClose && flightTimer <= 0f)
                StartCoroutine(FlyAway(-to.normalized));
            else
            {
                // Drift to keep a shooting distance, circling a little
                Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * Mathf.Sin(phase * 0.7f);
                Vector3 want = (distance - preferredDistance) * to.normalized * 0.6f + side * 1.5f;
                move += Vector3.ClampMagnitude(want, driftSpeed);
                if (volleyTimer <= 0f)
                {
                    volleyTimer = volleyInterval;
                    StartCoroutine(Volley());
                }
            }
        }
        pos += move * Time.deltaTime;
        transform.position = KeepInZone(pos);
        AnimateWings(stunned);
    }

    private void Face(Vector3 direction)
    {
        if (direction.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 300f * Time.deltaTime);
    }

    // A quick beat of the wings: dart away from the player, then turn back
    private IEnumerator FlyAway(Vector3 away)
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        flying = true;
        flightTimer = flightCooldown;
        Vector3 sideways = Vector3.Cross(Vector3.up, away) * Random.Range(-0.6f, 0.6f);
        Vector3 dir = (away + sideways).normalized;
        for (float t = 0f; t < flightTime && !dead; t += Time.deltaTime)
        {
            Face(dir);
            Vector3 pos = transform.position + dir * (flightDistance / flightTime) * Time.deltaTime;
            pos.y = Mathf.Lerp(pos.y, hoverHeight + 0.8f, 4f * Time.deltaTime);
            transform.position = KeepInZone(pos);
            yield return null;
        }
        flying = false;
    }

    private IEnumerator Volley()
    {
        // a hard beat of the wings as it rears to throw
        EnemyFeel.Coil(transform, 0.3f, 0.12f);
        yield return new WaitForSeconds(0.15f);
        for (int i = 0; i < feathersPerVolley && !dead; i++)
        {
            if (player == null || Time.time < stunnedUntil)
                yield break;
            Vector3 to = player.position - transform.position;
            Vector3 dir = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f) * new Vector3(to.x, 0f, to.z).normalized;
            EnemyFeel.Launch(transform, transform.position + dir * 0.6f, featherColor);
            Projectile.Feather(transform.position + dir * 0.6f, dir, this, featherDamage, featherColor);
            yield return new WaitForSeconds(0.25f);
        }
    }

    // Stay over the zone's walkable area
    private Vector3 KeepInZone(Vector3 pos)
    {
        // Over the floor (not out over a shaped hall's open cosmos, where no blade could reach it)
        return HomeRoom == null ? pos : HomeRoom.ClampToFloor(pos, 1f);
    }

    // The wings beat (faster while darting away, spread still when knocked down), and the body rides each stroke.
    // Without bendable wings the whole model rocks like a wingstroke instead.
    private void AnimateWings(bool stunned)
    {
        if (model == null)
            return;
        if (wings != null)
        {
            int group = stunned ? WindSway.GlideGroup : flying ? WindSway.FastGroups + wingPhase : wingPhase;
            if (group != wingGroup)
            {
                wings.SetGroup(group);
                wingGroup = group;
            }
        }
        // In step with the bent wings (same clock and phase as WindSway's beat), or its own rhythm without them
        float stroke = wings != null
            ? Time.time * (flying ? 17f : 9f) + wingPhase * 2.09f
            : phase * (flying ? 22f : 9f);
        float sway = wings != null ? 0.3f : 1f;
        float roll = stunned ? 70f : Mathf.Sin(stroke) * (flying ? 10f : 5f) * sway;
        float pitch = stunned ? 0f : Mathf.Sin(stroke) * 4f;
        model.localRotation = Quaternion.Euler(pitch, 0f, roll);
        // Each downstroke (wings sweeping down) lifts it a little
        model.localPosition = stunned || wings == null ? Vector3.zero : Vector3.up * -Mathf.Cos(stroke) * 0.08f;
    }

    public override void TakeHit(Vector3 from, int damage)
    {
        if (dead)
            return;
        health -= Mathf.Max(1, damage);
        Vector3 away = transform.position - from;
        away.y = 0f;
        knockback = away.normalized * 4f;
        StartCoroutine(Flash());
        if (health <= 0)
            StartCoroutine(Die());
    }

    // Knocked out of the air: it drops to the ground for a while
    public override void Knockdown(float seconds)
    {
        if (!dead)
            stunnedUntil = Time.time + seconds;
    }

    public override void Interrupt()
    {
        volleyTimer = Mathf.Max(volleyTimer, 1.5f);
    }

    private IEnumerator Flash()
    {
        FlashTint(hitTint);
        yield return new WaitForSeconds(0.12f);
        if (!dead)
            ClearFlash();
    }

    private IEnumerator Die()
    {
        dead = true;
        RaiseDied();
        if (wings != null)
            wings.SetGroup(WindSway.GlideGroup);
        Vector3 start = transform.position;
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            transform.position = new Vector3(start.x, Mathf.Lerp(start.y, 0.2f, t / 0.5f), start.z);
            if (model != null)
                model.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 160f, t / 0.5f));
            yield return null;
        }
        Destroy(gameObject, 0.8f);
    }
}
