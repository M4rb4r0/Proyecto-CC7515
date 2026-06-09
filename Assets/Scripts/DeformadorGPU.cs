using UnityEngine;

public class DeformadorGPU : MonoBehaviour
{
    struct VertexData
    {
        public Vector3 position;
        public Vector3 normal;
    }

    [Header("Conexiones")]
    public ComputeShader deformShader;

    [Header("Parámetros de Deformación")]
    public float waveSpeed = 2.0f;
    public float waveHeight = 0.5f;

    private MeshFilter meshFilter;
    private Mesh mesh;
    private ComputeBuffer vertexBuffer;
    private ComputeBuffer baseBuffer;
    private VertexData[] vertexArray;
    private int kernelID;

    void Start()
    {
        // Obtenemos la geometría del modelo 3D
        meshFilter = GetComponent<MeshFilter>();
        mesh = meshFilter.mesh;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int vertexCount = vertices.Length;

        // Preparamos nuestro arreglo de datos
        vertexArray = new VertexData[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertexArray[i] = new VertexData
            {
                position = vertices[i],
                normal = normals[i]
            };
        }

        // Buffer de trabajo (Lectura/Escritura)
        vertexBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 6);
        vertexBuffer.SetData(vertexArray);

        // Buffer original inmutable (Solo Lectura)
        baseBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 6);
        baseBuffer.SetData(vertexArray);

        // Vinculamos AMBOS buffers al kernel de nuestro shader
        kernelID = deformShader.FindKernel("DeformMesh");
        deformShader.SetBuffer(kernelID, "vertexBuffer", vertexBuffer);
        deformShader.SetBuffer(kernelID, "baseBuffer", baseBuffer);
    }

    void Update()
    {
        // Enviamos variables dinámicas de tiempo al shader
        deformShader.SetFloat("time", Time.time);
        deformShader.SetFloat("waveSpeed", waveSpeed);
        deformShader.SetFloat("waveHeight", waveHeight);

        // Ejecutamos el shader masivamente en paralelo
        // Dividimos la cantidad de vértices entre 256
        int threadGroups = Mathf.CeilToInt(vertexArray.Length / 256.0f);
        deformShader.Dispatch(kernelID, threadGroups, 1, 1);

        // --- FASE 1: LECTURA DE VUELTA ---
        // Para visualizar los cambios usando el sistema estándar de Unity, 
        // traemos los datos modificados de vuelta a la CPU.
        vertexBuffer.GetData(vertexArray);

        Vector3[] newVertices = new Vector3[vertexArray.Length];
        for (int i = 0; i < vertexArray.Length; i++)
        {
            newVertices[i] = vertexArray[i].position;
        }

        // Actualizamos la malla visible
        mesh.vertices = newVertices;
        mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (vertexBuffer != null)
        {
            if (vertexBuffer != null) vertexBuffer.Release();
            if (baseBuffer != null) baseBuffer.Release();
        }
    }
}