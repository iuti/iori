using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Iori.Camp
{
    /// <summary>
    /// One planar reflection shared by the lake and the puddles. A hidden camera sits mirrored under
    /// the puddle plane (a proper rotation, so no culling flip is needed; the image comes out
    /// mirrored left-right and the water shader samples it with x flipped). The ground and grass
    /// are not drawn into it. It renders at a fraction of the screen size, more when it is wet.
    /// Also raises and lowers the puddle water with the wetness.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    public sealed class CampReflection : MonoBehaviour
    {
        public Camera source;
        public CampBuilder builder;
        [Range(0.1f, 1f)] public float dryScale = 0.33f;
        [Range(0.1f, 1f)] public float wetScale = 0.5f;
        public float planeY = -0.015f;
        public float clipOffset = 0.02f;
        public bool obliqueClip = true;
        public LayerMask mask = ~((1 << CampBuilder.GroundLayer) | (1 << CampBuilder.WaterLayer) | (1 << CampBuilder.RainLayer) | (1 << 5));

        Camera _cam;
        RenderTexture _rt;
        static readonly int ReflTexId = Shader.PropertyToID("_IoriReflectionTex");

        public Camera ReflectionCamera => _cam;

        void OnEnable()
        {
            if (!_cam)
            {
                var go = new GameObject("Reflection camera (自動生成)") { hideFlags = HideFlags.HideAndDontSave };
                _cam = go.AddComponent<Camera>();
                _cam.enabled = true;
                _cam.clearFlags = CameraClearFlags.Skybox;
                _cam.allowHDR = true;
                _cam.allowMSAA = false;
                var data = _cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
                data.antialiasing = AntialiasingMode.None;
            }
        }

        void OnDisable()
        {
            if (_cam) DestroyImmediate(_cam.gameObject);
            _cam = null;
            if (_rt) { _rt.Release(); DestroyImmediate(_rt); }
            _rt = null;
        }

        void LateUpdate() => UpdateNow();

        /// <summary>Places the mirrored camera and sizes its texture for the current frame.</summary>
        public void UpdateNow()
        {
            if (!source || !_cam) return;
            var atm = CampAtmosphere.Instance;
            float wet = atm ? atm.Wet : 0f;

            if (builder && builder.Puddle)
            {
                var t = builder.Puddle.transform;
                var p = t.localPosition;
                p.y = Mathf.Lerp(planeY - 0.09f, planeY, Mathf.SmoothStep(0, 1, wet));
                t.localPosition = p;
                builder.Puddle.enabled = wet > 0.01f;
            }

            float scale = Mathf.Lerp(dryScale, wetScale, wet);
            int w = Mathf.Max(64, Mathf.RoundToInt(source.pixelWidth * scale));
            int h = Mathf.Max(64, Mathf.RoundToInt(source.pixelHeight * scale));
            if (!_rt || _rt.width != w || _rt.height != h)
            {
                if (_rt) { _rt.Release(); DestroyImmediate(_rt); }
                _rt = new RenderTexture(w, h, 16, RenderTextureFormat.DefaultHDR) { name = "IORI reflection", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                _rt.Create();
            }
            _cam.targetTexture = _rt;
            _cam.cullingMask = mask;
            _cam.depth = source.depth - 1;
            _cam.fieldOfView = source.fieldOfView;
            _cam.nearClipPlane = source.nearClipPlane;
            _cam.farClipPlane = source.farClipPlane;
            _cam.aspect = source.aspect;

            var st = source.transform;
            var pos = st.position; pos.y = 2 * planeY - pos.y;
            var f = st.forward; f.y = -f.y;
            var u = st.up; u.y = -u.y;
            _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(f, u));

            _cam.ResetProjectionMatrix();
            if (obliqueClip)
            {
                var w2c = _cam.worldToCameraMatrix;
                var cpos = w2c.MultiplyPoint(new Vector3(0, planeY + clipOffset, 0));
                var cn = w2c.MultiplyVector(Vector3.up).normalized;
                _cam.projectionMatrix = _cam.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cpos, cn)));
            }
            Shader.SetGlobalTexture(ReflTexId, _rt);
        }
    }
}
