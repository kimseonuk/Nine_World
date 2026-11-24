using System.Collections;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Something big that has done its damage (a fallen tree, a dropped or thrown giant weapon) stops getting in
    // the way at once (its colliders go off), then sinks into the ground and is removed.
    public class JOT_SinkAway : MonoBehaviour
    {
        public static void Begin(GameObject target, float delay = 0.5f, float duration = 1.2f)
        {
            if (target == null || target.GetComponent<JOT_SinkAway>() != null)
                return;
            foreach (Collider c in target.GetComponentsInChildren<Collider>())
                c.enabled = false;
            target.AddComponent<JOT_SinkAway>().StartCoroutine(Sink(target.transform, delay, duration));
        }

        private static IEnumerator Sink(Transform t, float delay, float duration)
        {
            yield return new WaitForSeconds(delay);
            Vector3 start = t.position;
            Vector3 scale = t.localScale;
            for (float time = 0f; time < duration; time += Time.deltaTime)
            {
                float k = time / duration;
                t.position = start + Vector3.down * (3f * k * k);
                t.localScale = scale * (1f - 0.6f * k);
                yield return null;
            }
            Destroy(t.gameObject);
        }
    }
}
