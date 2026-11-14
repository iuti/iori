# 塗り分けの地図

**今の正は chico が直した版**(2026-11-14、`partmap_chico_v1.psd`。手前にある部位は、輪郭より少し太らせてある)。`labels.png` はそれを読み戻したもの。下書きは `draft_source/` に移した(`labels_draft.png`、`partmap_draft.psd`)。この地図で切り出したレイヤーは `../layers/`(`iori_sit_v2_parts.psd` と `parts_v1/`)。

以下は下書きを作ったときの説明。

最終版(`../final/iori_sit_v2_final.png`)の全画素を、ちょうど 1 つの部位に割り当てた地図。人形のレイヤーは、これからこの地図で最終版を切り出して作る(色や差で振り分けるのはやめた)。

- `partmap_draft.psd`: クリスタで開いて直す用。レイヤーは下から
  - `final 最終版(さわらない)`
  - `map 塗り分け(ここを直す)`: 全部の部位が 1 枚に、部位ごとの単色で塗ってある。レイヤーの不透明度 55%(線が透ける)。
  - `legend 色の見本`: 部位の色と名前。色はここからスポイトで取る。
- `preview.png`: 最終版に地図を重ねた絵。`preview_head.png`: 頭を 3 倍にして境目を白線で描いた絵。
- `parts_sheet.png`: 頭まわりの部位を 1 つずつ抜き出した絵(この地図で切るとレイヤーに何が入るか)。
- `labels.png` / `labels.json`: 地図そのもの(画素の値 = 部位の番号)と、部位の一覧と色。
- `draft_source/labels_from_old_layers.png`: 前のレイヤー分けを地図にしたもの。肩から下の出発点に使った。

## 下書きの作り方と、あやしい所

- 頭まわり: ゴーグル、タグ、耳飾り、耳、ヘッドホン、あごの線は拡大して輪郭を読み、髪は色から、境目は絵の線の上に落ち着かせた。線は手前の部位に入れてある。
- 髪の分け方(前髪 / 横髪 / 後ろ髪 / アホ毛)は読みで決めた仮のもの。前髪 = ゴーグルの下から顔にかかる束と、耳の前の束。横髪 = 顔の左の大きな束と、耳の後ろから肩に落ちる束。後ろ髪 = ゴーグルより上と、外に広がる束。アホ毛 = てっぺんの束と細い 2 本。
- 首・ヘッドホン: カップとバンドと首の肌。カップから下がるストラップは胴に入れた。
- 肩から下(胴 / 土台 / ストラップ)は、前のレイヤー分けを線に寄せ直しただけ。ジャケットのすそと座面の先は直したが、ほかは細かく見ていない。
- 目は前のモデルの白目・黒目・まつげの形のまま。口は口の線だけ。

## 直し方(クリスタ)

1. `map` レイヤーを選ぶ。`legend` の色をスポイトで取って、ちがう所をその部位の色で塗りつぶす。1 枚のレイヤーなので、重なりもすき間もできない。
2. 境目は線画の線に沿って。線は手前にある部位に入れる(前髪の線は前髪、あごの線は顔)。
3. アンチエイリアスは切らなくてよい。部位の色とぴったり同じでない画素は、読み戻すときに、いちばん近い画素の部位になる。
4. 部位を増やしたいときは、まだ使っていない色で塗って、どの色が何かを教えてくれればよい。
5. レイヤーを結合しないで、PSD のまま保存する(`map` で始まるレイヤー名は変えない)。

隠れている所(髪の下の顔、後ろ髪の続きなど)は、この地図には入れない。地図が決まったあとで、部位ごとに描き足しが要る所を図にして相談する。

## 道具(`tools/character/partmap/`)

```
python3 tools/character/partmap/build_draft.py --final <最終版> --below draft_source/labels_from_old_layers.png --legend labels.json --out .
python3 tools/character/partmap/sheet.py <最終版> labels.png labels.json .
python3 tools/character/partmap/psd_map.py write <最終版> labels.png labels.json partmap_draft.psd
python3 tools/character/partmap/psd_map.py read <直した PSD> labels.json labels.png
python3 tools/character/partmap/cut_layers.py <最終版> labels.png labels.json ../layers/parts_v1 ../layers/iori_sit_v2_parts.psd
```

`shapes.py` が、読んだ輪郭と髪の分け方の種。
