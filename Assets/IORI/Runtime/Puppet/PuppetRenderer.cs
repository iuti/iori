using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Iori.Puppet
{
    /// <summary>
    /// Moves the puppet and draws it, layer over layer, into two textures (her colors, her normals), then
    /// hands those to the board this sits on in place of its still picture. Everything else about the board
    /// stays as it is: the scene's light, the shadow she casts, the focus blur. On a phone this also keeps
    /// the cost down: the layers overlap only inside the small texture, and the screen draws one board.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    [DefaultExecutionOrder(50)]
    public sealed class PuppetRenderer : MonoBehaviour
    {
        public PuppetAsset asset;
        [Tooltip("レイヤーを描くシェーダー(IORI/Puppet/Layer)")] public Shader layerShader;
        [Tooltip("描きためる絵の長い辺(px)。小さいほど軽い")] public int maxSize = 1024;
        [Tooltip("待機中の動き(まばたき、呼吸、視線、首のかしげ、体の揺れ)")] public bool idle = true;
        [Tooltip("頭と体の動き全体の大きさ(下の 3 つに掛かる)")] [Range(0f, 2f)] public float amount = 1f;
        [Tooltip("首を向ける、うなずく大きさ。4.5 あたりが、絵が崩れない上限")] [Range(0f, 6f)] public float turn = 4.5f;
        [Tooltip("首をかしげる大きさ。3.5 あたりが、絵が崩れない上限")] [Range(0f, 5f)] public float tilt = 3.5f;
        [Tooltip("体を揺らす、傾ける大きさ。1.5 あたりが、絵が崩れない上限")] [Range(0f, 3f)] public float lean = 1.5f;
        [Tooltip("髪と揺れものの揺れの大きさ。1 あたりが、絵が崩れない上限")] [Range(0f, 2f)] public float swing = 1f;
        [Tooltip("口の開き(声に合わせて外から入れる)")] [Range(0f, 1f)] public float mouthOpen;
        [Tooltip("口の形(-1: お、う / 1: い、え)")] [Range(-1f, 1f)] public float vowel;
        [Tooltip("ほほえみ")] [Range(0f, 1f)] public float smile;
        [Tooltip("眠っている")] [Range(0f, 1f)] public float asleep;
        [Tooltip("動きの乱数の種")] public int seed = 1;

        public PuppetRig Rig => _rig;
        public PuppetIdle Idle => _idle;
        public RenderTexture ColorTexture => _color;
        public RenderTexture NormalTexture => _normal;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex"), NormalTexId = Shader.PropertyToID("_NormalTex");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha"), StencilRefId = Shader.PropertyToID("_StencilRef"), VpId = Shader.PropertyToID("_PuppetVP");
        static readonly int TexelId = Shader.PropertyToID("_MainTex_TexelSize"), BoardId = Shader.PropertyToID("_IoriPuppetBoard");

        PuppetRig _rig;
        PuppetIdle _idle;
        PuppetAsset _builtFor;
        int _builtSize, _builtSeed;
        RenderTexture _color, _normal;
        readonly RenderBuffer[] _targets = new RenderBuffer[2];
        Mesh[] _meshes;
        Vector3[][] _verts;
        float[][] _sent;
        int[] _sentAt;
        int _drawing;
        Material _plain;
        readonly Dictionary<int, Material> _stencil = new Dictionary<int, Material>();
        Texture2D _flat;
        MaterialPropertyBlock _block;
        MeshRenderer _board;

        void OnEnable() => Build();

        void OnDisable() => Release();

        void OnValidate()
        {
            if (maxSize < 64) maxSize = 64;
        }

        void Release()
        {
            if (_board) _board.SetPropertyBlock(null);
            if (_rig != null) Shader.SetGlobalFloat(BoardId, 0f);
            if (_color) { _color.Release(); DestroyImmediate(_color); }
            if (_normal) { _normal.Release(); DestroyImmediate(_normal); }
            if (_meshes != null) foreach (var m in _meshes) if (m) DestroyImmediate(m);
            if (_plain) DestroyImmediate(_plain);
            foreach (var m in _stencil.Values) if (m) DestroyImmediate(m);
            _stencil.Clear();
            if (_flat) DestroyImmediate(_flat);
            _color = _normal = null; _meshes = null; _verts = null; _sent = null; _sentAt = null; _plain = null; _flat = null;
            _rig = null; _idle = null; _builtFor = null;
        }

        bool Build()
        {
            Release();
            _board = GetComponent<MeshRenderer>();
            if (!asset || !asset.model || !layerShader) return false;
            PuppetModel model;
            try { model = PuppetModel.Parse(asset.model.text); }
            catch (System.Exception e) { Debug.LogError("[IORI] puppet: " + e.Message, this); return false; }
            if (asset.layers.Length != model.layers.Length) { Debug.LogError("[IORI] puppet: the asset's layers do not match its model.json (取り込み直す)", this); return false; }

            var occ = new byte[model.layers.Length][];
            for (int i = 0; i < occ.Length; i++)
            {
                PuppetRig.GridOf(model, model.layers[i], out _, out _, out int cols, out int rows);
                var o = asset.layers[i].occupancy;
                occ[i] = o != null && o.Length == cols * rows ? o : null;
            }
            _rig = new PuppetRig(model, occ);
            _idle = new PuppetIdle(seed);

            int n = _rig.layers.Length;
            _meshes = new Mesh[n]; _verts = new Vector3[n][]; _sent = new float[n][]; _sentAt = new int[n];
            for (int i = 0; i < n; i++)
            {
                var L = _rig.layers[i];
                int count = L.rest.Length / 2;
                var mesh = new Mesh { name = "puppet " + L.def.id, hideFlags = HideFlags.HideAndDontSave };
                if (count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.MarkDynamic();
                var v = new Vector3[count]; var uv = new Vector2[count];
                for (int k = 0; k < count; k++)
                {
                    v[k] = new Vector3(L.rest[2 * k], L.rest[2 * k + 1], 0f);
                    uv[k] = new Vector2(L.uv[2 * k], 1f - L.uv[2 * k + 1]);    // the model counts down from the top
                }
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(L.tris, 0, false);
                _meshes[i] = mesh; _verts[i] = v;
            }

            float scale = Mathf.Min(1f, maxSize / (float)Mathf.Max(model.canvasW, model.canvasH));
            int w = Mathf.Max(16, Mathf.RoundToInt(model.canvasW * scale)), h = Mathf.Max(16, Mathf.RoundToInt(model.canvasH * scale));
            _color = Target("puppet color", w, h, 24);
            _normal = Target("puppet normals", w, h, 0);

            _plain = new Material(layerShader) { hideFlags = HideFlags.HideAndDontSave };
            _flat = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            _flat.SetPixel(0, 0, new Color(0.5f, 0.5f, 1f, 1f)); _flat.Apply();
            _block = new MaterialPropertyBlock();
            _builtFor = asset; _builtSize = maxSize; _builtSeed = seed;
            Step(0f); Draw(); Bind();
            return true;
        }

        static RenderTexture Target(string name, int w, int h, int depth)
        {
            // the colors are kept as the numbers in the layers' files (see IoriPuppetLayer.shader): no conversion on the way in or out
            var rt = new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = name, hideFlags = HideFlags.HideAndDontSave, useMipMap = true, autoGenerateMips = false,
                filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, antiAliasing = 1,
            };
            rt.Create();
            return rt;
        }

        bool Ready()
        {
            if (_rig == null || _builtFor != asset || _builtSize != maxSize || _builtSeed != seed || !_color || !_normal || !_plain) return Build();
            if (!_color.IsCreated() || !_normal.IsCreated()) { _color.Create(); _normal.Create(); }
            return true;
        }

        void LateUpdate()
        {
            if (!Ready()) return;
            // in the editor, not playing: she holds still as drawn (and stays so in the pictures CampSetup takes, unless it asks)
            Step(Application.isPlaying ? Time.deltaTime : 0f);
            Draw();
            Bind();
        }

        /// <summary>From rest, plays `seconds` of her idle motion and draws that moment (for still pictures:
        /// the same number always gives the same pose).</summary>
        public void RenderAt(float seconds)
        {
            if (!Ready()) return;
            var P = _rig.param;
            P.angleX = P.angleY = P.angleZ = P.eyeBallX = P.eyeBallY = P.bodyX = P.bodyZ = P.breath = 0f;
            P.eyeLOpen = P.eyeROpen = 1f;
            _rig.time = 0f;
            _rig.ResetPhysics(); _rig.ResetMouth();
            _idle.Reset();
            const float dt = 1f / 30f;
            int whole = Mathf.FloorToInt(seconds / dt + 1e-4f);
            for (int i = 0; i < whole; i++) Step(dt);
            float rest = seconds - whole * dt;      // so that pictures taken at any rate are evenly apart in her motion
            if (rest > 1e-4f) Step(rest);
            Draw();
            Bind();
        }

        /// <summary>From rest, holds a pose for `seconds` (no idle motion; `pose` writes the head, eyes, mouth and
        /// body it wants every step) and draws that moment. A short time shows the hair still catching up with
        /// the head, a long one shows it settled. For looking at one pose closely.</summary>
        public void RenderPose(System.Action<PuppetParams> pose, float seconds)
        {
            if (!Ready()) return;
            var P = _rig.param;
            P.angleX = P.angleY = P.angleZ = P.eyeBallX = P.eyeBallY = P.bodyX = P.bodyZ = P.breath = 0f;
            P.eyeLOpen = P.eyeROpen = 1f;
            P.mouthOpen = P.vowel = P.smile = 0f;
            _rig.time = 0f;
            _rig.ResetPhysics(); _rig.ResetMouth();
            _idle.Reset();
            _rig.physicsScale = swing;
            const float dt = 1f / 30f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(seconds / dt));
            for (int i = 0; i < steps; i++) { pose?.Invoke(P); _rig.Update(dt); }
            Draw();
            Bind();
        }

        void Step(float dt)
        {
            var P = _rig.param;
            _idle.amount = amount; _idle.turn = turn; _idle.tilt = tilt; _idle.lean = lean; _idle.asleep = asleep;
            if (idle) _idle.Apply(P, dt);
            P.mouthOpen = mouthOpen; P.vowel = vowel; P.smile = smile;
            _rig.physicsScale = swing;
            _rig.Update(dt);
        }

        Material StencilMaterial(int value)
        {
            if (!_stencil.TryGetValue(value, out var m) || !m)
            {
                m = new Material(layerShader) { hideFlags = HideFlags.HideAndDontSave };
                m.SetFloat(StencilRefId, value);
                _stencil[value] = m;
            }
            return m;
        }

        void Draw()
        {
            var model = _rig.model;
            var prev = RenderTexture.active;
            _targets[0] = _color.colorBuffer; _targets[1] = _normal.colorBuffer;
            Graphics.SetRenderTarget(_targets, _color.depthBuffer);
            GL.Clear(true, true, Color.clear);
            // model px, y down, onto the whole texture
            var proj = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0f, model.canvasW, model.canvasH, 0f, -1f, 1f), true);
            Shader.SetGlobalMatrix(VpId, proj);
            _drawing++;
            foreach (var d in _rig.draws)
            {
                if (d.alpha <= 0f) continue;
                var L = _rig.layers[d.layer]; var A = asset.layers[d.layer];
                Texture2D tex = A.main, normal = A.normal;
                if (d.texture == PuppetTexture.Frame && d.frame < A.frames.Length)
                {
                    tex = A.frames[d.frame];
                    normal = d.frame < A.frameNormals.Length ? A.frameNormals[d.frame] : null;
                }
                else if (d.texture == PuppetTexture.Mask) tex = A.mask;
                if (!tex) continue;

                // the mesh, if this layer's points may have moved since they were last sent
                if (_sent[d.layer] != d.positions || (!L.still && _sentAt[d.layer] != _drawing))
                {
                    var v = _verts[d.layer]; var p = d.positions;
                    for (int k = 0; k < v.Length; k++) { v[k].x = p[2 * k]; v[k].y = p[2 * k + 1]; }
                    _meshes[d.layer].SetVertices(v);
                    _sent[d.layer] = p; _sentAt[d.layer] = _drawing;
                }

                var m = d.stencil == PuppetStencil.None ? _plain : StencilMaterial(d.stencilRef);
                m.SetTexture(MainTexId, tex);
                m.SetTexture(NormalTexId, normal ? normal : _flat);
                m.SetFloat(AlphaId, d.alpha);
                int pass = d.stencil == PuppetStencil.Write ? 1 : d.stencil == PuppetStencil.Test ? 2 : 0;
                if (m.SetPass(pass)) Graphics.DrawMeshNow(_meshes[d.layer], Matrix4x4.identity);
            }
            RenderTexture.active = prev;
            _color.GenerateMips(); _normal.GenerateMips();
        }

        /// <summary>The board shows what was just drawn, in place of its still picture.</summary>
        void Bind()
        {
            if (!_board) return;
            var model = _rig.model;
            _board.GetPropertyBlock(_block);
            _block.SetTexture(MainTexId, _color);
            _block.SetTexture(NormalTexId, _normal);
            // the rims are measured in the drawing's own pixels, whatever size it is kept at here
            _block.SetVector(TexelId, new Vector4(1f / model.canvasW, 1f / model.canvasH, model.canvasW, model.canvasH));
            _board.SetPropertyBlock(_block);
            // how the board's shader is to read them (color times alpha, encoded for the screen). Said to all shaders
            // rather than to this board alone: there is one Iori, and every view of her (the mirrors too) agrees
            Shader.SetGlobalFloat(BoardId, 1f);
        }
    }
}
