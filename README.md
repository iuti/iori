# IORI

ゲームで得られる音楽体験を、現実の生活に持ち込むための、キャラクターのいる音楽プレーヤー。
焚き火のキャンプ地でイオリが過ごしていて、時間帯と天気で曲と環境音が変わる。Android と PC(Windows)向け。

- Unity 6000.3.10f1(URP)
- 段階 1「音の土台」: 時間帯で曲が変わるラジオ、環境音のミキサー、Android で画面を消しても鳴り続ける再生

## しくみ

```
radio.json(番組表) ─┐
天気・静けさモード ──┼─▶ RadioScheduler(C#、Unity 非依存) ─▶ PlaybackPlan(予定表 JSON、48 時間ぶん)
ミキサーの音量 ──────┘                                              │
                                                ┌────────────────────┴───────────────────┐
                                                ▼                                        ▼
                               Android: IoriPlaybackService(Java)          PC: UnityAudioBackend
                               Media3 / ExoPlayer、裏でも鳴り続ける            Unity の AudioSource
```

- **何を鳴らすかを決めるのは C# の `RadioScheduler` だけ。** 再生係(Android のサービス、PC の Unity)は予定表をそのまま鳴らす。Android ではアプリが裏に回ると Unity が止まるので、先に 48 時間ぶんの予定表を作って渡しておく。
- 予定表は「1 回の再生 = 1 クリップ」の並び。クリップには開始・終了時刻、フェードイン/アウト、ループの有無がある。再生位置は `t - start + offset` で決まるので、予定表を作り直しても鳴っている曲は途切れない。
- アプリが前面に戻ったとき、天気や設定を変えたとき、30 分ごとに予定表を作り直す。予定表が切れても(48 時間アプリを開かなくても)再生係は最後の 24 時間を繰り返す。

## フォルダ

| 場所 | 中身 |
| --- | --- |
| `Assets/IORI/Runtime/Audio/Core/` | 選曲の頭脳(番組表、予定表、ミキサー、仮素材の合成)。UnityEngine を参照しない |
| `Assets/IORI/Runtime/Audio/Unity/` | 司令塔 `RadioDirector`(どのシーンでも自動で作られる)、PC の再生係、Android への橋渡し、動作確認パネル |
| `Assets/IORI/Editor/` | メニュー「IORI/Audio」 |
| `Assets/IORI/Tests/EditMode/` | Core のテスト |
| `Assets/Plugins/Android/IoriPlayback.androidlib/` | Android のバックグラウンド再生サービス(Java、Media3) |
| `Assets/StreamingAssets/IORI/radio.json` | 番組表(時間帯、曲、チャイム、静けさの時間のフレーズ、環境音) |
| `Assets/StreamingAssets/IORI/Audio/` | 音声ファイル。`placeholder/` はメニューで生成する仮素材(git に入れない) |
| `tools/CoreTests/` | Unity なしで Core のテストを動かす .NET プロジェクト |

## はじめに(デスクトップで)

1. Unity Hub で `C:\ai\iori` を開く(Unity 6000.3.10f1)。
2. メニュー **IORI/Audio/仮素材を生成** を実行する(仮の曲と環境音が `StreamingAssets/IORI/Audio/placeholder/` にでき、曲の長さ一覧 `audio-index.json` も更新される)。
3. Play する。キャンプ地のシーンでは、右上の一行を押して開くパネルの「音」タブで、静けさの時間、時計のずらし(次の時間帯の直前へ、など)、音量を試せる(天気は「景色」タブ)。ほかのシーンでは左上の「IORI ▶」を押すと同じ操作のパネルが開く。

テスト: Unity の Test Runner(EditMode)か、Unity なしなら `dotnet test tools/CoreTests`。

## 曲の差し替え(Suno など)

1. 曲を **OGG Vorbis** にする(MP3 や M4A はループの継ぎ目に隙間が出るので使わない)。`ffmpeg -i in.mp3 -c:a libvorbis -q:a 6 out.ogg` など。
2. `Assets/StreamingAssets/IORI/Audio/radio/` などに置き、`radio.json` の `file` を書き換える。
3. メニュー **IORI/Audio/番組表をチェック** で抜けを確認。ビルド時には曲の長さ一覧が自動で更新される。

`radio.json` の曲(`tracks`)の書き方:

| 項目 | 意味 |
| --- | --- |
| `phase` / `weather` | どの時間帯・天気の曲か。雨の曲がなければ晴れの曲が使われる。同じ条件に複数あれば日替わり |
| `loop` | `crossfade`(既定): 最後まで流して、終わり際に頭とクロスフェード。`seamless`: ループ用素材を継ぎ目なく繰り返す |
| `gapSeconds` | `crossfade` のとき、重ねずに無音を挟む秒数(0 なら重ねる) |
| `volume` | 曲ごとの音量 |

時間帯(`phases`)は `start`(HH:mm)の順に並び、次の時間帯まで続く。1 時間刻みにしたければ 24 個書けばよい。
時間帯が切り替わるとき、前の曲は `crossfadeSeconds`(既定 8 秒)かけてフェードアウトし、次の曲はフェードインせず頭から鳴る(イントロの張りを残すため。フェードインさせたいときは `introFadeSeconds` を指定)。
`mode: "quiet"` の時間帯は曲を流さず、環境音とたまに鳴るフレーズだけになる(ブレワイ式)。

## Android

