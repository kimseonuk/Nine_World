using UnityEngine;

// Brokkr at his anvil: hammering away with a shower of sparks on every blow, turning to wave when
// the player walks up, and watching them while they browse the forge.
public class Blacksmith : MonoBehaviour
{
    private const float GreetRange = 5f;
    [SerializeField, Range(0f, 1f)] private float hammerImpact = 0.42f; // when the hammer meets the anvil, through the clip

    private Animator animator;
    private Transform player;
    private Vector3 anvilTop;
    private Quaternion workRotation;
    private bool greeted;
    private float lastCycle = -1f;
    private float waveUntil;

    public void Init(Transform playerTransform, Vector3 anvilTopWorld)
    {
        player = playerTransform;
        anvilTop = anvilTopWorld;
    }

    private void Start()
    {
        animator = GetComponentInChildren<Animator>();
        workRotation = transform.rotation;
        Play("Hammer");
    }

    private void Update()
    {
        if (animator == null || player == null)
            return;

        Vector3 to = player.position - transform.position;
        to.y = 0f;
        bool near = to.magnitude < GreetRange;

        if (near && !greeted)
        {
            greeted = true;
            waveUntil = Time.time + 2.2f;
            Play("Wave");
        }
        else if (!near && greeted)
        {
            greeted = false;
            Play("Hammer");
        }
        else if (near && Time.time > waveUntil && IsPlaying("Wave"))
            Play("Idle");

        // Face the visitor while they're here; back to the anvil once they leave
        Quaternion want = near && to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to) : workRotation;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 240f * Time.deltaTime);

        // Sparks fly each time the hammer comes down
        if (IsPlaying("Hammer"))
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            float cycle = Mathf.Floor(state.normalizedTime);
            float phase = state.normalizedTime - cycle;
            if (phase >= hammerImpact && cycle != lastCycle)
            {
                lastCycle = cycle;
                Sparks.Burst(anvilTop);
                GameAudio.Play("anvil", anvilTop, 0.45f);
            }
        }
    }

    private void Play(string state)
    {
        if (animator.HasState(0, Animator.StringToHash(state)))
            animator.CrossFade(state, 0.2f);
    }

    private bool IsPlaying(string state) => animator.GetCurrentAnimatorStateInfo(0).IsName(state);
}

// A quick spray of glowing sparks that arc up and fall off the anvil
public static class Sparks
{
    public static void Burst(Vector3 at)
    {
        Vfx.Burst(at, new Color(1f, 0.7f, 0.3f), 14, 3.2f, 0.12f, 2.2f);
        Vfx.Flash(at, 0.6f, new Color(1f, 0.6f, 0.25f), 0.12f);
    }
}
