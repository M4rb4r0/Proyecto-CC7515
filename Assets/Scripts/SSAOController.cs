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

    private void OnEnable(){
        targetCamera = GetComponent<Camera>();

        if (ssaoCompute == null){
            Debug.LogError("Assign SSAO.compute to SSAO Compute.", this);
            enabled = false;
            return;
        }

        computeAOKernel = ssaoCompute.FindKernel("ComputeAO");

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

        aoRenderTexture.Create();
    }
    private void OnDisable(){
        ReleaseGBufferCommandBuffer();
        ReleaseRenderTextures();
    }

    private void ReleaseRenderTextures(){
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
            "aoOutput",
            aoRenderTexture
        );

        int threadGroupsX =
            Mathf.CeilToInt(currentWidth / 8.0f);

        int threadGroupsY =
            Mathf.CeilToInt(currentHeight / 8.0f);

        gBufferCommandBuffer.DispatchCompute(
            ssaoCompute,
            computeAOKernel,
            threadGroupsX,
            threadGroupsY,
            1
        );
        targetMaterial.SetTexture("_AOTexture", aoRenderTexture);
        targetCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, gBufferCommandBuffer);
    }

    private void ReleaseGBufferCommandBuffer(){
        if (gBufferCommandBuffer == null) return;
        if (targetCamera != null) targetCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, gBufferCommandBuffer);

        gBufferCommandBuffer.Release();
        gBufferCommandBuffer = null;
    }
}
