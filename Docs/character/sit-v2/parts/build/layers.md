# 下書きモデルのレイヤー

描く順(上ほど奥)。i2i なし、ノーマルなし。`x-iori`: {"head_tilt": -27.1, "body_pivot": [585, 885], "still": ["base"]}

| id | group | role | 範囲 (x, y, w, h) | どの絵から取ったか |
|---|---|---|---|---|
| base | body |  | 47, 88, 957, 1420 | 最終版 + behind_body_02(体の後ろ) |
| back_hair | hair_back |  | 315, 42, 488, 412 | back_hair_01 |
| body | body |  | 216, 43, 734, 969 | 最終版 + head_02(髪の下の肩とシャツ) + 髪の取りこぼし |
| face | head |  | 434, 105, 254, 265 | 最終版 + head_02(約10.5px 下へずらした、髪の下のおでこ・耳・首) |
| eye_L | head | eye_L | 460, 268, 46, 32 | white_01(白目) |
| iris_L | head | iris_L | 466, 263, 39, 37 | 最終版(見えている所) + iris_01(隠れた所) |
| lash_L | head | eye_L | 456, 264, 54, 37 | 最終版 |
| lid_L | head | lid_L | 453, 261, 60, 46 | closed_04 |
| eye_R | head | eye_R | 525, 229, 52, 39 | white_01(白目) |
| iris_R | head | iris_R | 533, 225, 41, 41 | 最終版(見えている所) + iris_01(隠れた所) |
| lash_R | head | eye_R | 521, 225, 59, 41 | 最終版 |
| lid_R | head | lid_R | 518, 222, 66, 53 | closed_04 |
| mouth | head | mouth | 487, 285, 101, 75 | 最終版(閉じ) / halfopen_05 / halfopen_02 / open_03 / u_02 |
| side_hair | hair_side |  | 321, 56, 457, 395 | 最終版 + behind_tag_02(タグと耳飾りの裏) + under_goggles_03(頭の外のゴーグルの下) |
| earring | hair_side |  | 610, 259, 26, 34 | 最終版 |
| tag | hair_side |  | 634, 201, 71, 109 | 最終版 |
| front_hair | hair_front |  | 436, 106, 247, 264 | 最終版 + under_goggles_03(ゴーグルの下) |
| ahoge | hair_back |  | 372, 43, 217, 84 | 最終版 |
| goggles | head |  | 389, 85, 279, 159 | 最終版 |

単独の絵(isolates)は使っていない。タグ、耳飾り、アホ毛は最終版の画素から切り出した。
