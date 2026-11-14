using System;
using UnityEngine;

namespace Iori.Puppet
{
    /// <summary>
    /// A puppet model as Unity holds it: the model.json and, per layer, its pictures. Made by the importer
    /// (IORI/Puppet/モデルを取り込む) from a folder with model.json and its textures; not edited by hand.
    /// </summary>
    public sealed class PuppetAsset : ScriptableObject
    {
        [Tooltip("model.json(LivLiv の形式)")] public TextAsset model;
        [Tooltip("モデルのレイヤーと同じ順番")] public Layer[] layers = new Layer[0];

        [Serializable]
        public sealed class Layer
        {
            public string id;
            public Texture2D main;
            [Tooltip("口のコマ(閉じ、半開き、開き、…)")] public Texture2D[] frames = new Texture2D[0];
            [Tooltip("白目の形(黒目をこの中に切り抜く)")] public Texture2D mask;
            [Tooltip("ノーマル(なければ平ら)")] public Texture2D normal;
            public Texture2D[] frameNormals = new Texture2D[0];
            [Tooltip("絵が乗っているマス(メッシュを作る範囲)")] public byte[] occupancy = new byte[0];
        }
    }
}
