using UnityEngine;

public class FreeFlyCamera : MonoBehaviour
{
    [Header("Configuración de Movimiento")]
    public float movementSpeed = 10f;
    public float sprintMultiplier = 3f;

    [Header("Configuración de Rotación")]
    public float lookSensitivity = 2f;

    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        // Oculta y bloquea el cursor en el centro de la pantalla al iniciar
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Inicializa la rotación basada en la rotación actual de la cámara
        Vector3 rot = transform.localRotation.eulerAngles;
        rotationY = rot.y;
        rotationX = rot.x;
    }

    void Update()
    {
        // 1. Manejo de la Rotación (Ratón)
        rotationY += Input.GetAxis("Mouse X") * lookSensitivity;
        rotationX -= Input.GetAxis("Mouse Y") * lookSensitivity;

        // Limita la rotación vertical para no dar volteretas
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0f);

        // 2. Manejo del Movimiento (Teclado)
        float currentSpeed = movementSpeed;

        // Multiplicador de velocidad al mantener Shift
        if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed *= sprintMultiplier;
        }

        float horizontal = Input.GetAxis("Horizontal"); // A y D
        float vertical = Input.GetAxis("Vertical");     // W y S
        float upDown = 0f;

        // Subir y bajar con E y Q
        if (Input.GetKey(KeyCode.E)) upDown = 1f;
        if (Input.GetKey(KeyCode.Q)) upDown = -1f;

        // Calcula el vector de movimiento y lo aplica en el espacio local de la cámara
        Vector3 movement = new Vector3(horizontal, upDown, vertical) * currentSpeed * Time.deltaTime;
        transform.Translate(movement, Space.Self);

        // 3. Desbloquear el cursor (con la tecla Escape)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}