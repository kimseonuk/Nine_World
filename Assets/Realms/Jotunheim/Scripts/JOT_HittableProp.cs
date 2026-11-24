using System.Collections;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // A map object the player breaks by hitting it (ancient trees, chains, rune stones).
    // It joins Enemy.Alive only so the player's existing swing can reach it: it never dies as an enemy
    // (no gold, no kill), and Jotunheim's area attacks skip it.
    [RequireComponent(typeof(Collider))]
    public abstract class JOT_HittableProp : Enemy
    {
        [SerializeField] protected Transform visual;
        [SerializeField] protected int hitsToBreak = 3;
        [SerializeField] private Color hitTint = new Color(1f, 0.85f, 0.55f);

        protected int hitsLeft;
        protected bool broken;

        public override int Health => broken ? 0 : hitsLeft;
        public override int MaxHealth => hitsToBreak;
        public bool Broken => broken;

        // Hint shown when the player stands next to it (null when there is nothing to say)
        public abstract string Prompt { get; }

        protected virtual void Awake()
        {
            hitsLeft = hitsToBreak;
            GoldDrop = 0;
            if (visual == null && transform.childCount > 0)
                visual = transform.GetChild(0);
        }

        protected override void OnEnable()
        {
            if (!broken)
                base.OnEnable();
        }

        public override void TakeHit(Vector3 from, int damage)
        {
            if (broken)
                return;
            hitsLeft--;
            StartCoroutine(Shudder());
            OnHit(from);
            if (hitsLeft > 0)
                return;
            broken = true;
            Alive.Remove(this);
            OnBroken(from);
        }

        public override void Knockdown(float seconds) { }
        public override void Scale(float healthMultiplier, float damageMultiplier) { }

        protected virtual void OnHit(Vector3 from) { }
        protected abstract void OnBroken(Vector3 from);

        private IEnumerator Shudder()
        {
            FlashTint(hitTint);
            Vector3 rest = visual != null ? visual.localPosition : Vector3.zero;
            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                if (visual != null)
                    visual.localPosition = rest + Random.insideUnitSphere * 0.06f;
                yield return null;
            }
            if (visual != null)
                visual.localPosition = rest;
            ClearFlash();
        }
    }
}
