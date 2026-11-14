using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Iori.Ui
{
    /// <summary>
    /// Makes the blurred copy of the scene that the glass panels show behind them. While
    /// <see cref="wanted"/> is on, a small extra step is added to the camera's drawing: the finished
    /// picture is shrunk and spread (see the shader "Hidden/IORI/UiBlur") into a small texture the UI
    /// shader reads. It costs nothing while no panel is open.
    /// </summary>
    public sealed class UiBackdrop : MonoBehaviour
    {
        static readonly int BackdropId = Shader.PropertyToID("_IoriUiBackdrop");
        static readonly int OnId = Shader.PropertyToID("_IoriUiBackdropOn");
        static readonly int FlipId = Shader.PropertyToID("_IoriUiBackdropFlip");
        static readonly int SaturationId = Shader.PropertyToID("_IoriUiBackdropSaturation");
        static readonly int GroundId = Shader.PropertyToID("_IoriUiGround");
        static readonly int SpreadId = Shader.PropertyToID("_IoriBlurSpread");

        /// <summary>For measuring what the blur costs: when set, the glass never shows the scene (as on a device without the shader).</summary>
        public static bool Off;

        public Camera sceneCamera;
        public bool wanted;
        [Tooltip("ガラス越しの絵が上下逆さに出る機種でだけ入れる")] public bool flip;
        [Tooltip("UI の下にあるものの、ふだんの明るさ(半透明の濃さの見え方を案に合わせるための値)")] [Range(0.05f, 0.6f)] public float ground = 0.22f;

        Material _material;
        BlurPass _pass;
        bool _failed;

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Publish(false);
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Publish(false);
            _pass?.Dispose();
            _pass = null;
            if (_material) CoreUtils.Destroy(_material);
            _material = null;
        }

        void Publish(bool on)
        {
            var th = UiTheme.Current;
            Shader.SetGlobalFloat(OnId, on ? 1f : 0f);
            Shader.SetGlobalFloat(FlipId, flip ? 1f : 0f);
            Shader.SetGlobalFloat(SaturationId, th.blurSaturation);
            Shader.SetGlobalFloat(GroundId, ground);
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!wanted || _failed || Off)
            {
                Publish(false);
                return;
            }
            if (!sceneCamera || cam != sceneCamera) return;
            if (!_material)
            {
                var shader = Resources.Load<Shader>("IoriUI/UiBlur");
                if (!shader || !shader.isSupported)
                {
                    Debug.LogWarning("[IORI] ぼかしのシェーダー IoriUI/UiBlur が使えないので、ガラスは景色を映さず濃いめの色で出ます");
                    _failed = true;
                    return;
                }
                _material = CoreUtils.CreateEngineMaterial(shader);
            }
            _pass ??= new BlurPass(_material);
            _material.SetFloat(SpreadId, UiTheme.Current.blurSpread);
            var data = cam.GetUniversalAdditionalCameraData();
            if (!data || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(_pass);
            Publish(_pass.Ready);
        }

        sealed class BlurPass : ScriptableRenderPass
        {
            sealed class PassData
            {
                public TextureHandle source;
                public Material material;
                public int pass;
            }

            readonly Material _material;
            RTHandle _result;

            /// <summary>True once the blurred picture has been drawn at least once.</summary>
            public bool Ready { get; private set; }

            public BlurPass(Material material)
            {
                _material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                // the finished picture must be somewhere it can be read from, not drawn straight to the screen
                requiresIntermediateTexture = true;
            }

            public void Dispose()
            {
                _result?.Release();
                _result = null;
                Ready = false;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer) return;
                var source = resources.activeColorTexture;
                if (!source.IsValid()) return;

                var desc = cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;
                desc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
                desc.useMipMap = false;
                desc.autoGenerateMips = false;

                // Halve until the picture is about 34 across (so the blur is the same share of the
                // screen on every device), then double back up to a quarter of the full size.
                int levels = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(Mathf.Max(8, desc.width) / 34f, 2f)), 3, 7);
                int w = desc.width, h = desc.height;
                var sizes = new Vector2Int[levels];
                for (int i = 0; i < levels; i++)
                {
                    w = Mathf.Max(1, w / 2);
                    h = Mathf.Max(1, h / 2);
                    sizes[i] = new Vector2Int(w, h);
                }

                var resultDesc = desc;
                resultDesc.width = sizes[1].x;
                resultDesc.height = sizes[1].y;
                RenderingUtils.ReAllocateHandleIfNeeded(ref _result, resultDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_IoriUiBackdrop");
                var result = renderGraph.ImportTexture(_result);

                var current = source;
                for (int i = 0; i < levels; i++)
                {
                    var d = desc;
                    d.width = sizes[i].x;
                    d.height = sizes[i].y;
                    var target = UniversalRenderer.CreateRenderGraphTexture(renderGraph, d, "IORI UI blur down", false, FilterMode.Bilinear);
                    Blit(renderGraph, current, target, i == 0 ? 0 : 1, "IORI UI blur down");
                    current = target;
                }
                for (int i = levels - 2; i >= 1; i--)
                {
                    TextureHandle target;
                    if (i == 1) target = result;
                    else
                    {
                        var d = desc;
                        d.width = sizes[i].x;
                        d.height = sizes[i].y;
                        target = UniversalRenderer.CreateRenderGraphTexture(renderGraph, d, "IORI UI blur up", false, FilterMode.Bilinear);
                    }
                    Blit(renderGraph, current, target, 2, "IORI UI blur up");
                    current = target;
                }

                Shader.SetGlobalTexture(BackdropId, _result.rt);
                Ready = true;
            }

            void Blit(RenderGraph renderGraph, TextureHandle from, TextureHandle to, int pass, string name)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data))
                {
                    data.source = from;
                    data.material = _material;
                    data.pass = pass;
                    builder.UseTexture(from);
                    builder.SetRenderAttachment(to, 0);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, d.pass));
                }
            }
        }
    }
}
