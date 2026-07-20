using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class SSAOController : MonoBehaviour
{
    private Camera targetCamera;
    private RenderTexture positionTexture;
    private RenderTexture normalTexture;

    private int currentWidth;
    private int currentHeight;

    [Header("Geometry")]
    [SerializeField]
    private MeshRenderer targetRenderer;

    private CommandBuffer gBufferCommandBuffer;
    private Material targetMaterial;
    private int gBufferPassIndex;

    [Header("SSAO")]
    [SerializeField]
    private ComputeShader ssaoCompute;

    private RenderTexture aoRenderTexture;
    private int computeAOKernel;

    [SerializeField, Min(0.001f)]
    private float sampleRadius = 0.3f;

    [SerializeField, Min(0.0f)]
    private float bias = 0.005f;

    [SerializeField, Range(1, 64)]
    private int sampleCount = 32;
    private ComputeBuffer sampleKernelBuffer;

    private Texture2D noiseTexture;
    private const int NoiseTextureSize = 4;

    private RenderTexture aoBlurredRenderTexture;
    private int blurAOKernel;

    private void OnEnable(){
        targetCamera = GetComponent<Camera>();

        if (ssaoCompute == null){
            Debug.LogError("Assign SSAO.compute to SSAO Compute.", this);
            enabled = false;
            return;
        }

        computeAOKernel = ssaoCompute.FindKernel("ComputeAO");
        blurAOKernel = ssaoCompute.FindKernel("BlurAO");
        CreateSampleKernel();
        CreateNoiseTexture();
        CreateRenderTextures();
        CreateGBufferCommandBuffer();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update(){
        if (targetCamera.pixelWidth != currentWidth || targetCamera.pixelHeight != currentHeight){
            ReleaseGBufferCommandBuffer();
            CreateRenderTextures();
            CreateGBufferCommandBuffer();
        }
    }

    void CreateRenderTextures(){
        ReleaseRenderTextures();
        currentWidth = targetCamera.pixelWidth;
        currentHeight = targetCamera.pixelHeight;
        positionTexture = new RenderTexture(
            currentWidth, currentHeight,
            24,
            RenderTextureFormat.ARGBFloat
        );
        positionTexture.name = "SSAO Position Texture";
        positionTexture.filterMode = FilterMode.Point;
        positionTexture.wrapMode = TextureWrapMode.Clamp;
        positionTexture.Create();
        normalTexture = new RenderTexture(
            currentWidth, currentHeight,
            0,
            RenderTextureFormat.ARGBHalf
        );
        normalTexture.name = "SSAO Normal Texture";
        normalTexture.filterMode = FilterMode.Point;
        normalTexture.wrapMode = TextureWrapMode.Clamp;
        normalTexture.Create();

        aoRenderTexture = new RenderTexture(
            currentWidth,
            currentHeight,
            0,
            RenderTextureFormat.RFloat
        );

        aoRenderTexture.name = "SSAO Texture";
        aoRenderTexture.filterMode = FilterMode.Point;
        aoRenderTexture.wrapMode = TextureWrapMode.Clamp;

        // fix
        aoRenderTexture.enableRandomWrite = true;

        aoBlurredRenderTexture = new RenderTexture(
            currentWidth,
            currentHeight,
            0,
            RenderTextureFormat.RFloat
        );

        aoBlurredRenderTexture.name =
            "SSAO Blurred Texture";

        aoBlurredRenderTexture.filterMode =
            FilterMode.Bilinear;

        aoBlurredRenderTexture.wrapMode =
            TextureWrapMode.Clamp;

        aoBlurredRenderTexture.enableRandomWrite =
            true;

        aoBlurredRenderTexture.Create();

        aoRenderTexture.Create();
    }
    private void OnDisable(){
        ReleaseGBufferCommandBuffer();
        ReleaseRenderTextures();
        ReleaseSampleKernel();
        ReleaseNoiseTexture();
    }

    private void ReleaseRenderTextures(){
        // boilerplate
        if (positionTexture != null){
            positionTexture.Release();
            Destroy(positionTexture);
            positionTexture = null;
        }

        if (normalTexture != null){
            normalTexture.Release();
            Destroy(normalTexture);
            normalTexture = null;
        }

        if (aoRenderTexture != null){
            aoRenderTexture.Release();
            Destroy(aoRenderTexture);
            aoRenderTexture = null;
        }

        if (aoBlurredRenderTexture != null)
        {
            aoBlurredRenderTexture.Release();
            Destroy(aoBlurredRenderTexture);
            aoBlurredRenderTexture = null;
        }
    }

    private void CreateGBufferCommandBuffer(){
        ReleaseGBufferCommandBuffer();

        if (targetRenderer == null){
            Debug.LogError("Assign the model's MeshRenderer to Target Renderer.", this);
            return;
        }

        // obtenemos la instancia del material que ya contiene
        // el vertexBuffer de la deformación
        targetMaterial = targetRenderer.material;

        gBufferPassIndex = targetMaterial.FindPass("SSAOGBuffer");


        gBufferCommandBuffer = new CommandBuffer();

        gBufferCommandBuffer.name = "Render SSAO GBuffer";

        RenderTargetIdentifier[] colorTargets ={
            new RenderTargetIdentifier(positionTexture),
            new RenderTargetIdentifier(normalTexture)
        };


        RenderTargetIdentifier depthTarget = new RenderTargetIdentifier(positionTexture);

        gBufferCommandBuffer.SetRenderTarget(colorTargets, depthTarget);
        gBufferCommandBuffer.ClearRenderTarget(true, true, Color.clear);
        gBufferCommandBuffer.DrawRenderer(targetRenderer, targetMaterial, 0, gBufferPassIndex);

        gBufferCommandBuffer.SetComputeTextureParam(
            ssaoCompute,
            computeAOKernel,
            "positionTexture",
            positionTexture
        );

        gBufferCommandBuffer.SetComputeTextureParam(
            ssaoCompute,
            computeAOKernel,
            "normalTexture",
            normalTexture
        );

        gBufferCommandBuffer.SetComputeTextureParam(
            ssaoCompute,
            computeAOKernel,
            "aoOutput",
            aoRenderTexture
        );

        int threadGroupsX = Mathf.CeilToInt(currentWidth / 8.0f);

        int threadGroupsY = Mathf.CeilToInt(currentHeight / 8.0f);

        Matrix4x4 gpuProjectionMatrix = GL.GetGPUProjectionMatrix(targetCamera.projectionMatrix, true);

        // cargar las variables del compute shader
        gBufferCommandBuffer.SetComputeMatrixParam(
            ssaoCompute,
            "projectionMatrix",
            gpuProjectionMatrix
        );

        gBufferCommandBuffer.SetComputeFloatParam(
            ssaoCompute,
            "sampleRadius",
            sampleRadius
        );

        gBufferCommandBuffer.SetComputeFloatParam(
            ssaoCompute,
            "bias",
            bias
        );

        gBufferCommandBuffer.SetComputeBufferParam(
            ssaoCompute,
            computeAOKernel,
            "sampleKernel",
            sampleKernelBuffer
        );

        gBufferCommandBuffer.SetComputeIntParam(
            ssaoCompute,
            "sampleCount",
            sampleCount
        );

        gBufferCommandBuffer.SetComputeTextureParam(
            ssaoCompute,
            computeAOKernel,
            "noiseTexture",
            noiseTexture
        );

        gBufferCommandBuffer.SetComputeIntParam(
            ssaoCompute,
            "noiseTextureSize",
            NoiseTextureSize
);

        // dispatch kernel
        gBufferCommandBuffer.DispatchCompute(
            ssaoCompute,
            computeAOKernel,
            threadGroupsX,
            threadGroupsY,
            1
        );

        gBufferCommandBuffer.SetComputeTextureParam(
            ssaoCompute,
            blurAOKernel,
            "aoInput",
            aoRenderTexture
        );

        gBufferCommandBuffer.SetComputeTextureParam(
            ssaoCompute,
            blurAOKernel,
            "aoBlurOutput",
            aoBlurredRenderTexture
        );

        gBufferCommandBuffer.DispatchCompute(
            ssaoCompute,
            blurAOKernel,
            threadGroupsX,
            threadGroupsY,
            1
        );
        targetMaterial.SetTexture("_AOTexture", aoBlurredRenderTexture);
        targetCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, gBufferCommandBuffer);
    }

    private void ReleaseGBufferCommandBuffer(){
        if (gBufferCommandBuffer == null) return;
        if (targetCamera != null) targetCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, gBufferCommandBuffer);

        gBufferCommandBuffer.Release();
        gBufferCommandBuffer = null;
    }

    private void CreateSampleKernel(){
        ReleaseSampleKernel();

        Vector4[] samples = new Vector4[sampleCount];

        System.Random random = new System.Random(99824435);

        for (int i = 0; i < sampleCount; ++i){
            Vector3 sample = new Vector3(
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)random.NextDouble()
            );

            sample.Normalize();

            // sampleamos dentro de la semiesfera
            sample *= (float)random.NextDouble();
            float t = sampleCount > 1 ? i / (float)(sampleCount - 1): 0.0f;
            float scale = Mathf.Lerp(0.1f, 1.0f, t * t);
            sample *= scale;

            samples[i] = new Vector4(
                sample.x,
                sample.y,
                sample.z,
                0.0f
            );
        }

        sampleKernelBuffer = new ComputeBuffer(
            sampleCount,
            sizeof(float) * 4
        );

        sampleKernelBuffer.SetData(samples);
    }

    private void ReleaseSampleKernel(){
        if (sampleKernelBuffer == null) return;

        sampleKernelBuffer.Release();
        sampleKernelBuffer = null;
    }

    private void CreateNoiseTexture(){
        ReleaseNoiseTexture();

        int pixelCount =
            NoiseTextureSize * NoiseTextureSize;

        Color[] noisePixels = new Color[pixelCount];

        System.Random random = new System.Random(54321);

        for (int i = 0; i < pixelCount; ++i){
            float x = (float)(random.NextDouble() * 2.0 - 1.0);
            float y = (float)(random.NextDouble() * 2.0 - 1.0);
            Vector2 direction = new Vector2(x, y).normalized;
            noisePixels[i] = new Color(direction.x, direction.y, 0.0f, 0.0f);
        }

        noiseTexture = new Texture2D(
            NoiseTextureSize,
            NoiseTextureSize,
            TextureFormat.RGBAFloat,
            false,
            true
        );

        noiseTexture.name = "SSAO Noise Texture";
        noiseTexture.filterMode = FilterMode.Point;
        noiseTexture.wrapMode = TextureWrapMode.Repeat;

        noiseTexture.SetPixels(noisePixels);
        noiseTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
    }

    private void ReleaseNoiseTexture(){
        if (noiseTexture == null) return;
        Destroy(noiseTexture);
        noiseTexture = null;
    }
}   