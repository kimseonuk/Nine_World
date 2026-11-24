using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // The guardian's chasing vine: creeps slowly along the ground in a straight line toward where the player stood.
    // Touching it binds the player for a moment. Hitting the vine's head (from the front as a parry, or from the side)
    // cuts it and cancels the attack.
    [RequireComponent(typeof(SphereCollider))]
    public class JOT_Vine : Enemy
    {
        [SerializeField] private float speed = 3f;
        [SerializeField] private float maxLength = 14f;
        [SerializeField] private float touchRadius = 0.8f;
        [SerializeField] private float bindSeconds = 1.5f;
        [SerializeField] private float bindDamage = 6f;

        private static readonly Color VineColor = new Color(0.25f, 0.45f, 0.18f);

        private Vector3 origin, direction;
        private Transform trail;
        private Enemy source;
        private bool done;

        public override int Health => done ? 0 : 1;
        public override int MaxHealth => 1;

        public static JOT_Vine Launch(Vector3 origin, Vector3 target, Enemy source)
        {
            Material mat = Mats.Get("JOT_fx_vine", VineColor, 0.1f);
            var head = new GameObject("JOT_Vine");
            head.transform.position = new Vector3(origin.x, 0.4f, origin.z);
            var col = head.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.6f;
            Mats.Shape(PrimitiveType.Sphere, head.transform, Vector3.zero, new Vector3(0.8f, 0.6f, 0.9f), mat);
            Mats.Shape(PrimitiveType.Cube, head.transform, new Vector3(0.25f, 0.3f, 0.1f), new Vector3(0.3f, 0.08f, 0.2f), mat, false).transform.localRotation = Quaternion.Euler(0f, 30f, 40f);
            Mats.Shape(PrimitiveType.Cube, head.transform, new Vector3(-0.25f, 0.3f, -0.1f), new Vector3(0.3f, 0.08f, 0.2f), mat, false).transform.localRotation = Quaternion.Euler(0f, -30f, -40f);

            var vine = head.AddComponent<JOT_Vine>();
            vine.DisplayName = "넝쿨";
            vine.GoldDrop = 0;
            vine.source = source;
            vine.origin = head.transform.position;
            vine.direction = JOT_Area.Flat(target - origin).normalized;
            head.transform.rotation = Quaternion.LookRotation(vine.direction);
            vine.trail = Mats.Shape(PrimitiveType.Cylinder, null, vine.origin, Vector3.one * 0.3f, mat, false, "JOT_VineTrail").transform;
            return vine;
        }

        private void Update()
        {
            if (done)
                return;
            if (trail == null) // lost its trail (e.g. switched off and on again): just withdraw
            {
                Finish();
                return;
            }
            transform.position += direction * speed * Time.deltaTime;
            float length = Vector3.Distance(origin, transform.position);

            // The trail is a long thin cylinder from the root to the head
            trail.position = (origin + transform.position) / 2f;
            trail.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f);
            trail.localScale = new Vector3(0.3f, length / 2f, 0.3f);

            PlayerStats player = FindAnyObjectByType<PlayerStats>();
            if (player != null && !player.IsDead && FlatDistance(player.transform.position, transform.position) <= touchRadius)
            {
                if (JOT_Parry.TryParry(transform.position, this))
                    return; // parried: the parry cuts the vine

                player.TakeDamage(bindDamage, source);
                JOT_Bind.Apply(player.gameObject, bindSeconds, VineColor);
                Finish();
                return;
            }
            if (length >= maxLength)
                Finish();
        }

        // Any hit on the head cuts the vine
        public override void TakeHit(Vector3 from, int damage)
        {
            if (done)
                return;
            Shockwave.Spawn(transform.position, 1.2f, VineColor, 10);
            Finish();
        }

        public override void Knockdown(float seconds) => Finish();
        public override void Scale(float healthMultiplier, float damageMultiplier) { }

        private void Finish()
        {
            if (done)
                return;
            done = true;
            Alive.Remove(this);
            if (trail != null)
                Destroy(trail.gameObject);
            Destroy(gameObject);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!done && trail != null)
                Destroy(trail.gameObject);
        }
    }

    // Holds the player in place for a moment (vines wrapped around their feet).
    // Stops movement and attacks by switching their input components off, then always switches them back on.
    public class JOT_Bind : MonoBehaviour
    {
        private Behaviour[] held;

        public static void Apply(GameObject player, float seconds, Color color)
        {
            if (player.GetComponentInChildren<JOT_Bind>() != null || JOT_SkillSet.SuperArmor)
                return;
            var go = new GameObject("JOT_Bind");
            go.transform.SetParent(player.transform, false);
            go.transform.localPosition = Vector3.down * 0.9f;
            Material mat = Mats.Get("JOT_fx_vine", color, 0.1f);
            for (int i = 0; i < 3; i++)
                Mats.Shape(PrimitiveType.Cylinder, go.transform, Vector3.up * (0.15f + i * 0.25f), new Vector3(0.9f - i * 0.1f, 0.04f, 0.9f - i * 0.1f), mat);
            var bind = go.AddComponent<JOT_Bind>();
            bind.held = new Behaviour[] { player.GetComponent<PlayerMovement>(), FindAnyObjectByType<ClickToDig>() };
            foreach (Behaviour b in bind.held)
                if (b != null)
                    b.enabled = false;
            Destroy(go, seconds);
        }

        private void OnDestroy()
        {
            if (held == null)
                return;
            foreach (Behaviour b in held)
                if (b != null)
                    b.enabled = true;
        }
    }
}
