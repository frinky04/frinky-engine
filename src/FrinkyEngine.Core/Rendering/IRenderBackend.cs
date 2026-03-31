using Raylib_cs;

namespace FrinkyEngine.Core.Rendering;

internal interface IRenderBackend : IDisposable
{
    int LastFrameDrawCallCount { get; }
    int LastFrameSkinnedMeshCount { get; }

    RaylibRenderBackend.AutoInstancingFrameStats GetAutoInstancingFrameStats();
    RaylibRenderBackend.ForwardPlusFrameStats GetForwardPlusFrameStats();
    void ConfigureForwardPlus(ForwardPlusSettings settings);
    void LoadShader(string vsPath, string fsPath);
    void UnloadShader();
    RenderViewResult RenderView(RenderFrame frame, RenderVisibleSet visibleSet, RenderViewRequest request);
    void RenderDepthPrePass(RenderVisibleSet visibleSet, Camera3D camera, RenderTexture2D depthTarget, Shader depthShader);
    void RenderSelectionMask(RenderVisibleSet visibleSet, Camera3D camera, RenderTexture2D renderTarget);
    void ResolveToRenderTexture(RenderTargetHandle sourceHandle, RenderTexture2D destination);
    bool TryGetTexture(RenderTargetHandle handle, out Texture2D texture);
    bool TryGetRenderTexture(RenderTargetHandle handle, out RenderTexture2D renderTexture);
}
