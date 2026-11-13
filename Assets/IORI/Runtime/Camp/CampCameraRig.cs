using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// The camp camera: framed close on the fire, Iori and the props (portrait first, landscape works too),
    /// sways a little on its own, can be dragged left and right a bit, and moves closer to a prop
    /// when it is tapped. Values are the mock's.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(50)]
    public sealed class CampCameraRig : MonoBehaviour
    {
        [System.Serializable]
        public struct Framing
        {
            public float fov, distance, tilt, focusDistance;
            public Vector3 target;
        }

        public Framing portrait = new Framing { fov = 50, distance = 3.85f, tilt = 0.118f, focusDistance = 2.8f, target = new Vector3(0.04f, 0.93f, -0.2f) };
        public Framing landscape = new Framing { fov = 35, distance = 4.9f, tilt = 0.09f, focusDistance = 3.3f, target = new Vector3(0.05f, 0.95f, -0.2f) };

        [Header("動き")]
        public bool autoSway = true;
        [Tooltip("左右に振れる範囲(ラジアン)")] public float yawLimit = 0.45f;
        public float dragSensitivity = 1.2f;
        public float follow = 3.2f;

        [Header("撮影用")]
        public float yaw;
        public float pitch;
        public CampProp focus;

        Camera _cam;
        Vector3 _camT;
        float _camR;
        bool _init;

        // input
        Vector2 _downPos;
        float _downYaw;
        bool _dragging, _down;

        void OnEnable() { _cam = GetComponent<Camera>(); _init = false; }

        Framing Current => (_cam && _cam.aspect < 1f) ? portrait : landscape;

        public void Snap() { _init = false; Step(0f, true); }

        void LateUpdate()
        {
            if (Application.isPlaying) HandleInput();
            Step(Application.isPlaying ? Time.deltaTime : 0.016f, !Application.isPlaying);
        }

        void Step(float dt, bool snap)
        {
            if (!_cam) _cam = GetComponent<Camera>();
            var fr = Current;
            var atm = CampAtmosphere.Instance;
            float t = atm ? atm.ShaderTime : Time.time;
            Vector3 wantT = fr.target;
            float wantR = fr.distance;
            if (focus)
            {
                var f = focus.FocusPoint;
                bool iori = focus.kind == CampPropKind.Iori;
                wantT = new Vector3(fr.target.x + (f.x - fr.target.x) * .8f, iori ? 1.28f : Mathf.Max(.6f, f.y + .15f), fr.target.z + (f.z - fr.target.z) * .8f);
                wantR = fr.focusDistance * (iori ? .9f : 1f);
            }
            if (!_init || snap) { _camT = wantT; _camR = wantR; _init = true; }
            else
            {
                float k = 1 - Mathf.Exp(-dt * follow);
                _camT = Vector3.Lerp(_camT, wantT, k);
                _camR += (wantR - _camR) * k;
            }
            float y = yaw + (autoSway ? Mathf.Sin(t * .13f) * (focus ? .05f : .1f) : 0f);
            float p = fr.tilt + pitch + (autoSway ? Mathf.Sin(t * .09f) * .008f : 0f);
            _cam.fieldOfView = fr.fov;
            var pos = new Vector3(_camT.x + _camR * Mathf.Sin(y) * Mathf.Cos(p), Mathf.Max(.3f, _camT.y + _camR * Mathf.Sin(p)), _camT.z - _camR * Mathf.Cos(y) * Mathf.Cos(p));
            transform.position = pos;
            transform.LookAt(_camT);
        }

        void HandleInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _down = true; _dragging = false;
                _downPos = Input.mousePosition; _downYaw = yaw;
            }
            if (_down && Input.GetMouseButton(0))
            {
                Vector2 d = (Vector2)Input.mousePosition - _downPos;
                if (d.magnitude > 8f) _dragging = true;
                if (_dragging) yaw = Mathf.Clamp(_downYaw + d.x / Mathf.Max(1, Screen.width) * dragSensitivity, -yawLimit, yawLimit);
            }
            if (_down && Input.GetMouseButtonUp(0))
            {
                _down = false;
                if (!_dragging) Tap(Input.mousePosition);
            }
        }

        void Tap(Vector2 screen)
        {
            var ray = _cam.ScreenPointToRay(screen);
            CampProp hit = null;
            if (Physics.Raycast(ray, out var info, 100f, ~0, QueryTriggerInteraction.Collide))
                hit = info.collider.GetComponentInParent<CampProp>();
            if (hit)
            {
                if (hit.focusOnTap) focus = focus == hit ? null : hit;
                hit.Tap();
            }
            else focus = null;
        }
    }
}
