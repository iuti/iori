using System.Collections.Generic;
using Iori.Audio;
using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// The fire's sheet: the sounds around the camp (fire, wind, insects, rain), each a picture and a
    /// slider and nothing else. The fire's picture is amber while the fire is heard. A sound that is
    /// not heard in this weather (rain while it is clear) is greyed but can still be set.
    /// </summary>
    public sealed class AmbientPanel : UiPanel
    {
        static readonly (string id, string icon)[] Known =
        {
            ("fire", "fire"), ("wind", "wind"), ("insects", "leaf"), ("rain", "rain"),
        };

        const float Pad = 26f, First = 16f, RowHeight = 46f, Last = 24f;

        sealed class Row
        {
            public AmbientDef def;
            public UiShape icon;
            public UiSlider slider;
            public bool warm;
            public int shown = -1;
        }

        readonly List<Row> _rows = new List<Row>();

        static int LayerCount
        {
            get
            {
                var radio = RadioDirector.Instance;
                return radio && radio.Mixer != null ? radio.Mixer.Layers.Count : Known.Length;
            }
        }

        public override float Height => First + RowHeight * LayerCount + Last;

        protected override void Build()
        {
            var th = UiTheme.Current;
            var radio = RadioDirector.Instance;
            if (!radio || radio.Mixer == null) return;

            // the layers the UI knows come first, in its own order; any other after them
            var layers = new List<AmbientDef>();
            foreach (var k in Known)
                foreach (var l in radio.Mixer.Layers)
                    if (l.Id == k.id) layers.Add(l);
            foreach (var l in radio.Mixer.Layers) if (!layers.Contains(l)) layers.Add(l);

            float y = First;
            foreach (var def in layers)
            {
                string icon = "wind";
                foreach (var k in Known) if (k.id == def.Id) icon = k.icon;
                var row = new Row { def = def, warm = def.Id == "fire" };
                row.icon = UiKit.Icon("Icon " + def.Id, Content, Pad, y + 12, 22, icon, th.text);
                row.slider = UiSlider.Create("Slider " + def.Id, Content, 66, y + 1, Width - Pad - 66);
                row.slider.Bind(IoriSettings.AmbientVolume(def.Id));
                _rows.Add(row);
                y += RowHeight;
            }
        }

        protected override void Refresh()
        {
            var radio = RadioDirector.Instance;
            if (!radio) return;
            var th = UiTheme.Current;
            foreach (var r in _rows)
            {
                bool active = r.def.IsActiveIn(radio.Weather);
                bool heard = active && r.slider.Value > 0.001f;
                int state = !active ? 0 : heard ? 2 : 1;
                if (state == r.shown) continue;
                r.shown = state;
                r.slider.SetDim(!active);
                UiKit.Tint(r.icon, !active ? th.textDim : r.warm && heard ? th.amber : th.text);
            }
        }
    }
}
