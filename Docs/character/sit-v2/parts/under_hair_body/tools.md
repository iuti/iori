# インペイントのために入れた物(2026-11-14、chico の了承のうえで)

すべて `C:\ai\ComfyUI` の中。ComfyUI 本体と venv のパッケージは変えていない(追加の pip は無し。必要な spandrel 0.4.1、kornia 0.8.0、opencv 4.11 はもともと入っていた)。

| 名前 | 版 / コミット | 置いた場所 | 大きさ | 配布元 | ライセンス |
|---|---|---|---|---|---|
| comfyui-inpaint-nodes(Acly) | v1.4.4、コミット 5b7681746fe19e563113d27366ba85236bc1ba5b | `custom_nodes/comfyui-inpaint-nodes` | 数百 KB | https://github.com/Acly/comfyui-inpaint-nodes | GPL-3.0 |
| big-lama.pt(LaMa) | Sanster/models の add_big_lama | `models/inpaint/big-lama.pt` | 205,669,692 バイト | https://github.com/Sanster/models/releases/download/add_big_lama/big-lama.pt | Apache-2.0(advimman/lama) |
| fooocus_inpaint_head.pth | lllyasviel/fooocus_inpaint | `models/inpaint/fooocus_inpaint_head.pth` | 52,602 バイト | https://huggingface.co/lllyasviel/fooocus_inpaint | Fooocus と同じ(GPL-3.0) |
| inpaint_v26.fooocus.patch | lllyasviel/fooocus_inpaint | `models/inpaint/inpaint_v26.fooocus.patch` | 1,323,362,033 バイト | https://huggingface.co/lllyasviel/fooocus_inpaint | 同上 |

MAT(Acly/MAT の fp16)は一度ダウンロードしたが、了承の範囲に入っていなかったので消した。

## 使い方の注意

- カスタムノードを ComfyUI に読み込ませて起動する操作は、この PC の自動許可の判定で止められたので、まだ一度も読み込んでいない。そのため Fooocus のパッチ(ノードが要る)は今回使っていない。
- LaMa は、ComfyUI の venv にもともとある spandrel で big-lama.pt を直接読んで動かした(`parts/build/scripts/lama.py`)。
- ComfyUI Desktop をふつうに起動すると、`custom_nodes` にあるこのノードは読み込まれる(Desktop は全部のカスタムノードを読む)。

## ノードの読み込みと起動(2026-11-14、chico の許可のあと)

comfyui-inpaint-nodes だけを読み込み、ほかのカスタムノードは切ったまま、ヘッドレスで起動した:

```
C:\ai\ComfyUI\.venv\Scripts\python.exe "%LOCALAPPDATA%\Programs\@comfyorgcomfyui-electron\resources\ComfyUI\main.py" --port 8000 --base-directory C:\ai\ComfyUI --extra-model-paths-config %APPDATA%\ComfyUI\extra_models_config.yaml --disable-auto-launch --disable-all-custom-nodes --whitelist-custom-nodes comfyui-inpaint-nodes --preview-method none
```

読み込みは 0.1 秒で通った(エラーなし)。使い終わったら止めてある。

## 使ったワークフロー(候補 15〜18、`parts/build/scripts/fooocus.py`)

1. 肩まわり(最終版の x290〜830、y210〜570)を 2 倍にし、マスク(`mask_body.png`、体にかかる髪だけ +3px)を透明度で入れて LoadImage。
2. INPAINT_LoadInpaintModel(big-lama.pt)→ INPAINT_InpaintWithModel で、マスクの中を LaMa で下塗り。
3. INPAINT_LoadFooocusInpaint(fooocus_inpaint_head.pth、inpaint_v26.fooocus.patch)→ INPAINT_ApplyFooocusInpaint で Hassaku XL(hassakuXLIllustrious_v22)をインペイント用にする。
4. INPAINT_VAEEncodeInpaintConditioning(下塗りした絵、同じマスク)→ KSampler(28 ステップ、cfg 5、dpmpp_2m karras、denoise 0.3/0.5/0.8)→ VAEDecode。
5. 縮小して、マスクの中だけを最終版に貼り戻す(マスクの外は差 0)。

プロンプト: 白い上着の肩、クリーム色のフードの縁、白いシャツの襟、黒いヘッドホン、無地の灰色の背景、フラットな塗り、細い線画。ネガティブ: 髪全般。
