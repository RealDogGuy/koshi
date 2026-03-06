using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to any persistent GameObject (e.g. Main Camera).
/// Click any ragdoll body part to drag it with the mouse.
/// Uses a TargetJoint2D so forces ramp up gradually and HingeJoint2Ds stay intact.
/// </summary>
public class RagdollDragger : MonoBehaviour
{
    [Header("Drag Settings")]
    [Tooltip("Stiffness of the drag. Higher = snappier feel.")]
    [SerializeField] private float springFrequency = 5f;

    [Tooltip("How quickly oscillation dies out. 1 = critically damped (no wobble).")]
    [SerializeField] private float dampingRatio = 0.7f;

    [Tooltip("Hard cap on force per frame. Lower this if joints break; raise it if drag feels weak.")]
    [SerializeField] private float maxForce = 500f;

    private Rigidbody2D _draggedBody;
    private TargetJoint2D _dragJoint;
    private Camera _cam;

    private void Awake()
    {
        _cam = Camera.main;
    }

    private void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
            BeginDrag();
        else if (mouse.leftButton.isPressed && _draggedBody != null)
            UpdateDrag();
        else if (mouse.leftButton.wasReleasedThisFrame)
            EndDrag();
    }

    private Vector2 MouseWorldPos()
    {
        Vector3 screen = Mouse.current.position.ReadValue();
        screen.z = Mathf.Abs(_cam.transform.position.z);
        return _cam.ScreenToWorldPoint(screen);
    }

    private void BeginDrag()
    {
        Vector2 mousePos = MouseWorldPos();

        Collider2D hit = Physics2D.OverlapPoint(mousePos);
        if (hit == null) return;

        _draggedBody = hit.attachedRigidbody;
        if (_draggedBody == null) return;

        // TargetJoint2D pulls the body toward a world-space target — no anchor GameObject needed.
        _dragJoint = _draggedBody.gameObject.AddComponent<TargetJoint2D>();
        _dragJoint.frequency = springFrequency;
        _dragJoint.dampingRatio = dampingRatio;
        _dragJoint.maxForce = maxForce;

        // Anchor to the exact local-space point that was clicked so the grab feels precise.
        _dragJoint.anchor = _draggedBody.transform.InverseTransformPoint(mousePos);
        _dragJoint.target = mousePos;
    }

    private void UpdateDrag()
    {
        _dragJoint.target = MouseWorldPos();
    }

    private void EndDrag()
    {
        if (_dragJoint != null)
        {
            Destroy(_dragJoint);
            _dragJoint = null;
        }

        _draggedBody = null;
    }
}
