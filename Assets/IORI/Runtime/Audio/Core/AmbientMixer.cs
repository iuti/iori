using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>
    /// 環境音(焚き火、雨、風など)の個別音量ミキサー。
    /// ユーザーが決めた音量を覚えておき、天気に応じて実際に鳴らす音量を出す。
    /// 例: 雨音は雨の日だけ鳴り、晴れの日はユーザー音量に関係なく 0 になる。
    /// </summary>
    public sealed class AmbientMixer
    {
        readonly List<AmbientDef> _defs;
        readonly Dictionary<string, float> _userVolumes = new Dictionary<string, float>();
        readonly HashSet<string> _muted = new HashSet<string>();

        public float Master { get; private set; } = 1f;

        public AmbientMixer(IEnumerable<AmbientDef> defs)
        {
            _defs = new List<AmbientDef>(defs);
            foreach (var d in _defs) _userVolumes[d.Id] = Clamp01(d.DefaultVolume);
        }

        public IReadOnlyList<AmbientDef> Layers => _defs;

        public float GetUserVolume(string id) => _userVolumes.TryGetValue(id, out var v) ? v : 0f;

        public void SetUserVolume(string id, float volume)
        {
            if (!_userVolumes.ContainsKey(id)) throw new ArgumentException("未定義の環境音です: " + id);
            _userVolumes[id] = Clamp01(volume);
        }

        public bool IsMuted(string id) => _muted.Contains(id);

        public void SetMuted(string id, bool muted)
        {
            if (!_userVolumes.ContainsKey(id)) throw new ArgumentException("未定義の環境音です: " + id);
            if (muted) _muted.Add(id); else _muted.Remove(id);
        }

        public void SetMaster(float volume) => Master = Clamp01(volume);

        /// <summary>この天気で実際に鳴らす音量。</summary>
        public float EffectiveVolume(string id, string weather)
        {
            var def = _defs.Find(d => d.Id == id);
            if (def == null) return 0f;
            if (!def.IsActiveIn(weather) || _muted.Contains(id)) return 0f;
            return Clamp01(_userVolumes[id] * Master);
        }

        public List<AmbientLayerState> BuildStates(string weather)
        {
            var result = new List<AmbientLayerState>();
            foreach (var d in _defs)
                result.Add(new AmbientLayerState { Id = d.Id, File = d.File, Volume = EffectiveVolume(d.Id, weather) });
            return result;
        }

        /// <summary>保存用の文字列。未知の id は読み込み時に無視する。</summary>
        public string Save()
        {
            var layers = new Dictionary<string, object>();
            foreach (var kv in _userVolumes)
                layers[kv.Key] = new Dictionary<string, object>
                {
                    ["volume"] = (double)kv.Value,
                    ["muted"] = _muted.Contains(kv.Key),
                };
            return Json.Write(new Dictionary<string, object> { ["master"] = (double)Master, ["layers"] = layers });
        }

        public void Load(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return;
            Dictionary<string, object> root;
            try { root = JsonRead.Obj(Json.Parse(saved), "mixer"); }
            catch (FormatException) { return; } // 壊れた保存データは無視して既定値のまま
            Master = Clamp01((float)JsonRead.Num(root, "master", 1));
            if (!root.TryGetValue("layers", out var layersObj) || !(layersObj is Dictionary<string, object> layers)) return;
            foreach (var kv in layers)
            {
                if (!_userVolumes.ContainsKey(kv.Key) || !(kv.Value is Dictionary<string, object> o)) continue;
                _userVolumes[kv.Key] = Clamp01((float)JsonRead.Num(o, "volume", _userVolumes[kv.Key]));
                if (o.TryGetValue("muted", out var m) && m is bool muted && muted) _muted.Add(kv.Key);
                else _muted.Remove(kv.Key);
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
