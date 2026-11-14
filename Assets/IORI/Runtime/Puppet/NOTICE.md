# このフォルダの出どころ

イオリを 1 枚絵から動かす仕組み(`Core/PuppetRig.cs`)は、LivLiv Player の `src/runtime/puppet.ts` の一部を C# に移したもの。
モデルの形式(`model.json`、format 4)も LivLiv のもので、`Core/PuppetModel.cs` はその読み込みを自前で書いたもの。

```
LivLiv Player
Copyright 2026 LivLiv

This product includes software developed by LivLiv (https://livliv.ai).
```

- 元: https://github.com/livliv-dev/player (Apache License 2.0。全文は `LICENSE-Apache-2.0.txt`)
- 変えたところ: 描画は持たず、描く順番の一覧(`PuppetRig.draws`)を呼ぶ側が Unity で描く。眉の上げ下げ、キーフォーム、全身モデル、胸のばね、黒目のやわらかいマスク、まぶたの曲線、口パク用の別テクスチャは移していない。頭の傾き(`x-iori.head_tilt`)、体の回る中心(`x-iori.body_pivot`)、動かさないレイヤー(`x-iori.still`)、短い髪の毛先の揺れ(`x-iori.hair_tips`)を足した。
- 待機中の動き(`Core/PuppetIdle.cs`)と Unity 側(`PuppetAsset.cs`、`PuppetRenderer.cs`、シェーダー)は自前。
- LivLiv Player に同梱のサンプルキャラは Apache License 2.0 ではないので、このリポジトリには入れない。
- モデルのパッケージが透かし(`meta.watermark`)を求めるときは描く決まり(イオリのモデルは自作なので求めない)。
