using System;
using System.Runtime.InteropServices;
using UnityEngine;

/*
Buscamos calcular la suma de un arreglo en O(log n) pasadas de un kernel que hace un step de la reduccion,
sumando de a pares en cada paso. Genera overhead en modelos pequenhos pero se nota la ventaja cuando la
malla tiene muchos vertices (+1M)
*/

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class CentroidScaleGPU : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
    private struct VertexData
    {
        public Vector3 position;
        public Vector3 normal;
    }

    [Header("Connections")]
    public ComputeShader centroidScaleShader;

    [Header("Centroid scaling")]
    [Range(0.0f, 0.95f)]
    public float scaleAmplitude = 0.3f;

    public float scaleSpeed = 2.0f;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;
    private Material runtimeMaterial;

    // vertices, input y output del deformador.
    private ComputeBuffer baseBuffer;
    private ComputeBuffer vertexBuffer;

    // buffers temporales para la reduccion paralela
    private ComputeBuffer reductionBufferA;
    private ComputeBuffer reductionBufferB;

    // contiene un float4:
    // xyz = centroide
    // w   = placeholder
    private ComputeBuffer centroidBuffer;

    private int initializeSumsKernelID;
    private int reduceSumsKernelID;
    private int finalizeCentroidKernelID;
    private int scaleFromCentroidKernelID;

    private int vertexCount;
    private int vertexThreadGroups;

    private void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        if (centroidScaleShader == null)
        {
            Debug.LogError(
                "Assign CentroidScale.compute in the Inspector.",
                this
            );

            enabled = false;
            return;
        }

        mesh = meshFilter.mesh;

        if (mesh == null)
        {
            Debug.LogError(
                "The MeshFilter does not contain a mesh.",
                this
            );

            enabled = false;
            return;
        }

        if (!mesh.isReadable)
        {
            Debug.LogError(
                $"Mesh '{mesh.name}' is not readable. " +
                "Enable Read/Write in its import settings.",
                this
            );

            enabled = false;
            return;
        }

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;

        vertexCount = vertices.Length;

        if (vertexCount == 0)
        {
            Debug.LogError(
                "The mesh does not contain vertices.",
                this
            );

            enabled = false;
            return;
        }

        if (normals.Length != vertexCount)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        CreateVertexBuffers(vertices, normals);
        CreateReductionBuffers();
        FindKernels();
        ConfigureShader();
        ComputeCentroidMultiDispatch();
        BindRenderingMaterial();
        ExpandBounds();
    }

    private void CreateVertexBuffers(
        Vector3[] vertices,
        Vector3[] normals
    )
    {
        VertexData[] vertexData =
            new VertexData[vertexCount];

        for (int i = 0; i < vertexCount; ++i)
        {
            vertexData[i] = new VertexData
            {
                position = vertices[i],
                normal = normals[i]
            };
        }

        const int vertexStride =
            sizeof(float) * 6;

        baseBuffer = new ComputeBuffer(
            vertexCount,
            vertexStride
        );

        vertexBuffer = new ComputeBuffer(
            vertexCount,
            vertexStride
        );

        baseBuffer.SetData(vertexData);
        vertexBuffer.SetData(vertexData);
    }

    private void CreateReductionBuffers()
    {
        // Buffer A recibe una posicion por vertice
        reductionBufferA = new ComputeBuffer(
            vertexCount,
            sizeof(float) * 4
        );

        // output maximo despues de una pasada del kernel de reduccion:
        // ceil(vertexCount / 2).
        int reductionBufferBSize =
            Mathf.Max(1, (vertexCount + 1) / 2);

        reductionBufferB = new ComputeBuffer(
            reductionBufferBSize,
            sizeof(float) * 4
        );

        centroidBuffer = new ComputeBuffer(
            1,
            sizeof(float) * 4
        );
    }

    private void FindKernels()
    {
        initializeSumsKernelID =
            centroidScaleShader.FindKernel(
                "InitializeSums"
            );

        reduceSumsKernelID =
            centroidScaleShader.FindKernel(
                "ReduceSums"
            );

        finalizeCentroidKernelID =
            centroidScaleShader.FindKernel(
                "FinalizeCentroid"
            );

        scaleFromCentroidKernelID =
            centroidScaleShader.FindKernel(
                "ScaleFromCentroid"
            );
    }

    private void ConfigureShader()
    {
        centroidScaleShader.SetInt(
            "vertexCount",
            vertexCount
        );

        vertexThreadGroups = Mathf.Max(
            1,
            Mathf.CeilToInt(vertexCount / 256.0f)
        );
    }

    private void ComputeCentroidMultiDispatch()
    {
        // pasar la info del baseBuffer al reductionBufferA
        centroidScaleShader.SetBuffer(
            initializeSumsKernelID,
            "baseBuffer",
            baseBuffer
        );

        centroidScaleShader.SetBuffer(
            initializeSumsKernelID,
            "outputSums",
            reductionBufferA
        );

        centroidScaleShader.Dispatch(
            initializeSumsKernelID,
            vertexThreadGroups,
            1,
            1
        );

        ComputeBuffer input =
            reductionBufferA;

        ComputeBuffer output =
            reductionBufferB;

        int currentCount =
            vertexCount;

        // hacer la reduccion de a pares
        // recordar swapear los buffers
        while (currentCount > 1)
        {
            int outputCount =
                (currentCount + 1) / 2;

            centroidScaleShader.SetInt(
                "inputCount",
                currentCount
            );

            centroidScaleShader.SetBuffer(
                reduceSumsKernelID,
                "inputSums",
                input
            );

            centroidScaleShader.SetBuffer(
                reduceSumsKernelID,
                "outputSums",
                output
            );

            int reductionThreadGroups = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    outputCount / 256.0f
                )
            );

            centroidScaleShader.Dispatch(
                reduceSumsKernelID,
                reductionThreadGroups,
                1,
                1
            );
            ComputeBuffer temporary = input;
            input = output;
            output = temporary;

            currentCount = outputCount;
        }

        // input[0] contiene la respuesta
        centroidScaleShader.SetBuffer(
            finalizeCentroidKernelID,
            "finalSums",
            input
        );

        centroidScaleShader.SetBuffer(
            finalizeCentroidKernelID,
            "centroidBuffer",
            centroidBuffer
        );

        centroidScaleShader.Dispatch(
            finalizeCentroidKernelID,
            1,
            1,
            1
        );
    }

    private void BindRenderingMaterial()
    {
        // para evitar que se sobreescriban los vertexbuffers de distintos objetos usando este mismo material
        runtimeMaterial = meshRenderer.material;

        runtimeMaterial.SetBuffer(
            "vertexBuffer",
            vertexBuffer
        );
    }

    private void ExpandBounds()
    {
        // aumentar la bounding box por el culling
        Bounds bounds = mesh.bounds;
        bounds.extents *= 4.0f;
        mesh.bounds = bounds;
    }

    private void Update()
    {
        if (
            centroidScaleShader == null ||
            baseBuffer == null ||
            vertexBuffer == null ||
            centroidBuffer == null
        )
        {
            return;
        }

        centroidScaleShader.SetFloat(
            "time",
            Time.time
        );

        centroidScaleShader.SetFloat(
            "scaleSpeed",
            scaleSpeed
        );

        centroidScaleShader.SetFloat(
            "scaleAmplitude",
            scaleAmplitude
        );

        centroidScaleShader.SetBuffer(
            scaleFromCentroidKernelID,
            "baseBuffer",
            baseBuffer
        );

        centroidScaleShader.SetBuffer(
            scaleFromCentroidKernelID,
            "vertexBuffer",
            vertexBuffer
        );

        centroidScaleShader.SetBuffer(
            scaleFromCentroidKernelID,
            "centroidBuffer",
            centroidBuffer
        );

        centroidScaleShader.Dispatch(
            scaleFromCentroidKernelID,
            vertexThreadGroups,
            1,
            1
        );
    }

    private void OnDestroy()
    {
        ReleaseBuffer(ref baseBuffer);
        ReleaseBuffer(ref vertexBuffer);
        ReleaseBuffer(ref reductionBufferA);
        ReleaseBuffer(ref reductionBufferB);
        ReleaseBuffer(ref centroidBuffer);

        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    private static void ReleaseBuffer(
        ref ComputeBuffer buffer
    )
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }
}