using UnityEngine;

// Destroys the material instances on this object's renderers when it goes (materials made at runtime for a
// short-lived effect object - a ghost, a glow - are not freed with it otherwise)
public class OwnedMaterials : MonoBehaviour
{
    private void OnDestroy()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
                if (m != null)
                    Destroy(m);
    }
}
