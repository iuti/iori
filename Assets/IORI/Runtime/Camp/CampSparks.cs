using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// The mesh behind the fire's sparks: a few quads whose "positions" are just random numbers.
    /// IoriSparks.shader works out where each spark is from the time, so nothing is simulated here.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CampSparks : MonoBehaviour
    {
        [Range(4, 200)] public int count = 28;
        public int seed = 7;

        Mesh _mesh;
        int _built;

        void OnEnable() => Build();

        void OnDisable()
        {
            if (_mesh) DestroyImmediate(_mesh);
            _mesh = null;
        }

        void Update()
        {
            if (_built != count || !_mesh) Build();
        }

        void Build()
        {
            if (!_mesh) _mesh = new Mesh { name = "Camp sparks", hideFlags = HideFlags.DontSave };
            var rng = new System.Random(seed);
            var v = new Vector3[count * 4];
            var uv = new Vector2[count * 4];
            var tri = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                var s = new Vector3((float)rng.NextDouble(), (i + 0.5f) / count, (float)rng.NextDouble());
                for (int k = 0; k < 4; k++) v[i * 4 + k] = s;
                uv[i * 4] = new Vector2(-1, -1); uv[i * 4 + 1] = new Vector2(1, -1); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(-1, 1);
                int b = i * 4, t = i * 6;
                tri[t] = b; tri[t + 1] = b + 2; tri[t + 2] = b + 1; tri[t + 3] = b; tri[t + 4] = b + 3; tri[t + 5] = b + 2;
            }
            _mesh.Clear();
            _mesh.vertices = v;
            _mesh.uv = uv;
            _mesh.triangles = tri;
            // the sparks fly well away from where the mesh says they are
            _mesh.bounds = new Bounds(new Vector3(0, 1.2f, 0), new Vector3(3f, 3.5f, 3f));
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _built = count;
        }
    }
}
