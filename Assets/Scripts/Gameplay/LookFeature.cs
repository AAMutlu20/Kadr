using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Gameplay
{
    /// <summary>
    /// Runs the Kadr look (KadrLook.shader) on the player's camera only. It replaces Unity's Full Screen Pass Renderer
    /// Feature, which also runs on every other camera, including the weapon's overlay camera in a camera stack. There
    /// the depth is empty, so the pass fogged the whole world as if it were at the far plane.
    ///
    /// Runs on: the scene view (so you can tune in the editor) and the Base camera tagged MainCamera.
    /// Skips: overlay cameras, the inventory and photo cameras, previews, reflection probes.
    ///
    /// Add it to the URP Renderer (Add Renderer Feature > Kadr Look Feature), assign the material that uses the
    /// Hidden/Kadr/Look shader, and remove the Full Screen Pass feature. Needs the Render Graph path (the Unity 6 default).
    /// </summary>
    public class KadrLookFeature : ScriptableRendererFeature
    {
        [SerializeField] private Material material;
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        private LookPass _pass;
        private static readonly System.Collections.Generic.HashSet<string> Warned = new();

        public override void Create()
        {
            _pass = new LookPass { renderPassEvent = injectionPoint };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!material)
            {
                Warn("material", "Kadr Look Feature has no material assigned. Assign the material that uses Hidden/Kadr/Look.");
                return;
            }
            if (_pass == null) return;
            if (!Applies(ref renderingData.cameraData))
            {
                ExplainSkip(ref renderingData.cameraData);
                return;
            }

            _pass.Setup(material);
            renderer.EnqueuePass(_pass);
        }

        // Tells you once per camera why the look is not running there. Editor and development builds only.
        private static void ExplainSkip(ref CameraData cameraData)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var cam = cameraData.camera;
            if (!cam || cameraData.cameraType != CameraType.Game) return;

            string reason = null;
            if (cameraData.renderType != CameraRenderType.Base) reason = "it is an Overlay camera (expected: the look runs on the Base camera)";
            else if (cam.targetTexture) reason = "it renders to a texture (photo camera, minimap...)";
            else if (!cam.CompareTag("MainCamera")) reason = $"its tag is '{cam.tag}', not MainCamera";

            if (reason != null) Warn(cam.name, $"Kadr Look skipped camera '{cam.name}': {reason}.");
#endif
        }

        private static void Warn(string key, string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Warned.Add(key)) Debug.LogWarning(message);
#endif
        }

        private static bool Applies(ref CameraData cameraData)
        {
            if (cameraData.cameraType == CameraType.SceneView) return true;
            if (cameraData.cameraType != CameraType.Game) return false;
            if (cameraData.renderType != CameraRenderType.Base) return false;

            var cam = cameraData.camera;
            return cam && !cam.targetTexture && cam.CompareTag("MainCamera");
        }

        private class LookPass : ScriptableRenderPass
        {
            private Material _material;

            private class PassData
            {
                public TextureHandle Source;
                public Material Material;
            }

            public void Setup(Material material)
            {
                _material = material;
                ConfigureInput(ScriptableRenderPassInput.Depth); // the shader reads the depth texture
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return; // nothing to read from

                var source = resources.activeColorTexture;
                var description = renderGraph.GetTextureDesc(source);
                description.name = "_KadrLook";
                description.clearBuffer = false;
                var destination = renderGraph.CreateTexture(description);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Kadr Look", out var data))
                {
                    data.Source = source;
                    data.Material = _material;

                    builder.UseTexture(source, AccessFlags.Read);
                    if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, 0);
                    });
                }

                resources.cameraColor = destination;
            }
        }
    }
}