using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // The bright part of a ring warning: it fills the band from its inner edge outward and is full
    // exactly when the band erupts, the same way the shared circle warnings fill up before a rock lands.
    public class JOT_RingFill : MonoBehaviour
    {
        private Mesh mesh;
        private Vector3[] vertices;
        private Vector3[] directions;
        private float inner, outer, seconds, t;

        public void Init(Mesh ringMesh, Vector3[] ringDirections, float innerRadius, float outerRadius, float duration)
        {
            mesh = ringMesh;
            directions = ringDirections;
            vertices = mesh.vertices;
            inner = innerRadius;
            outer = outerRadius;
            seconds = Mathf.Max(0.01f, duration);
            Apply(0f);
        }

        private void Update()
        {
            t += Time.deltaTime;
            Apply(Mathf.Clamp01(t / seconds));
        }

        private void Apply(float k)
        {
            float edge = Mathf.Lerp(inner, outer, k);
            for (int i = 0; i < directions.Length; i++)
            {
                vertices[i * 2] = directions[i] * inner;
                vertices[i * 2 + 1] = directions[i] * edge;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        private void OnDestroy() => Destroy(mesh);
    }
}
