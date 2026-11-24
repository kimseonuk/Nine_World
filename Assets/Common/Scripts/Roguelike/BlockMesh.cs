using System.Collections.Generic;
using UnityEngine;

// Builds one mesh out of many carved stone blocks laid in rings: each block a curved slab (a sector of a ring) with a
// flat top, bevelled edges sloping down to straight sides, standing from the ground up to its height. The top and
// bevels carry the floor texture laid flat (`uvPerMetre` repeats a metre), so a ring of blocks reads as one paved floor
// cut into stones. Faces are kept separate so each catches the light on its own.
public class BlockMesh
{
    private readonly List<Vector3> verts = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> tris = new List<int>();
    private readonly float uvScale;

    public BlockMesh(float uvPerMetre)
    {
        uvScale = uvPerMetre;
    }

    private static Vector3 P(float r, float a, float y) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
    private static Vector3 Radial(float a) => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    private static Vector3 Tangent(float a) => new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));

    // One quad, wound so that it faces `facing`
    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f)
            (b, d) = (d, b);
        int s = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        uvs.Add(new Vector2(a.x, a.z) * uvScale); uvs.Add(new Vector2(b.x, b.z) * uvScale);
        uvs.Add(new Vector2(c.x, c.z) * uvScale); uvs.Add(new Vector2(d.x, d.z) * uvScale);
        tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
    }

    // A block filling the ring between `inner` and `outer` from angle a0 to a1 (radians), its top at `height`, its edges
    // bevelled by `bevel`; a0..a1 a whole turn makes an unbroken ring, and inner 0 a round cap
    public void Sector(float inner, float outer, float a0, float a1, float height, float bevel)
    {
        bool cap = inner < 0.01f;
        bool whole = a1 - a0 > Mathf.PI * 2f - 0.01f;
        float c = Mathf.Min(bevel, (outer - inner) * 0.3f, height * 0.6f);
        float ri = cap ? 0f : inner + c, ro = outer - c;
        float inset = whole ? 0f : c / Mathf.Max(0.2f, (inner + outer) / 2f);
        float ta0 = a0 + inset, ta1 = a1 - inset;
        float low = height - c;
        int steps = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) * Mathf.Max(outer, 0.5f) / 0.4f));
        for (int s = 0; s < steps; s++)
        {
            float u0 = s / (float)steps, u1 = (s + 1f) / steps;
            float t0 = Mathf.Lerp(ta0, ta1, u0), t1 = Mathf.Lerp(ta0, ta1, u1);
            float b0 = Mathf.Lerp(a0, a1, u0), b1 = Mathf.Lerp(a0, a1, u1);
            Vector3 radial = Radial((b0 + b1) / 2f);
            // the top
            Quad(P(ri, t0, height), P(ri, t1, height), P(ro, t1, height), P(ro, t0, height), Vector3.up);
            // the outer bevel and side
            Quad(P(ro, t0, height), P(ro, t1, height), P(outer, b1, low), P(outer, b0, low), radial + Vector3.up);
            Quad(P(outer, b0, low), P(outer, b1, low), P(outer, b1, 0f), P(outer, b0, 0f), radial);
            if (!cap)
            {
                // the inner bevel and side
                Quad(P(ri, t0, height), P(ri, t1, height), P(inner, b1, low), P(inner, b0, low), -radial + Vector3.up);
                Quad(P(inner, b0, low), P(inner, b1, low), P(inner, b1, 0f), P(inner, b0, 0f), -radial);
            }
        }
        if (whole || cap)
            return;
        // the two ends: bevel and side
        Vector3 end0 = -Tangent(a0), end1 = Tangent(a1);
        Quad(P(ri, ta0, height), P(ro, ta0, height), P(outer, a0, low), P(inner, a0, low), end0 + Vector3.up);
        Quad(P(inner, a0, low), P(outer, a0, low), P(outer, a0, 0f), P(inner, a0, 0f), end0);
        Quad(P(ri, ta1, height), P(ro, ta1, height), P(outer, a1, low), P(inner, a1, low), end1 + Vector3.up);
        Quad(P(inner, a1, low), P(outer, a1, low), P(outer, a1, 0f), P(inner, a1, 0f), end1);
    }

    public Mesh Build()
    {
        var mesh = new Mesh { name = "StoneBlocks", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }
}