- サービスは Media3 の `MediaSessionService`。再生中は通知が出てフォアグラウンドで動き、通知・ロック画面・イヤホンのボタンで再生/一時停止できる。
- 音は APK 内の StreamingAssets(`asset:///IORI/Audio/...`)から ExoPlayer で鳴らす。
- Player Settings の **Mute Other Audio Sources** はオフにしておく(Unity が音のフォーカスを奪わないように)。

## キャンプ地の景色(段階 2)

シーンは `Assets/IORI/Scenes/Camp.unity`。形(木、草、道具、テント、地面)は Blender の Python で作って `Art/Camp/Models/camp.iomesh` に書き出し、Unity 側はそれを並べて、光、水、炎、空を足す。

| もの | 役割 |
| --- | --- |
| `tools/camp/blender/build_camp.py` | 形を作る。`camp.iomesh`(全メッシュ)、`camp_layout.json`(置き場所)、`iori_ground.png`(地面の覆いと落ち影)を書き出す。Blender の Python モジュール(`pip install bpy`)で動く。`--draft --preview 絵.png` で、何も書き出さずに形だけ確かめられる(`--yaw 20` でカメラを回り込ませる、`--wide` で横長) |
| `tools/camp/make_strokes.py` | 筆あとの地図 `iori_strokes.png` を作る(どの画素がどの筆あとに属するか) |
| `tools/camp/make_ridges.py` | 対岸の山並みの輪郭と町の灯りの配置 `iori_ridges.png` を作る(山の形は乱数ではなく、角の点を並べて描いた構図。第 2 引数に画像名を渡すと確認用の絵も出る) |
| `CampAtmosphere` | 時刻、天気(晴れ、雨、雨上がり)、季節、おやすみモードから、いまの光と色を決める。太陽や月、焚き火、ランタン、テントは本物のライトで、焚き火は影も落とす。Play 中は端末の時計とラジオの天気に合わせる |
| `CampLook`(`Art/Camp/CampLook.asset`) | 時刻ごとの空と光、灯りの強さ、筆あとの細かさ、雨と季節とおやすみの効き方。見た目の調整はまずここ |
| `CampProp` | 押せる小物(ラジオ、音楽プレーヤー、ランタン、焚き火、イオリ)。`CampProp.Tapped` で機能のパネルを開く。ランタンはおやすみモードの切り替え |
| `CampCameraRig` | 寄りの構図、ゆっくりした揺れ、なぞって回り込む、つまんで寄る、小物に寄る。ピントの距離も決める |
| `CampReflection` | 水面の鏡像。湖用と水たまり用の 2 つ(水たまりは湖より高い所にあるため) |
| `CampQuality` | 画質の段階。「スマホ相当」は、ぼかし、焚き火の影、草の一部、水たまり専用の鏡像を省く。Android では自動でこちら |
| `CampBillboard` | イオリの仮の立ち絵。livliv のモデルを RenderTexture に描くようになったら、この板の絵を差し替える |
| `CampDebugPanel` | Play 中の確認パネルはこれひとつ。右上の一行にいまの景色(時刻、天気、季節、自動かどうか)が出て、押すと下からパネルが開く。タブは 3 つ: 「景色」(時刻、天気、季節、おやすみモード)、「カメラ・画質」、「音」(ラジオの流れ方、時計のずらし、音量)。景色の天気を変えると雨音も一緒に変わる |
| `CampShotRunner` | Windows 用ビルドを `-ioriShots フォルダ` 付きで起動すると、パネル、炎の動き、雨の降り始めなどを自分で撮って終了する(動いている画面の確認用。`CampSetup.BuildWindowsPlayBatch` でビルド) |

描き方(`Art/Camp/Shaders`、共通部分は `IoriCommon.hlsl`):

- 物の表面は画素ごとではなく筆あと一枚ごとに光と影を取る。一枚の筆あとは一色で、輪郭は角ばる。筆あとは遠いほど大きくなり、画面の中ではほぼ同じ大きさに見える。
- 暖色は光っているものだけ。影は黒ではなく藍。灯りの輪は段になる。灯りに照らされた色は深い橙で頭打ちにし、灯りが強いところでは空の青い光を引っこめる(焚き火のまわりが白っぽい肌色にならないように)。
- トーンカーブは使わない(決めた色がそのまま出る)。1 を超えた明るさは白で止まり、にじむ。
- 空、雲、星、月、山並み、町の灯り、対岸の木は全部スカイボックス(`IoriSky.shader`)に描く。だから水面にもそのまま映る。
- 炎(`IoriFire.shader`)は板ではなく体積。箱の中を光線でたどり、6 本の炎の舌それぞれのいちばん熱い所の温度で色を決めて、奥の舌から順に塗り重ねる。色の境目はやわらかく、輪郭はくっきり。薪の後ろの炎は薪に隠れ、手前の炎は薪にかぶる。
- テント(`IoriLit.shader` の「中から照らされた布」)は、ドームの面ひとつがほぼ一色。カメラとランプを結ぶ線に近い面ほど明るく、カメラが動くと明るい面も移る。
- 湖と水たまり(`IoriWater.shader`)は、筆あと一枚ごとに鏡像を少しずらして映す。浅い所は底が透ける。

メニュー **IORI/Camp/シーンを組み立てる** でシーンを作り直せる。**IORI/Camp/スクショを撮る** で時間帯と天気の見本を撮れる(バッチモードでも動く。`CampSetup` のコメント参照)。
