# レイヤー

**今の正は `iori_sit_v2_parts_chico_v1.psd`**(2026-11-14 夜、chico が `iori_sit_v2_parts.psd` に隠れた所を描き足し、見えている所にも手を入れて返したもの。耳を別レイヤー「耳」にしてある)。人形のテクスチャと model.json は、ここから `tools/character/partmap/model_from_psd.py` で作る。自動の切り出しでこの PSD を上書きしない。止めの絵は最終版と同じではなくなった(chico の手直しの分)。

- `iori_sit_v2_parts.psd` と `parts_v1/`: chico が直した塗り分けの地図(`../partmap/labels.png`)のとおりに、最終版を切り出したもの(`tools/character/partmap/cut_layers.py`)。背景の灰色は抜いてある。見えている所だけで、隠れた所の描き足しはまだ無い。
  - 21 レイヤー(下から base, strap, back_hair, body, neck, face, eye_L, iris_L, lash_L, eye_R, iris_R, lash_R, mouth, side_hair, earring, tag, front_hair, brow_L, brow_R, ahoge, goggles)。いちばん下に、非表示の最終版(見本)。
  - どの画素もちょうど 1 枚に入る。部位どうしの境目は地図の線で硬く切り、背景に面したふちだけ半透明に解いてある。同じ灰色の上に重ねると最終版に戻る(差は 0.5 以下)。
  - 眉は、まつげの色で塗られた所のうち目に触れていないかたまりを、別レイヤー(brow_L / brow_R)にした。
  - `parts_v1/labels_used.png` が実際に使った地図(背景に残っていた輪郭のにじみ 2,263 画素を、となりの部位に入れた)。`check.png` は青の上に重ねた絵、`check_background.png` は部位の中で背景として抜いた所(桃色)。
- `iori_sit_v2_layers.psd`: 前の下書きモデル(不合格になったレイヤー分け)を PSD にしたもの。
