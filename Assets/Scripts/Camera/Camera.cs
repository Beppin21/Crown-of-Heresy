using UnityEngine;
using UnityEngine.InputSystem;

public class Camera : MonoBehaviour {
    
    public Transform target;
    public Vector3 offset = new Vector3(0f, 1.5f, 0f);

    public float distance = 4f;
    public float minDistance = 0.5f;

    public float sensitivityX = 3f;
    public float sensitivityY = 2f;
    public float minPitch = -30f;
    public float maxPitch = 60f;

    public LayerMask obstacleMask;

    private float yaw;
    private float pitch;

    void Start() {
        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate() {
        if (target == null) return;

        // Sistema de movimiento con el mouse
        Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

        yaw += mouseDelta.x * sensitivityX * 0.1f;
        pitch -= mouseDelta.y * sensitivityY * 0.1f;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + offset;

        // Colisiones 
        float finalDistance = distance;
        if (Physics.Raycast(pivot, -(rotation * Vector3.forward), out RaycastHit hit, distance, obstacleMask)) {
            finalDistance = Mathf.Max(hit.distance - 0.2f, minDistance);
        }

        transform.position = pivot - (rotation * Vector3.forward * finalDistance);
        transform.rotation = rotation;
    }
}