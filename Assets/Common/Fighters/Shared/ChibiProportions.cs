using UnityEngine;

// Cute, big-headed (about 3 heads tall) proportions for a rigged character: its head bone is scaled up after the
// animation has posed it every frame (the clips would otherwise put it back), so the body keeps its animation and only
// the head grows.
[DefaultExecutionOrder(1000)]
public class ChibiProportions : MonoBehaviour
{
    [SerializeField] private string headBone = "Head";
    [SerializeField, Range(1f, 2.5f)] private float headScale = 1.75f;
    [Tooltip("Optional: hands (gauntlets, fists) a touch bigger too, for a chunkier toy-like look.")]
    [SerializeField, Range(0.5f, 2f)] private float handScale = 1f;

    private Transform head, leftHand, rightHand;

    public float HeadScale => headScale;

    private void Awake() => Find();

    private void Find()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == headBone) head = t;
            else if (t.name == "LeftHand") leftHand = t;
            else if (t.name == "RightHand") rightHand = t;
        }
    }

    private void LateUpdate()
    {
        if (head == null) return;
        head.localScale = Vector3.one * headScale;
        if (handScale != 1f)
        {
            if (leftHand != null) leftHand.localScale = Vector3.one * handScale;
            if (rightHand != null) rightHand.localScale = Vector3.one * handScale;
        }
    }
}
