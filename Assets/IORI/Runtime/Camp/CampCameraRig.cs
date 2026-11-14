using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// The camp camera: framed close on the fire, Iori and the props (portrait first, landscape works
    /// too). It drifts a little on its own, can be dragged around the scene a bit (left-right more,
    /// up-down a little), pinched or scrolled closer, and moves in on a prop when it is tapped.
    /// It also tells the scene what it is focused on, for the depth of field.
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

        public Framing portrait = new Framing { fov = 42, distance = 4.70f, tilt = 0.113f, focusDistance = 2.6f, target = new Vector3(0f, 0.95f, 0f) };
        public Framing landscape = new Framing { fov = 30, distance = 4.9f, tilt = 0.10f, focusDistance = 3.0f, target = new Vector3(0f, 0.95f, 0f) };

        [Header("動き")]
        public bool autoSway = true;
        [Tooltip("左右に回り込める範囲(ラジアン)")] public float yawLimit = 0.42f;
        [Tooltip("上下に動かせる範囲(ラジアン)")] public float pitchLimit = 0.07f;
        [Tooltip("寄れる範囲(1 がいつもの距離)")] public Vector2 zoomRange = new Vector2(0.72f, 1.1f);
        public float dragSensitivity = 1.2f;
        public float follow = 3.2f;

        [Header("撮影用")]
        public float yaw;
        public float pitch;
        public float zoom = 1f;
        public CampProp focus;

        Camera _cam;
        Vector3 _camT;
        float _camR;
        bool _init;

        // input
        Vector2 _downPos;
        float _downYaw, _downPitch;
        bool _dragging, _down;
        float _pinch;

        /// <summary>Set by the debug panel while the pointer is over it, so taps there do not move the camera.</summary>
        public static bool InputBlocked;

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
            float wantR = fr.distance * Mathf.Clamp(zoom, zoomRange.x, zoomRange.y);
            float close = 0f;
            if (focus)
            {
                var f = focus.FocusPoint;
                bool iori = focus.kind == CampPropKind.Iori;
                wantT = new Vector3(Mathf.Lerp(fr.target.x, f.x, .85f), iori ? f.y + 0.2f : Mathf.Max(.35f, f.y + .05f), Mathf.Lerp(fr.target.z, f.z, .85f));
                wantR = fr.focusDistance * (iori ? 1.05f : 1f) + Vector3.Distance(f, fr.target) * 0.25f;
                close = 1f;
            }
            if (!_init || snap) { _camT = wantT; _camR = wantR; _init = true; }
            else
            {
                float k = 1 - Mathf.Exp(-dt * follow);
                _camT = Vector3.Lerp(_camT, wantT, k);
                _camR += (wantR - _camR) * k;
            }
            float y = yaw + (autoSway ? Mathf.Sin(t * .13f) * (focus ? .04f : .07f) : 0f);
            float p = fr.tilt + pitch + (autoSway ? Mathf.Sin(t * .09f) * .006f : 0f);
            _cam.fieldOfView = fr.fov;
            var pos = new Vector3(_camT.x + _camR * Mathf.Sin(y) * Mathf.Cos(p), Mathf.Max(.3f, _camT.y + _camR * Mathf.Sin(p)), _camT.z - _camR * Mathf.Cos(y) * Mathf.Cos(p));
            transform.position = pos;
            transform.LookAt(_camT);
            if (atm) atm.SetFocus(_camR, close * Mathf.Clamp01((fr.distance - _camR) / Mathf.Max(0.01f, fr.distance - fr.focusDistance)));
        }

        void HandleInput()
        {
            if (InputBlocked) { _down = false; _dragging = false; return; }
            if (Input.touchCount >= 2)
            {
                float d = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
                if (_pinch > 0f) zoom = Mathf.Clamp(zoom * (_pinch / Mathf.Max(1f, d)), zoomRange.x, zoomRange.y);
                _pinch = d;
                _down = false; _dragging = false;
                return;
            }
            _pinch = 0f;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f) zoom = Mathf.Clamp(zoom * (1f - wheel * 0.06f), zoomRange.x, zoomRange.y);

            if (Input.GetMouseButtonDown(0))
            {
                _down = true; _dragging = false;
                _downPos = Input.mousePosition; _downYaw = yaw; _downPitch = pitch;
            }
            if (_down && Input.GetMouseButton(0))
            {
                Vector2 d = (Vector2)Input.mousePosition - _downPos;
                if (d.magnitude > 8f) _dragging = true;
                if (_dragging)
                {
                    yaw = Mathf.Clamp(_downYaw + d.x / Mathf.Max(1, Screen.width) * dragSensitivity, -yawLimit, yawLimit);
                    pitch = Mathf.Clamp(_downPitch - d.y / Mathf.Max(1, Screen.height) * dragSensitivity * 0.5f, -pitchLimit, pitchLimit);
                }
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
