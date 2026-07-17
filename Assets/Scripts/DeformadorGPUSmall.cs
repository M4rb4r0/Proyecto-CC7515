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

    // buffers del kernel de las caras
    private ComputeBuffer triangleIndices;
    private ComputeBuffer faceNormalBuffer;

    // buffers del kernel de los vertices
    private ComputeBuffer incidentOffsets;
    private ComputeBuffer incidentTriangles;

    private VertexData[] vertexArray;
    private int kernelID;
    private int faceNormalKernelID;
    private int vtxNormalKernelID;
    private int triangleCount;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        Mesh mesh = meshFilter.mesh;
        //mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int vertexCount = vertices.Length;

        // crear buffers con los triangulos
        int[] meshTriangles = mesh.triangles;
         triangleCount = meshTriangles.Length / 3;

        triangleIndices = new ComputeBuffer(meshTriangles.Length, sizeof(int));
        triangleIndices.SetData(meshTriangles);

        faceNormalBuffer = new ComputeBuffer(triangleCount, sizeof(float) * 4); // este es output del compute shader

        deformShader.SetInt("triangleCount", triangleCount);

        // crear buffers del kernel para las normales de cada vértice
        int[] incidentCounts = new int[vertexCount]; // guarda cuantos triangulos son incidentes a cada vertice
        for(int i = 0; i<triangleCount; i++){
            for(int j = 0; j < 3; j++){
                int vtxid = meshTriangles[3*i + j];
                incidentCounts[vtxid]++;
            }
        }

        // prefix sum sobre los offsets
        int[] offsetData = new int[vertexCount + 1];
        for(int i = 0; i < vertexCount; i++){
            offsetData[i+1] = offsetData[i] + incidentCounts[i];
        }

        // guarda la id de todos los triangulos que lo contienen
        int[] incidentTriangleData = new int[meshTriangles.Length];

        int[] writePositions = (int[])offsetData.Clone();

        for (int i = 0; i < triangleCount; i++){
            for(int j = 0; j < 3; j++){
                int pt = meshTriangles[3*i + j];
                incidentTriangleData[writePositions[pt]++] = i;
            }
        }

        // crear los buffers
        incidentOffsets = new ComputeBuffer(offsetData.Length, sizeof(int));

        incidentOffsets.SetData(offsetData);

        incidentTriangles = new ComputeBuffer(incidentTriangleData.Length, sizeof(int));

        incidentTriangles.SetData(incidentTriangleData);


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

        // kernels de normales
        faceNormalKernelID = deformShader.FindKernel("ComputeFaceNormals");
        vtxNormalKernelID = deformShader.FindKernel("ComputeVertexNormals");

        deformShader.SetBuffer(faceNormalKernelID, "vertexBuffer", vertexBuffer);
        deformShader.SetBuffer(faceNormalKernelID, "triangleIndices", triangleIndices);
        deformShader.SetBuffer(faceNormalKernelID, "faceNormalBuffer", faceNormalBuffer);

        deformShader.SetBuffer(vtxNormalKernelID, "faceNormalBuffer", faceNormalBuffer);
        deformShader.SetBuffer(vtxNormalKernelID, "vertexBuffer", vertexBuffer);
        deformShader.SetBuffer(vtxNormalKernelID, "baseBuffer", baseBuffer);
        deformShader.SetBuffer(vtxNormalKernelID, "incidentOffsets", incidentOffsets);
        deformShader.SetBuffer(vtxNormalKernelID, "incidentTriangles", incidentTriangles);
        
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
        int vertexThreadGroups = Mathf.Max(1, Mathf.CeilToInt(vertexArray.Length / 256.0f));
        int triangleThreadGroups = Mathf.Max(1, Mathf.CeilToInt(triangleCount / 256.0f));

        deformShader.Dispatch(kernelID, threadGroups, 1, 1);
        deformShader.Dispatch(faceNormalKernelID, triangleThreadGroups, 1, 1);
        deformShader.Dispatch(vtxNormalKernelID, vertexThreadGroups, 1, 1);
    }

    void OnDestroy()
    {
        if (vertexBuffer != null) vertexBuffer.Release();
        if (baseBuffer != null) baseBuffer.Release();
        if (triangleIndices != null) triangleIndices.Release();
        if (faceNormalBuffer != null) faceNormalBuffer.Release();
        if (incidentOffsets != null) incidentOffsets.Release();
        if (incidentTriangles != null) incidentTriangles.Release();
    }
}