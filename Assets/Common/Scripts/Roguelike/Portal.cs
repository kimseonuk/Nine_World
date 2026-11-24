using System;
using UnityEngine;

// The Bifrost gate that opens where a realm's boss fell: step in to descend to the next realm.
public class Portal : MonoBehaviour
{
    private Transform ring;
    private Transform player;
    private Action onEnter;
    private bool used;

    public static Portal Create(Vector3 position, Transform playerTransform, Action entered)
    {
        var root = new GameObject("Bifrost");
        root.transform.position = position;
        Material rainbow = Mats.Get("bifrost", new Color(0.6f, 0.8f, 1f), 0.9f, new Color(0.7f, 0.5f, 1.2f));
        Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(2.6f, 0.02f, 2.6f), rainbow);
        GameObject ring = Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 1.6f, 0f), new Vector3(2.2f, 0.05f, 2.2f), rainbow);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // a glow so it reads as a gate, not a stray disc
        var lightGo = new GameObject("BifrostLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.8f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(0.7f, 0.6f, 1f); l.range = 9f; l.intensity = 4f;

        var portal = root.AddComponent<Portal>();
        portal.ring = ring.transform;
        portal.player = playerTransform;
        portal.onEnter = entered;
        GameAudio.Loop(root, "portal_loop", 0.6f, 14f);
        return portal;
    }

    private void Update()
    {
        ring.Rotate(0f, 120f * Time.deltaTime, 0f, Space.Self);
        if (used || player == null || !GameManager.IsRunActive)
            return;

        Vector3 d = player.position - transform.position;
        d.y = 0f;
        if (d.magnitude < 1.1f)
        {
            used = true;
            GameAudio.Play("portal_enter");
            onEnter?.Invoke();
        }
    }

    private GUIStyle labelStyle;

    // Its name over it, so it is plain what it is and that stepping in moves on
    private void OnGUI()
    {
        if (used || player == null || Time.timeScale <= 0f) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 d = player.position - transform.position; d.y = 0f;
        if (d.magnitude > 18f) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 3.6f);
        if (sp.z <= 0f) return;
        labelStyle ??= UiFont.Style(16, TextAnchor.MiddleCenter, FontStyle.Bold);
        var r = new Rect(sp.x - 130f, Screen.height - sp.y - 16f, 260f, 32f);
        UiFont.Fill(r, new Color(0f, 0f, 0f, 0.6f));
        GUI.color = new Color(0.8f, 0.75f, 1f);
        GUI.Label(r, "비프로스트 · 들어가면 다음 세계로", labelStyle);
        GUI.color = Color.white;
    }
}
