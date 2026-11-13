using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Iori.Camp
{
    /// <summary>
    /// A mirror image for a flat water surface. A hidden camera sits mirrored under the surface
    /// (a proper rotation, so no culling flip is needed; the image comes out mirrored left-right
    /// and the water shader reads it with x flipped). The ground, the grass and the water itself are
    /// not drawn into it. It renders at a fraction of the screen size.
    /// The camp has two: one for the lake and one for the puddles, which lie higher; the puddles'
    /// one only runs while there are puddles, and can be switched off to save work (the puddles then
    /// borrow the lake's image, slightly off).
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    public sealed class CampReflection : MonoBehaviour
    {
        public Camera source;
        [Tooltip("シェーダーに渡す名前")] public string textureName = "_IoriReflectionTex";
        [Tooltip("水面の高さ。followPuddle がオンなら水たまりの今の高さを使う")] public float planeY = -0.24f;
        public bool followPuddle;
        [Range(0.1f, 1f)] public float scale = 0.5f;
        public float clipOffset = 0.01f;
        public bool obliqueClip = true;
        public LayerMask mask = CampLayers.MirrorMask;
        [Tooltip("使わないあいだ、代わりにこの鏡像を貸す")] public CampReflection fallback;
        [Tooltip("オフにすると鏡像を作らず、fallback を使う(軽くする用)")] public bool render = true;

        Camera _cam;
        RenderTexture _rt;
        int _id;

        public Camera ReflectionCamera => _cam;
        public RenderTexture Texture => _rt;
        public bool Active { get; private set; }

        void OnEnable()
        {
            _id = Shader.PropertyToID(textureName);
            if (!_cam)
            {
                var go = new GameObject("Mirror camera " + textureName + " (自動生成)") { hideFlags = HideFlags.HideAndDontSave };
                _cam = go.AddComponent<Camera>();
                _cam.enabled = false;
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
            if (_id == 0) _id = Shader.PropertyToID(textureName);
            var atm = CampAtmosphere.Instance;
            float y = planeY;
            bool wanted = render;
            if (followPuddle)
            {
                if (atm && atm.puddle) y = atm.puddle.transform.position.y;
                wanted &= atm && atm.Wet > 0.01f;
            }
            Active = wanted;
            _cam.enabled = wanted;
            if (!wanted)
            {
                if (fallback && fallback.Texture) Shader.SetGlobalTexture(_id, fallback.Texture);
                return;
            }

            int w = Mathf.Max(64, Mathf.RoundToInt(source.pixelWidth * scale));
            int h = Mathf.Max(64, Mathf.RoundToInt(source.pixelHeight * scale));
            if (!_rt || _rt.width != w || _rt.height != h)
            {
                // (the camera must let go of the old texture first, or Unity reports an error when the screen changes size)
                if (_rt) { _cam.targetTexture = null; _rt.Release(); DestroyImmediate(_rt); }
                _rt = new RenderTexture(w, h, 16, RenderTextureFormat.DefaultHDR) { name = "IORI mirror " + textureName, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
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
            var pos = st.position; pos.y = 2 * y - pos.y;
            var f = st.forward; f.y = -f.y;
            var u = st.up; u.y = -u.y;
            _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(f, u));

            _cam.ResetProjectionMatrix();
            if (obliqueClip)
            {
                var w2c = _cam.worldToCameraMatrix;
                var cpos = w2c.MultiplyPoint(new Vector3(0, y + clipOffset, 0));
                var cn = w2c.MultiplyVector(Vector3.up).normalized;
                _cam.projectionMatrix = _cam.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cpos, cn)));
            }
            Shader.SetGlobalTexture(_id, _rt);
        }
    }
}
