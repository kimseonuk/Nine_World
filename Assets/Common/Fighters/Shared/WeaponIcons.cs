using System.Collections.Generic;
using UnityEngine;

// Each weapon's picture (Resources/UI/weapon_*.png, painted in one style — VARCO for all but the greatsword's), for
// the opening's altar choice and the village armory's cards
public static class WeaponIcons
{
    private static readonly Dictionary<CharacterProfile.WeaponKind, Texture2D> cache = new Dictionary<CharacterProfile.WeaponKind, Texture2D>();

    public static Texture2D For(CharacterProfile.WeaponKind kind)
    {
        if (cache.TryGetValue(kind, out Texture2D icon) && icon != null)
            return icon;
        icon = Resources.Load<Texture2D>(Path(kind));
        cache[kind] = icon;
        return icon;
    }

    // The weapon as a 3D model (Resources/Weapons3D/weapon_*, made in VARCO from its picture) for a weapon that is part
    // of its fighter's body and so can't be taken off the model: put into 'into', centred, about a metre and a half long
    public static bool Model(CharacterProfile.WeaponKind kind, Transform into)
    {
        string name = Path(kind).Replace("UI/", "Weapons3D/");
        GameObject prefab = Resources.Load<GameObject>(name);
        if (prefab == null)
            return false;
        GameObject go = Object.Instantiate(prefab, into);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = prefab.transform.rotation;
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (size < 0.001f) return false;
        go.transform.localScale = prefab.transform.localScale * (1.5f / size);
        b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        go.transform.position += into.position - b.center;
        return true;
    }

    private static string Path(CharacterProfile.WeaponKind kind)
    {
        switch (kind)
        {
            case CharacterProfile.WeaponKind.Greatsword: return "UI/weapon_greatsword";
            case CharacterProfile.WeaponKind.DualBlades: return "UI/weapon_dualblades";
            case CharacterProfile.WeaponKind.Spear: return "UI/weapon_spear";
            case CharacterProfile.WeaponKind.Greataxe: return "UI/weapon_greataxe";
            case CharacterProfile.WeaponKind.TwinAxes: return "UI/weapon_twinaxes";
            case CharacterProfile.WeaponKind.Hammer: return "UI/weapon_mjolnir";
            case CharacterProfile.WeaponKind.Gauntlets: return "UI/weapon_gauntlets";
            case CharacterProfile.WeaponKind.Bow: return "UI/weapon_bow";
            default: return "UI/-";
        }
    }
}
