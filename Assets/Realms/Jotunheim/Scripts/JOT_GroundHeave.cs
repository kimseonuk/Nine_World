using System.Collections.Generic;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // The ground itself heaves: the warned ring band cracks into slabs that jolt up, tilt, drop back with a small
    // rebound and sink away, with a thin skirt of dust over the same band. Covers exactly the telegraphed area.
    // Used for the guardian's ground pound rings (and a chained weapon's crash rings).
    public class JOT_GroundHeave : MonoBehaviour
    {
        private const float Thickness = 0.3f;
        private const float RiseTime = 0.3f;      // first jolt up and back down
        private const float ReboundTime = 0.2f;   // the smaller second bump
        private const float SinkStart = 0.75f;
        private const float Lifetime = 1.25f;
        private const float SegmentLength = 1.3f; // rough arc length of one slab
        private const float GapDegrees = 1.2f;    // the cracks between slabs

        private static Material slabMat, dustMat;

        private class Slab { public Transform t; public Vector3 basePos; public Quaternion baseRot; public Vector3 tiltAxis; public float tilt; public float delay; }

        private readonly List<Slab> slabs = new List<Slab>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private Renderer dust;
        private MaterialPropertyBlock block;
        private Color dustColor;
        private float peak;
        private float age;

        public static void Spawn(Vector3 center, float inner, float outer)
        {
            LoadMaterials();
            var root = new GameObject("JOT_GroundHeave");
            root.transform.position = new Vector3(center.x, 0f, center.z);
            var heave = root.AddComponent<JOT_GroundHeave>();
            heave.block = new MaterialPropertyBlock();
            heave.dustColor = dustMat.HasProperty("_BaseColor") ? dustMat.GetColor("_BaseColor") : new Color(0.6f, 0.5f, 0.38f, 0.5f);
            float width = outer - inner;
            heave.peak = Mathf.Clamp(0.16f + width * 0.07f, 0.2f, 0.4f);
            // The slab tops wear the floor's own material so it reads as the ground lifting; the sides show earth.
            // (No floor material to wear — a floor whose collider and look are separate objects: no slabs, they'd read
            // as a dark disc on the floor; the dust still rises.)
            Material top = GroundMaterial(center, (inner + outer) / 2f);
            var slabMats = new[] { top, slabMat };

            // Slabs: the band split around the circle; a full disc (inner 0) splits into pie slices
            float mid = (inner + outer) / 2f;
            int count = top == null ? 0 : Mathf.Max(inner <= 0.01f ? 5 : 8, Mathf.RoundToInt(2f * Mathf.PI * mid / SegmentLength));
            float step = 360f / count;
            float offset = Random.Range(0f, step);
            for (int i = 0; i < count; i++)
            {
                float a0 = offset + i * step + GapDegrees / 2f;
                float a1 = offset + (i + 1) * step - GapDegrees / 2f;
                float am = (a0 + a1) / 2f;
                Vector3 outward = Dir(am);
                Vector3 pivot = outward * (inner <= 0.01f ? outer * 0.5f : mid);
                Quaternion rot = Quaternion.LookRotation(outward);
                Mesh mesh = SectorMesh(inner, outer, a0, a1, pivot, rot);
                heave.meshes.Add(mesh);

                var slab = new GameObject("Slab");
                slab.transform.SetParent(root.transform, false);
                slab.transform.localPosition = pivot;
                slab.transform.localRotation = rot;
                slab.AddComponent<MeshFilter>().sharedMesh = mesh;
                slab.AddComponent<MeshRenderer>().sharedMaterials = slabMats;
                heave.slabs.Add(new Slab
                {
                    t = slab.transform,
                    basePos = pivot,
                    baseRot = rot,
                    tiltAxis = Vector3.right,               // local tangent: the outer edge lifts more than the inner
                    tilt = Random.Range(-7f, -3f) * (inner <= 0.01f ? 0.6f : 1f),
                    delay = Random.Range(0f, 0.05f),
                });
            }

            // Dust: one flat transparent band over the same area that lifts a little and fades
            Mesh dustMesh = SectorMesh(inner, outer, 0f, 360f, Vector3.zero, Quaternion.identity, flatOnly: true);
            heave.meshes.Add(dustMesh);
            var cloud = new GameObject("Dust");
            cloud.transform.SetParent(root.transform, false);
            cloud.transform.localPosition = Vector3.up * 0.05f;
            cloud.AddComponent<MeshFilter>().sharedMesh = dustMesh;
            heave.dust = cloud.AddComponent<MeshRenderer>();
            heave.dust.sharedMaterial = dustMat;
            heave.dust.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void LoadMaterials()
        {
            if (slabMat == null)
                slabMat = Resources.Load<Material>("JOT_EarthDebris") ?? Mats.Get("JOT_fx_debris", new Color(0.42f, 0.36f, 0.3f), 0.1f);
            if (dustMat == null)
                dustMat = Resources.Load<Material>("JOT_EarthDust") ?? Mats.Get("JOT_fx_dust", new Color(0.62f, 0.54f, 0.42f), 0f);
        }

        // The material of the biggest surface under the band (the arena floor), if there is one
        private static Material GroundMaterial(Vector3 center, float radius)
        {
            Vector3 probe = new Vector3(center.x, 5f, center.z) + Vector3.forward * radius;
            Material best = null;
            float bestArea = 100f; // at least 10 x 10 m, so props and rubble don't count
            foreach (RaycastHit hit in Physics.RaycastAll(probe, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
            {
                var r = hit.collider.GetComponent<Renderer>();
                // (a floor's look may sit beside its collider, under the same parent)
                if (r == null && hit.collider.transform.parent != null)
                    foreach (Renderer sib in hit.collider.transform.parent.GetComponentsInChildren<Renderer>())
                        if (sib.bounds.Contains(new Vector3(hit.point.x, sib.bounds.center.y, hit.point.z)) && Mathf.Abs(sib.bounds.max.y - hit.point.y) < 0.3f)
                        {
                            r = sib;
                            break;
                        }
                float area = hit.collider.bounds.size.x * hit.collider.bounds.size.z;
                if (r != null && area > bestArea)
                {
                    best = r.sharedMaterial;
                    bestArea = area;
                }
            }
            return best;
        }

        private static Vector3 Dir(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        // An annular sector from a0 to a1 degrees, top flush at y 0 and Thickness deep, in the slab's local space
        private static Mesh SectorMesh(float inner, float outer, float a0, float a1, Vector3 pivot, Quaternion rot, bool flatOnly = false)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();   // top face
            var sides = new List<int>();  // walls
            Quaternion inv = Quaternion.Inverse(rot);
            Vector3 L(Vector3 p) => inv * (p - pivot);

            int steps = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / 6f));
            Vector3 down = Vector3.down * Thickness;
            for (int s = 0; s < steps; s++)
            {
                Vector3 d0 = Dir(Mathf.Lerp(a0, a1, (float)s / steps)), d1 = Dir(Mathf.Lerp(a0, a1, (float)(s + 1) / steps));
                Vector3 i0 = d0 * inner, i1 = d1 * inner, o0 = d0 * outer, o1 = d1 * outer;
                Quad(verts, normals, tris, L(i0), L(o0), L(o1), L(i1), inv * Vector3.up);
                if (flatOnly)
                    continue;
                Vector3 mo = (d0 + d1).normalized;
                Quad(verts, normals, sides, L(o0), L(o0 + down), L(o1 + down), L(o1), inv * mo);
                if (inner > 0.01f)
                    Quad(verts, normals, sides, L(i0), L(i1), L(i1 + down), L(i0 + down), inv * -mo);
            }
            if (!flatOnly)
            {
                Vector3 e0 = Dir(a0), e1 = Dir(a1);
                Quad(verts, normals, sides, L(e0 * inner), L(e0 * inner + down), L(e0 * outer + down), L(e0 * outer), inv * -Vector3.Cross(Vector3.up, e0));
                Quad(verts, normals, sides, L(e1 * inner), L(e1 * outer), L(e1 * outer + down), L(e1 * inner + down), inv * Vector3.Cross(Vector3.up, e1));
            }

            var mesh = new Mesh { name = "JOT_HeaveSlab" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.subMeshCount = flatOnly ? 1 : 2;
            mesh.SetTriangles(tris, 0);
            if (!flatOnly)
                mesh.SetTriangles(sides, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Adds a flat-shaded quad, wound so it faces along n whatever order the corners came in
        private static void Quad(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++)
                n.Add(normal.normalized);
            bool facing = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f;
            if (facing)
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else
                t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        private void Update()
        {
            age += Time.deltaTime;

            foreach (Slab s in slabs)
            {
                float t = age - s.delay;
                float lift = 0f;
                if (t > 0f && t < RiseTime)
                    lift = Mathf.Sin(Mathf.PI * t / RiseTime);
                else if (t >= RiseTime && t < RiseTime + ReboundTime)
                    lift = 0.22f * Mathf.Sin(Mathf.PI * (t - RiseTime) / ReboundTime);

                float sink = Mathf.Clamp01((age - SinkStart) / (Lifetime - SinkStart));
                s.t.localPosition = s.basePos + Vector3.up * (lift * peak - sink * (Thickness + 0.05f) + 0.02f);
                s.t.localRotation = s.baseRot * Quaternion.AngleAxis(s.tilt * lift, s.tiltAxis);
            }

            float k = Mathf.Clamp01(age / Lifetime);
            dust.transform.localPosition = Vector3.up * (0.05f + k * 0.5f);
            dust.GetPropertyBlock(block);
            Color c = dustColor;
            c.a = dustColor.a * 0.8f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.4f));
            block.SetColor("_BaseColor", c);
            dust.SetPropertyBlock(block);

            if (age >= Lifetime)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            foreach (Mesh m in meshes)
                Destroy(m);
        }
    }
}
