using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Iori.Audio
{
    /// <summary>
    /// 今の人形(Iori.Puppet.PuppetRenderer、口のコマ差し替え)を <see cref="IMouthTarget"/> としてつなぐ仮の橋。
    /// 人形のコードは別の作業(調整中)にあるので、型を名前で探し、外から入れる口の値(mouthOpen と vowel)だけを書く。
    /// 人形のパーツや動きの値には触らない。人形がシーンに無ければ何もしない。
    /// 人形のコードが本線に入ったら、人形の側で IMouthTarget を実装して、この橋は消してよい。
    /// </summary>
    public sealed class PuppetMouthLink : MonoBehaviour
    {
        const string PuppetTypeName = "Iori.Puppet.PuppetRenderer";
        const float SearchInterval = 2f;

        sealed class Target : IMouthTarget
        {
            public Component Puppet;
            public FieldInfo OpenField, VowelField;

            public void SetMouth(float open, float vowel)
            {
                if (!Puppet) return;
                OpenField.SetValue(Puppet, open);
                VowelField?.SetValue(Puppet, vowel);
            }

            public void ReleaseMouth()
            {
                if (!Puppet) return;
                OpenField.SetValue(Puppet, 0f);
                VowelField?.SetValue(Puppet, 0f);
            }
        }

        readonly List<Target> _targets = new List<Target>();
        System.Type _type;
        bool _typeLooked;
        float _nextSearch;

        /// <summary>つないでいる人形の数。</summary>
        public int Count => _targets.Count;

        void Update()
        {
            if (Time.unscaledTime < _nextSearch) return;
            _nextSearch = Time.unscaledTime + SearchInterval;
            // 消えた人形を外す
            for (var i = _targets.Count - 1; i >= 0; i--)
                if (!_targets[i].Puppet) { IoriMouth.Unregister(_targets[i]); _targets.RemoveAt(i); }

            var type = PuppetType();
            if (type == null) return;
            foreach (var o in FindObjectsByType(type, FindObjectsSortMode.None))
            {
                var c = o as Component;
                if (!c || _targets.Exists(t => t.Puppet == c)) continue;
                var open = type.GetField("mouthOpen", BindingFlags.Public | BindingFlags.Instance);
                if (open == null || open.FieldType != typeof(float)) continue;
                var vowel = type.GetField("vowel", BindingFlags.Public | BindingFlags.Instance);
                var t = new Target { Puppet = c, OpenField = open, VowelField = vowel != null && vowel.FieldType == typeof(float) ? vowel : null };
                _targets.Add(t);
                IoriMouth.Register(t);
            }
        }

        System.Type PuppetType()
        {
            if (_typeLooked) return _type;
            _typeLooked = true;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(PuppetTypeName, false);
                if (t != null && typeof(Component).IsAssignableFrom(t)) { _type = t; break; }
            }
            return _type;
        }

        void OnDestroy()
        {
            foreach (var t in _targets) IoriMouth.Unregister(t);
            _targets.Clear();
        }
    }
}
