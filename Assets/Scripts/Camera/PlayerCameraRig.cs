using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

// Controla la cámara de Cinemachine del jugador (va en el mismo objeto que la CinemachineCamera).
//
// Cinemachine se ocupa de seguir al personaje con suavidad, esquivar paredes y el temblor de
// "cámara en mano". Este script hace dos cosas:
//   1. Gira la cámara con el mouse / stick derecho (y sola hacia el enemigo con lock-on).
//   2. Cambia la "personalidad" de la cámara según la situación, mezclando suave entre estados:
//        Explorar  → sobre el hombro, cerca, cerrada, temblor sutil (Silent Hill 2)
//        Correr    → un poco más lejos, más abierta, más temblor
//        Combate   → espada en mano: más lejos y centrada, para ver alrededor
//        Lock-on   → centrada, girando sola hacia el enemigo
//        Apuntar   → zoom sobre el hombro (Aiming = true; lo usa la lámpara)
[DefaultExecutionOrder(-50)] // antes de que Cinemachine calcule la cámara en el frame
public class PlayerCameraRig : MonoBehaviour
{
    [System.Serializable]
    public struct CameraProfile
    {
        [Tooltip("Distancia de la cámara al personaje (metros).")]
        public float distance;
        [Tooltip("Dónde queda el personaje en pantalla: negativo = a la izquierda (cámara sobre el hombro derecho), 0 = centrado.")]
        public float shoulder;
        [Tooltip("Altura del encuadre: positivo = el personaje queda más abajo en pantalla.")]
        public float height;
        [Tooltip("Campo de visión vertical (grados). Más bajo = más cerrado y cinematográfico.")]
        public float fieldOfView;
        [Tooltip("Intensidad del temblor de cámara en mano (0 = quieta).")]
        public float shake;
        [Tooltip("Velocidad del temblor.")]
        public float shakeSpeed;

        public static CameraProfile Lerp(CameraProfile a, CameraProfile b, float t) => new CameraProfile
        {
            distance = Mathf.Lerp(a.distance, b.distance, t),
            shoulder = Mathf.Lerp(a.shoulder, b.shoulder, t),
            height = Mathf.Lerp(a.height, b.height, t),
            fieldOfView = Mathf.Lerp(a.fieldOfView, b.fieldOfView, t),
            shake = Mathf.Lerp(a.shake, b.shake, t),
            shakeSpeed = Mathf.Lerp(a.shakeSpeed, b.shakeSpeed, t),
        };
    }

    [Header("Estados de la cámara")]
    public CameraProfile explore = new CameraProfile { distance = 2.2f, shoulder = -0.18f, height = 0.04f, fieldOfView = 50f, shake = 1f, shakeSpeed = 0.8f };
    public CameraProfile run = new CameraProfile { distance = 2.9f, shoulder = -0.1f, height = 0.02f, fieldOfView = 57f, shake = 1.6f, shakeSpeed = 1.5f };
    public CameraProfile combat = new CameraProfile { distance = 3.4f, shoulder = -0.08f, height = 0.05f, fieldOfView = 52f, shake = 0.6f, shakeSpeed = 0.8f };
    public CameraProfile lockOn = new CameraProfile { distance = 3.8f, shoulder = 0f, height = 0.08f, fieldOfView = 50f, shake = 0.5f, shakeSpeed = 0.8f };
    public CameraProfile aim = new CameraProfile { distance = 1.4f, shoulder = -0.26f, height = 0.02f, fieldOfView = 42f, shake = 0.5f, shakeSpeed = 0.6f };
    [Tooltip("Segundos que tarda en pasar de un estado a otro.")]
    public float profileBlendTime = 0.45f;

    [Header("Modo lámpara (escena con ZoneSettings en modo lámpara, ej. Town)")]
    [Tooltip("Clic derecho sostenido (o gatillo izquierdo): la cámara se acerca a la lámpara.")]
    public CameraProfile lanternZoom = new CameraProfile { distance = 1.25f, shoulder = -0.22f, height = 0f, fieldOfView = 40f, shake = 0.7f, shakeSpeed = 0.6f };
    [Tooltip("Segundos que tarda el acercamiento a la lámpara (más alto = más lento y dramático).")]
    public float lanternZoomBlendTime = 0.6f;

    [Header("Control")]
    public float mouseSensitivityX = 0.12f;
    public float mouseSensitivityY = 0.09f;
    [Tooltip("Grados por segundo con el stick derecho a fondo.")]
    public float gamepadSensitivity = 150f;
    public bool invertY;
    public Vector2 pitchRange = new Vector2(-35f, 55f);

    [Header("Dinámica")]
    [Tooltip("Cuánto se inclina la cámara al girar rápido (grados). 0 = nunca.")]
    public float turnTilt = 2.5f;
    [Tooltip("Si caminás sin tocar el mouse, la cámara se acomoda sola detrás del personaje.")]
    public bool autoRecenter = true;
    [Tooltip("Segundos sin tocar el mouse antes de acomodarse.")]
    public float recenterDelay = 2.5f;
    [Tooltip("Qué tan rápido se acomoda detrás (más alto = más rápido).")]
    public float recenterSpeed = 1.2f;
    [Tooltip("Qué tan rápido gira hacia el enemigo con lock-on.")]
    public float lockOnTurnSpeed = 6f;

    // Lo prende la lámpara (clic derecho) para hacer zoom
    public bool Aiming { get; set; }

    private CinemachineCamera vcam;
    private CinemachineOrbitalFollow orbit;
    private CinemachineRotationComposer composer;
    private CinemachineBasicMultiChannelPerlin noise;
    private PlayerController player;
    private PlayerWeaponSheath sheath;

