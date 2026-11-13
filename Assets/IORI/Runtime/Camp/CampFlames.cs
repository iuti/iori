using UnityEngine;

namespace Iori.Camp
{
    /// <summary>Flickers the flame meshes (stretch and lean), and shrinks the fire when it burns low.</summary>
    [ExecuteAlways]
    public sealed class CampFlames : MonoBehaviour
    {
        Vector3[] _scale;
        Quaternion[] _rot;

        public void Capture()
        {
            int n = transform.childCount;
            _scale = new Vector3[n];
            _rot = new Quaternion[n];
            for (int i = 0; i < n; i++) { _scale[i] = transform.GetChild(i).localScale; _rot[i] = transform.GetChild(i).localRotation; }
        }

        void LateUpdate() => Animate();

        public void Animate()
        {
            if (_scale == null || _scale.Length != transform.childCount) Capture();
            var atm = CampAtmosphere.Instance;
            float t = atm ? atm.ShaderTime : Time.time;
            // smaller fire at dawn/day and in sleep mode, a little smaller in rain
            float size = 1f;
            if (atm)
            {
                var glow = Shader.GetGlobalVector("_IoriGlowA");
                size = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(glow.x));
            }
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                float ph = i * 1.7f;
                float w = Mathf.Sin(t * 7f + ph) * .5f + Mathf.Sin(t * 12.3f + ph * 2f) * .5f;
                var s = _scale[i] * size;
                c.localScale = new Vector3(s.x * (1 - .05f * w), s.y * (1 + .13f * w), s.z * (1 - .05f * w));
                c.localRotation = _rot[i] * Quaternion.Euler(0, 0, 4f * Mathf.Sin(t * 4f + ph));
            }
        }
    }
}
