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
| `Assets/StreamingAssets/IORI/Audio/` | 音声ファイル。`ambient/`(環境音)、`fx/`(チャイム)、`ui/`(UI の効果音)は本番素材。`placeholder/` はメニューで生成する仮素材(git に入れない) |
| `tools/CoreTests/` | Unity なしで Core のテストを動かす .NET プロジェクト |

## はじめに(デスクトップで)

1. Unity Hub で `C:\ai\iori` を開く(Unity 6000.3.10f1)。
2. メニュー **IORI/Audio/仮素材を生成** を実行する(仮の曲と環境音が `StreamingAssets/IORI/Audio/placeholder/` にでき、曲の長さ一覧 `audio-index.json` も更新される)。
3. 適当なシーンで Play。左上の動作確認パネルで、天気、静けさの時間、時計のずらし(「次の時間帯の15秒前」など)、音量を試せる。

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

## 環境音・チャイム・UI の効果音

ElevenLabs(Sound Effects v3)で作って、ループ処理と音量合わせをしたもの。

| ファイル | 中身 |
| --- | --- |
| `ambient/fire.ogg` `rain.ogg` `wind.ogg` `insects.ogg` | 継ぎ目なしのループ(28〜30 秒、OGG Vorbis 44.1kHz ステレオ、どれも -22 LUFS にそろえてある)。混ぜる量は `radio.json` の `defaultVolume` で決める |
| `fx/chime.ogg` | 正時チャイム(グロッケンの上がっていく 4 音、-18 LUFS) |
| `ui/tap.wav` `toggle.wav` `slider.wav` `notice.wav` `open.wav` `close.wav` `radio_tune.wav` | UI の効果音(WAV モノラル 44.1kHz、ピーク -6 dB 前後、`slider` は連打されるので -9 dB)。まだコードからは鳴らしていない |

ElevenLabs の効果音は、アプリに組み込んで使うのは問題ないが、音声ファイル単体として配ったり売ったりはしない(規約の条件)。

## Android

- サービスは Media3 の `MediaSessionService`。再生中は通知が出てフォアグラウンドで動き、通知・ロック画面・イヤホンのボタンで再生/一時停止できる。
- 音は APK 内の StreamingAssets(`asset:///IORI/Audio/...`)から ExoPlayer で鳴らす。
- Player Settings の **Mute Other Audio Sources** はオフにしておく(Unity が音のフォーカスを奪わないように)。

## キャンプ地の景色(段階 2)

シーンは `Assets/IORI/Scenes/Camp.unity`。景色の形はコードで組み立てる(`CampBuilder`)ので、シーンに保存されているのは置き場所と設定だけ。

| もの | 役割 |
| --- | --- |
| `CampAtmosphere` | 時刻、天気(晴れ、雨、雨上がり)、季節、おやすみモードから、いまの色と光を決めてシェーダーに渡す。Play 中は端末の時計とラジオの天気に合わせる |
| `CampLook`(`Art/Camp/CampLook.asset`) | 時刻ごとの色、小物の色、雨と季節の色の掛け方。見た目の調整はまずここ |
| `CampBuilder` | 地面、木、遠景、焚き火、テント、小物、草花、水、雨を作る。「置き場所」の下の物を動かすと組み立て直す |
| `CampProp` | 押せる小物(ラジオ、音楽プレーヤー、ランタン、焚き火、イオリ)。`CampProp.Tapped` で機能のパネルを開く。ランタンはおやすみモードの切り替え |
| `CampCameraRig` | 寄りの構図、ゆっくりした揺れ、左右のドラッグ、小物に寄る動き。縦画面と横画面で画角が変わる |
| `CampReflection` | 湖と水たまりの映り込み(反射用のカメラ 1 台を縮小解像度で) |
| `CampBillboard` | イオリの仮の立ち絵。livliv のモデルを RenderTexture に描くようになったら、この板の絵を差し替える |
| `CampDebugPanel` | Play 中、右上の「景色」から時刻、天気、季節、おやすみ、筆あと、光の段を試せる |

絵作りの決まり(方向性まとめより): 暖色は光っているものだけ、輪郭線なし、遠景は距離ごとに一色の層、空はなめらかなグラデーション、ぼかしなし。Unity のライトは使わず、焚き火、ランタン、テントの光はシェーダーの中で計算している(`Art/Camp/Shaders/IoriCamp.hlsl`)。近景の細かさは、筆あとテクスチャ(`tools/camp/make_brush.py` で生成)で光の段の境目と隣の色への切り替えを揺らして出している。

メニュー **IORI/Camp/シーンを組み立てる** でシーンを作り直せる。**IORI/Camp/スクショを撮る** で時間帯と天気の見本を撮れる(バッチモードでも動く。`CampSetup` のコメント参照)。
