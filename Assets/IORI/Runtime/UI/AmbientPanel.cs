using System;
using System.Collections.Generic;
using Iori.Audio;
using TMPro;
using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// The fire's panel: the sounds around the camp (fire, wind, insects, rain), one slider each.
    /// A sound that is not heard in this weather (rain while it is clear) is greyed but can still be set.
    /// </summary>
    public sealed class AmbientPanel : UiPanel
    {
        static readonly (string id, string label, string icon)[] Known =
        {
            ("fire", "焚き火", "fire"), ("wind", "風", "wind"), ("insects", "虫", "leaf"), ("rain", "雨", "rain"),
        };

        sealed class Row
        {
            public AmbientDef def;
            public UnityEngine.UI.RawImage icon;
            public TextMeshProUGUI label;
            public UiSlider slider;
            public bool warm;
            public int shown = -1;
        }

        public Action requestClose;
        readonly List<Row> _rows = new List<Row>();

        static int LayerCount
        {
            get
            {
                var radio = RadioDirector.Instance;
                return radio && radio.Mixer != null ? radio.Mixer.Layers.Count : Known.Length;
            }
        }

        static bool AnyWeatherBound
        {
            get
            {
                var radio = RadioDirector.Instance;
                if (!radio || radio.Mixer == null) return true;
                foreach (var l in radio.Mixer.Layers) if (l.Weathers.Count > 0) return true;
                return false;
            }
        }

        public override float Height => 60f + 58f * LayerCount + (AnyWeatherBound ? 39f : 12f);

        protected override void Build()
        {
            var th = UiTheme.Current;
            Header("まわりの音", () => requestClose?.Invoke());
            var radio = RadioDirector.Instance;
            if (!radio || radio.Mixer == null) return;

            // the layers the UI knows come first, in its own order; any other after them
            var layers = new List<AmbientDef>();
            foreach (var k in Known)
                foreach (var l in radio.Mixer.Layers)
                    if (l.Id == k.id) layers.Add(l);
            foreach (var l in radio.Mixer.Layers) if (!layers.Contains(l)) layers.Add(l);

            float y = 60f;
            foreach (var def in layers)
            {
                string label = def.Id, icon = "wind";
                foreach (var k in Known) if (k.id == def.Id) { label = k.label; icon = k.icon; }
                var row = new Row { def = def, warm = def.Id == "fire" };
                row.icon = UiKit.Icon("Icon " + def.Id, Content, 22, y + 13, 22, icon, th.text);
                row.label = UiKit.Text("Label " + def.Id, Content, 58, y + 13.5f, 70, 21, label, UiFonts.Medium, 15, th.text);
                row.slider = UiSlider.Create("Slider " + def.Id, Content, 130, y + 2, 218);
                string id = def.Id;
                row.slider.onChanged = v => { if (RadioDirector.Instance) RadioDirector.Instance.SetAmbientVolume(id, v); };
                _rows.Add(row);
                y += 58f;
            }
            if (AnyWeatherBound)
                UiKit.Text("Note", Content, 58, y, 290, 19, "雨の音は、雨が降っているときだけ鳴る", UiFonts.Regular, 12.5f, th.textSoft);
        }

        protected override void OnOpened()
        {
            var radio = RadioDirector.Instance;
            if (!radio || radio.Mixer == null) return;
            foreach (var r in _rows) r.slider.SetValue(radio.Mixer.GetUserVolume(r.def.Id));
        }

        protected override void Refresh()
        {
            var radio = RadioDirector.Instance;
            if (!radio) return;
            var th = UiTheme.Current;
            foreach (var r in _rows)
            {
                int state = r.def.IsActiveIn(radio.Weather) ? 1 : 0;
                if (state == r.shown) continue;
                r.shown = state;
                bool active = state == 1;
                r.slider.SetDim(!active);
                r.label.color = active ? th.text : th.textSoft;
                r.icon.color = !active ? new Color32(0xB4, 0xB0, 0xE4, 255) : r.warm ? th.amber : th.text;
            }
        }
    }
}
