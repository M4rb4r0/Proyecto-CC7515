using UnityEngine;

public class DeformadorGPUSmall : MonoBehaviour
{
    struct VertexData
    {
        public Vector3 position;
        public Vector3 normal;
    }

    [Header("Conexiones")]
    public ComputeShader deformShader;

    [Header("Parámetros de Deformación")]
    public float waveSpeed = 15.0f;
    public float waveHeight = 5.0f;

    private MeshFilter meshFilter;
    private ComputeBuffer vertexBuffer;
    private ComputeBuffer baseBuffer;
    private VertexData[] vertexArray;
    private int kernelID;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        Mesh mesh = meshFilter.mesh;
        //mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int vertexCount = vertices.Length;

        vertexArray = new VertexData[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertexArray[i] = new VertexData { position = vertices[i], normal = normals[i] };
        }

        vertexBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 6);
        vertexBuffer.SetData(vertexArray);

        baseBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 6);
        baseBuffer.SetData(vertexArray);

        kernelID = deformShader.FindKernel("DeformMesh");

        deformShader.SetInt("vertexCount", vertexCount);

        deformShader.SetBuffer(kernelID, "vertexBuffer", vertexBuffer);
        deformShader.SetBuffer(kernelID, "baseBuffer", baseBuffer);

        Material material = GetComponent<MeshRenderer>().material;
        material.SetBuffer("vertexBuffer", vertexBuffer);
    }

    void Update()
    {
        deformShader.SetInt("vertexCount", vertexArray.Length);
        deformShader.SetBuffer(kernelID, "vertexBuffer", vertexBuffer);
        deformShader.SetBuffer(kernelID, "baseBuffer", baseBuffer);

        deformShader.SetFloat("time", Time.time);
        deformShader.SetFloat("waveSpeed", waveSpeed);
        deformShader.SetFloat("waveHeight", waveHeight);

        int threadGroups = Mathf.Max(1, Mathf.CeilToInt(vertexArray.Length / 256.0f));
        deformShader.Dispatch(kernelID, threadGroups, 1, 1);
    }

    void OnDestroy()
    {
        if (vertexBuffer != null) vertexBuffer.Release();
        if (baseBuffer != null) baseBuffer.Release();
    }
}