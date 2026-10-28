using UnityEngine;
using UnityEngine.InputSystem;

// Cámara en tercera persona:
//   - orbita alrededor del personaje con el mouse o el stick derecho del joystick;
//   - lo sigue con un retraso suave (no va pegada al cuerpo);
//   - se acerca sola si hay una pared atrás o al costado (y vuelve a alejarse despacio);
//   - con lock-on (ruedita / R3) gira sola para encuadrar al enemigo trabado;
//   - opcional: corrida sobre el hombro, leve movimiento de "cámara en mano" y balanceo al caminar.
//
// Hay dos estilos listos: clic derecho sobre el componente (o en los tres puntitos) >
// "Preset: Silent Hill 2" o "Preset: Souls".
public class ThirdPersonCamera : MonoBehaviour {

    [Header("Objetivo")]
    public Transform target;                                // CameraTarget del jugador (a la altura de la cabeza)
    public Vector3 offset = new Vector3(0f, -0.15f, 0f);    // corrimiento del punto que se orbita
    [Tooltip("Corrimiento hacia un costado (+ = hombro derecho). 0 = centrada.")]
    public float shoulderOffset = 0.55f;

    [Header("Distancia")]
    public float distance = 2.1f;
    public float minDistance = 0.4f;
    [Tooltip("Radio de la esfera que se usa para que la cámara no atraviese paredes.")]
    public float collisionRadius = 0.2f;
    public LayerMask obstacleMask;

    [Header("Lente")]
    [Tooltip("Campo de visión vertical en grados. Más bajo = más cerrado y cinematográfico.")]
    public float fieldOfView = 50f;

    [Header("Control")]
    public float sensitivityX = 1.6f;
    public float sensitivityY = 1.2f;
    [Tooltip("Grados por segundo con el stick derecho a fondo.")]
    public float gamepadSensitivity = 140f;
    public float minPitch = -40f;
    public float maxPitch = 50f;
    [Tooltip("Inclinación con la que arranca (positivo = mirando un poco hacia abajo).")]
    public float startPitch = 6f;

    [Header("Suavizado")]
    [Tooltip("Retraso con el que la cámara sigue al personaje.")]
    public float followSmoothTime = 0.16f;
    [Tooltip("Inercia del giro de la cámara (0 = respuesta directa al mouse).")]
    public float rotationSmoothTime = 0.08f;
    [Tooltip("Qué tan rápido se acerca cuando aparece una pared.")]
    public float zoomInSmoothTime = 0.05f;
    [Tooltip("Qué tan rápido se vuelve a alejar cuando la pared ya no está.")]
    public float zoomOutSmoothTime = 0.4f;

    [Header("Cámara en mano")]
    [Tooltip("Cuánto se mueve sola la cámara, como si alguien la sostuviera (grados). 0 = quieta.")]
    public float handheldAmount = 0.3f;
    [Tooltip("Qué tan rápido es ese movimiento.")]
    public float handheldSpeed = 0.45f;
    [Tooltip("Cuánto sube y baja la cámara al caminar (metros). 0 = sin balanceo.")]
    public float walkBobAmount = 0.025f;
    [Tooltip("Pasos por segundo del balanceo a velocidad de caminata.")]
    public float walkBobFrequency = 1.8f;

    [Header("Lock-on")]
    [Tooltip("Qué tan rápido gira para encuadrar al enemigo trabado.")]
    public float lockOnTurnSpeed = 6f;
    [Tooltip("Inclinación de la cámara mientras hay un enemigo trabado.")]
    public float lockOnPitch = 8f;

    private float yaw;
    private float pitch;
    private float smoothYaw;
    private float smoothPitch;
    private float yawVelocity;
    private float pitchVelocity;
    private Vector3 pivot;
    private Vector3 pivotVelocity;
    private float currentDistance;
    private float distanceVelocity;
    private float currentShoulder;
    private float shoulderVelocity;
    private Vector3 lastTargetPosition;
    private float bobPhase;
    private float bobWeight;
    private PlayerController player;
    private UnityEngine.Camera unityCamera;

