using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// The mesh behind the rain: thin quads spread through a box over the camp. IoriRain.shader
    /// makes them fall, wrap around and lean with the wind. Hidden while it is not raining.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CampRain : MonoBehaviour
    {
        [Range(100, 4000)] public int count = 1500;
        public Vector3 center = new Vector3(0f, 0f, 3.5f);
        public Vector3 size = new Vector3(16f, 7f, 18f);
        public int seed = 11;

        Mesh _mesh;
        int _built;
        MeshRenderer _renderer;

        void OnEnable() { _renderer = GetComponent<MeshRenderer>(); Build(); }

        void OnDisable()
        {
            if (_mesh) DestroyImmediate(_mesh);
            _mesh = null;
        }

        void LateUpdate() => Refresh();

        /// <summary>Shows or hides the rain for the current weather (called every frame; by hand when taking stills).</summary>
        public void Refresh()
        {
            if (!_renderer) _renderer = GetComponent<MeshRenderer>();
            if (_built != count || !_mesh) Build();
            var atm = CampAtmosphere.Instance;
            bool show = atm && atm.Rain > 0.01f;
            if (_renderer && _renderer.enabled != show) _renderer.enabled = show;
        }

        void Build()
        {
            if (!_mesh) _mesh = new Mesh { name = "Camp rain", hideFlags = HideFlags.DontSave };
            var rng = new System.Random(seed);
            var v = new Vector3[count * 4];
            var uv = new Vector2[count * 4];
            var data = new Vector2[count * 4];
            var tri = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                // more streaks close to the camera, where they read as rain; fewer far away
                float depth = (float)rng.NextDouble(); depth *= depth;
                var p = new Vector3(center.x + ((float)rng.NextDouble() - 0.5f) * size.x, (float)rng.NextDouble() * size.y, center.z - size.z * 0.5f + depth * size.z);
                var d = new Vector2(7.5f + (float)rng.NextDouble() * 4f, (i + 0.5f) / count);
                for (int k = 0; k < 4; k++) { v[i * 4 + k] = p; data[i * 4 + k] = d; }
                uv[i * 4] = new Vector2(-1, -1); uv[i * 4 + 1] = new Vector2(1, -1); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(-1, 1);
                int b = i * 4, t = i * 6;
                tri[t] = b; tri[t + 1] = b + 2; tri[t + 2] = b + 1; tri[t + 3] = b; tri[t + 4] = b + 3; tri[t + 5] = b + 2;
            }
            _mesh.Clear();
            _mesh.indexFormat = count * 4 > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.vertices = v;
            _mesh.uv = uv;
            _mesh.uv2 = data;
            _mesh.triangles = tri;
            _mesh.bounds = new Bounds(center + new Vector3(0, size.y * 0.5f, 0), size + new Vector3(4, 2, 4));
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _built = count;
        }
    }
}
