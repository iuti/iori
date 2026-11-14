# PuppetPreview

人形(`Assets/IORI/Runtime/Puppet/Core`)を Unity なしで動かして、絵にする道具。クラウドでも、ポーズや待機の動きを原寸で描いて確かめられる。

- `Program.cs`(.NET 8): model.json と「何を描くか」の一覧を読み、人形を動かして、その瞬間ごとの網目の位置を書き出す。
- `render.js`(Node + Playwright の Chromium、WebGL): それを `PuppetRenderer` と `IoriPuppetLayer.shader` と同じ重ね方(色はファイルの数値のまま、白目のステンシルで瞳を切る)で PNG にする。

Unity が撮った絵とくらべて、アルファは同じ、色は半透明のふちの丸めの差だけ(2026-11-14、止め、かしげ、全部いっぺん、の 3 ポーズで確認)。光やノーマルは描かない(人形の色だけ)。見た目の最終確認は Unity で。

## 使い方

```
dotnet build tools/PuppetPreview -c Release
dotnet tools/PuppetPreview/bin/Release/net8.0/PuppetPreview.dll <model.json> <jobs.json> <出力の頭>
node tools/PuppetPreview/render.js <出力の頭> <model.json のあるフォルダ> <PNG を置くフォルダ> [--bg r,g,b]
```

`jobs.json` は一覧(配列)。1 つが 1 枚、または 1 本の動き。

```json
[
  { "name": "rest", "pose": {}, "seconds": 0.1 },
  { "name": "tilt", "pose": { "angleZ": 30, "bodyZ": 5 }, "seconds": 2, "swing": 1 },
  { "name": "tilt_moving", "pose": { "angleZ": 30 }, "seconds": 1, "frames": true, "every": 2 },
  { "name": "idle", "idle": { "turn": 4.5, "tilt": 3.5, "lean": 1.5, "seed": 1 }, "from": 1, "seconds": 12, "every": 1 }
]
```

- `pose`: その値を `seconds` のあいだ保って、最後の瞬間を 1 枚(`PuppetRenderer.RenderPose` と同じ)。`frames: true` で途中も全部。値は `PuppetParams` の名前(angleX / angleY / angleZ / bodyX / bodyZ / breath / eyeLOpen / eyeROpen / eyeBallX / eyeBallY / mouthOpen / vowel / smile)。
- `keys`: `[{ "t": 0, "pose": {} }, { "t": 1.2, "pose": { "angleZ": 30 } }, ...]`。キーからキーへなめらかに動かして、全コマを描く(1 つの動きを端まで振って見る)。
- `idle`: 待機の動き(`PuppetIdle`)を止めの姿から 1/30 秒ずつ進めて、`from` 秒から `seconds` 秒ぶんを描く(`PuppetRenderer.RenderAt` と同じ進め方)。
- `swing`: 髪と揺れものの揺れの倍率(`PuppetRenderer.swing`)。
- `posture`: 姿勢(`PuppetRenderer.posture`)。0 が描いた絵のまま(書かないとき)、1 が背すじを起こした姿(model.json の `x-iori.upright`)。
- `every`: 何コマおきに描くか。`dry: true`: 網目は書かず、各コマの数値だけ(長い動きがどこまで行くかを見る)。`only: ["back_hair"]`: そのレイヤーの網目だけ(測るため。絵にはしない)。

出力の `.json` には各コマの数値(`param`: angleX, angleY, angleZ, bodyX, bodyZ, breath, eyeLOpen, eyeBallX, eyeBallY)も入る。