    void Start() {
        // Arranca detrás del personaje, mirando hacia donde mira él
        yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;
        pitch = startPitch;
        smoothYaw = yaw;
        smoothPitch = pitch;
        currentDistance = distance;
        currentShoulder = shoulderOffset;

        if (target != null) {
            pivot = target.position + offset;
            lastTargetPosition = target.position;
            player = target.GetComponentInParent<PlayerController>();
        }

        unityCamera = GetComponent<UnityEngine.Camera>();
        if (unityCamera != null && fieldOfView > 0f)
            unityCamera.fieldOfView = fieldOfView;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate() {
        if (target == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // --- Hacia dónde quiere mirar ---
        Transform lockTarget = player != null ? player.LockOnTarget : null;
        if (lockTarget != null) {
            // Lock-on: gira sola hacia el enemigo (rápido al principio, suave al final)
            Vector3 toTarget = lockTarget.position - pivot;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f) {
                float targetYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                float t = 1f - Mathf.Exp(-lockOnTurnSpeed * dt);
                yaw = Mathf.LerpAngle(yaw, targetYaw, t);
                pitch = Mathf.Lerp(pitch, lockOnPitch, t);
            }
        }
        else {
            // Mouse
            if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked) {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                yaw += mouseDelta.x * sensitivityX * 0.1f;
                pitch -= mouseDelta.y * sensitivityY * 0.1f;
            }

            // Stick derecho del joystick
            if (Gamepad.current != null) {
                Vector2 stick = Gamepad.current.rightStick.ReadValue();
                yaw += stick.x * gamepadSensitivity * dt;
                pitch -= stick.y * gamepadSensitivity * 0.6f * dt;
            }
        }
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        smoothYaw = Mathf.SmoothDampAngle(smoothYaw, yaw, ref yawVelocity, rotationSmoothTime);
        smoothPitch = Mathf.SmoothDampAngle(smoothPitch, pitch, ref pitchVelocity, rotationSmoothTime);
        Quaternion rotation = Quaternion.Euler(smoothPitch, smoothYaw, 0f);

        // --- Balanceo al caminar: más fuerte cuanto más rápido se mueve el personaje ---
        Vector3 targetDelta = target.position - lastTargetPosition;
        lastTargetPosition = target.position;
        targetDelta.y = 0f;
        float speed = targetDelta.magnitude / dt;
        bobWeight = Mathf.Lerp(bobWeight, Mathf.Clamp01(speed / 2.5f), 1f - Mathf.Exp(-6f * dt));
        bobPhase += dt * walkBobFrequency * Mathf.Max(0.5f, speed / 2.5f) * Mathf.PI * 2f;
        Vector3 bob = Vector3.up * (Mathf.Sin(bobPhase) * walkBobAmount * bobWeight);

        // --- Seguimiento con retraso ---
        pivot = Vector3.SmoothDamp(pivot, target.position + offset, ref pivotVelocity, followSmoothTime);

        // --- Corrimiento al hombro, sin meterse en una pared que esté al costado ---
        Vector3 right = rotation * Vector3.right;
        float wantedShoulder = shoulderOffset;
        if (Mathf.Abs(shoulderOffset) > 0.001f &&
            Physics.SphereCast(pivot, collisionRadius, right * Mathf.Sign(shoulderOffset), out RaycastHit sideHit,
                               Mathf.Abs(shoulderOffset), obstacleMask, QueryTriggerInteraction.Ignore)) {
            wantedShoulder = Mathf.Sign(shoulderOffset) * sideHit.distance;
        }
        float shoulderTime = Mathf.Abs(wantedShoulder) < Mathf.Abs(currentShoulder) ? zoomInSmoothTime : zoomOutSmoothTime;
        currentShoulder = Mathf.SmoothDamp(currentShoulder, wantedShoulder, ref shoulderVelocity, shoulderTime);
        Vector3 orbitCenter = pivot + right * currentShoulder + bob;

        // --- Colisiones: si hay una pared entre el personaje y la cámara, se acerca ---
        Vector3 back = -(rotation * Vector3.forward);
        float wantedDistance = distance;
        if (Physics.SphereCast(orbitCenter, collisionRadius, back, out RaycastHit hit, distance,
                               obstacleMask, QueryTriggerInteraction.Ignore)) {
            wantedDistance = Mathf.Max(hit.distance, minDistance);
        }
        float zoomTime = wantedDistance < currentDistance ? zoomInSmoothTime : zoomOutSmoothTime;
        currentDistance = Mathf.SmoothDamp(currentDistance, wantedDistance, ref distanceVelocity, zoomTime);

        // --- Cámara en mano: un vaivén lento y orgánico (ruido de Perlin), no un temblor ---
        float time = Time.time * handheldSpeed;
        Quaternion handheld = Quaternion.Euler(
            (Mathf.PerlinNoise(time, 0.37f) - 0.5f) * 2f * handheldAmount,
            (Mathf.PerlinNoise(0.71f, time) - 0.5f) * 2f * handheldAmount,
            (Mathf.PerlinNoise(time, time * 0.5f + 3.1f) - 0.5f) * handheldAmount);

        transform.position = orbitCenter + back * currentDistance;
        transform.rotation = rotation * handheld;
    }

    // ---------------------------------------------------------------
    // PRESETS (clic derecho sobre el componente en el Inspector)
    // ---------------------------------------------------------------

    // Sobre el hombro, cerca, pesada y con cámara en mano, como el remake de Silent Hill 2.
    [ContextMenu("Preset: Silent Hill 2")]
    public void PresetSilentHill() {
        offset = new Vector3(0f, -0.15f, 0f);
        shoulderOffset = 0.55f;
        distance = 2.1f;
        minDistance = 0.4f;
        collisionRadius = 0.2f;
        fieldOfView = 50f;
        sensitivityX = 1.6f;
        sensitivityY = 1.2f;
        minPitch = -40f;
        maxPitch = 50f;
        startPitch = 6f;
        followSmoothTime = 0.16f;
        rotationSmoothTime = 0.08f;
        zoomOutSmoothTime = 0.4f;
        handheldAmount = 0.3f;
        handheldSpeed = 0.45f;
        walkBobAmount = 0.025f;
        walkBobFrequency = 1.8f;
        lockOnPitch = 8f;
        ApplyFieldOfView();
    }

    // Centrada, más lejos y más ágil, como Dark Souls.
    [ContextMenu("Preset: Souls")]
    public void PresetSouls() {
        offset = new Vector3(0f, 0.15f, 0f);
        shoulderOffset = 0f;
        distance = 4.2f;
        minDistance = 0.5f;
        collisionRadius = 0.25f;
        fieldOfView = 55f;
        sensitivityX = 2f;
        sensitivityY = 1.5f;
        minPitch = -35f;
        maxPitch = 65f;
        startPitch = 15f;
        followSmoothTime = 0.1f;
        rotationSmoothTime = 0.04f;
        zoomOutSmoothTime = 0.35f;
        handheldAmount = 0f;
        walkBobAmount = 0f;
        lockOnPitch = 12f;
        ApplyFieldOfView();
    }

    private void ApplyFieldOfView() {
        UnityEngine.Camera cam = GetComponent<UnityEngine.Camera>();
        if (cam != null && fieldOfView > 0f)
            cam.fieldOfView = fieldOfView;
    }
}
