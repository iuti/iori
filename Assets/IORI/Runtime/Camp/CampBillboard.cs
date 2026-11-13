using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// Iori as a 2D board standing in the scene (placeholder until the livliv model renders into a
    /// texture). Turns around the vertical axis to face the main camera, so it also faces the same
    /// way in the mirrored reflection. Its feet sit on the generated ground.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(60)]
    public sealed class CampBillboard : MonoBehaviour
    {
        public Texture2D picture;
        [Tooltip("板の高さ(m)")] public float height = 1.66f;
        public CampBuilder builder;
        [Tooltip("足もとの高さの調整")] public float footOffset = -0.02f;

        Mesh _mesh;
        Texture2D _builtFor;
        float _builtHeight;

        void OnEnable() => BuildMesh();

        void OnDisable()
        {
            if (_mesh) DestroyImmediate(_mesh);
            _mesh = null;
        }

        void BuildMesh()
        {
            float aspect = picture ? picture.width / (float)picture.height : 0.35f;
            float w = height * aspect, h = height;
            if (!_mesh) _mesh = new Mesh { name = "Iori board", hideFlags = HideFlags.DontSave };
            _mesh.Clear();
            _mesh.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(-w / 2, h / 2, 0) };
            _mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            var r = GetComponent<MeshRenderer>();
            if (r.sharedMaterial && picture) r.sharedMaterial.SetTexture("_MainTex", picture);
            _builtFor = picture;
            _builtHeight = height;
        }

        void LateUpdate() => Place();

        public void Place()
        {
            if (_builtFor != picture || !Mathf.Approximately(_builtHeight, height) || !_mesh) BuildMesh();
            var parent = transform.parent;
            Vector3 basePos = parent ? parent.position : transform.position;
            float gy = builder ? builder.GroundH(basePos.x, -basePos.z) : 0f;
            transform.position = new Vector3(basePos.x, gy + height / 2 + footOffset, basePos.z);
            var cam = Camera.main;
            if (!cam) return;
            var d = transform.position - cam.transform.position;
            d.y = 0;
            if (d.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
    }
}