    private CameraProfile current;
    private float lastLookInputTime;
    private float previousYaw;
    private float tilt;

    private void Awake()
    {
        vcam = GetComponent<CinemachineCamera>();
        orbit = GetComponent<CinemachineOrbitalFollow>();
        composer = GetComponent<CinemachineRotationComposer>();
        noise = GetComponent<CinemachineBasicMultiChannelPerlin>();

        Transform target = vcam != null ? vcam.Follow : null;
        if (target != null)
        {
            player = target.GetComponentInParent<PlayerController>();
            if (player != null) sheath = player.GetComponent<PlayerWeaponSheath>();
        }
        current = explore;
    }

    private void Start()
    {
        // Arranca detrás del personaje, mirando hacia donde mira él
        if (orbit != null && player != null)
        {
            orbit.HorizontalAxis.Value = player.transform.eulerAngles.y;
            orbit.VerticalAxis.Value = 8f;
            previousYaw = orbit.HorizontalAxis.Value;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (orbit == null) return;
        float dt = Time.deltaTime;

        UpdateRotation(dt);
        UpdateProfile(dt);
        ApplyProfile(dt);
    }

    // ---------------------------------------------------------------
    // GIRO
    // ---------------------------------------------------------------

    private void UpdateRotation(float dt)
    {
        Transform lockTarget = player != null ? player.LockOnTarget : null;
        float yaw = orbit.HorizontalAxis.Value;
        float pitch = orbit.VerticalAxis.Value;

        if (lockTarget != null)
        {
            // Lock-on: la cámara gira sola para mirar al enemigo desde atrás del jugador
            Vector3 toTarget = lockTarget.position - player.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f)
            {
                float t = 1f - Mathf.Exp(-lockOnTurnSpeed * dt);
                yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg, t);
                pitch = Mathf.Lerp(pitch, 10f, t);
            }
            lastLookInputTime = Time.time;
        }
        else
        {
            Vector2 look = Vector2.zero;
            if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                look += new Vector2(delta.x * mouseSensitivityX, delta.y * mouseSensitivityY);
            }
            if (Gamepad.current != null)
            {
                Vector2 stick = Gamepad.current.rightStick.ReadValue();
                look += new Vector2(stick.x, stick.y * 0.6f) * gamepadSensitivity * dt;
            }

            if (look.sqrMagnitude > 0.0001f) lastLookInputTime = Time.time;
            yaw += look.x;
            pitch += invertY ? look.y : -look.y;

            // Acomodarse sola detrás del personaje si camina sin tocar el mouse
            if (autoRecenter && player != null && Time.time - lastLookInputTime > recenterDelay &&
                (player.CurrentState == PlayerState.Moving || player.CurrentState == PlayerState.Sprinting))
            {
                float t = 1f - Mathf.Exp(-recenterSpeed * dt);
                yaw = Mathf.LerpAngle(yaw, player.transform.eulerAngles.y, t);
            }
        }

        orbit.HorizontalAxis.Value = Mathf.Repeat(yaw + 180f, 360f) - 180f;
        orbit.VerticalAxis.Value = Mathf.Clamp(pitch, pitchRange.x, pitchRange.y);
    }

    // ---------------------------------------------------------------
    // ESTADOS
    // ---------------------------------------------------------------

    // En modo lámpara (Town) la cámara es solo de exploración: explorar, correr y el acercamiento
    // a la lámpara con clic derecho. En el resto (Dungeon) están todos los estados de combate.
    private CameraProfile TargetProfile()
    {
        if (ZoneSettings.LanternMode)
        {
            if (LanternZoomHeld()) return lanternZoom;
            if (player != null && player.CurrentState == PlayerState.Sprinting) return run;
            return explore;
        }

        if (Aiming) return aim;
        if (player != null && player.LockOnTarget != null) return lockOn;
        if (player != null && player.CurrentState == PlayerState.Sprinting) return run;
        if (sheath != null && sheath.IsDrawn) return combat;
        return explore;
    }

    // Clic derecho sostenido o gatillo izquierdo (también lo puede pedir otro script con Aiming)
    public bool LanternZoomHeld()
    {
        return Aiming || PlayerLantern.RaiseInputHeld();
    }

    private void UpdateProfile(float dt)
    {
        bool zooming = ZoneSettings.LanternMode && LanternZoomHeld();
        float blendTime = zooming ? lanternZoomBlendTime : profileBlendTime;
        float t = 1f - Mathf.Exp(-dt * 3f / Mathf.Max(0.01f, blendTime));
        current = CameraProfile.Lerp(current, TargetProfile(), t);
    }

    private void ApplyProfile(float dt)
    {
        orbit.Radius = current.distance;

        if (composer != null)
        {
            ScreenComposerSettings composition = composer.Composition;
            composition.ScreenPosition = new Vector2(current.shoulder, current.height);
            composer.Composition = composition;
        }

        if (noise != null)
        {
            noise.AmplitudeGain = current.shake;
            noise.FrequencyGain = current.shakeSpeed;
        }

        // Inclinación leve al girar rápido (como una cámara en mano que acompaña el giro)
        float yaw = orbit.HorizontalAxis.Value;
        float yawSpeed = Mathf.DeltaAngle(previousYaw, yaw) / Mathf.Max(dt, 0.0001f);
        previousYaw = yaw;
        float targetTilt = Mathf.Clamp(-yawSpeed * 0.02f, -turnTilt, turnTilt);
        tilt = Mathf.Lerp(tilt, targetTilt, 1f - Mathf.Exp(-6f * dt));

        LensSettings lens = vcam.Lens;
        lens.FieldOfView = current.fieldOfView;
        lens.Dutch = tilt;
        vcam.Lens = lens;
    }
}
