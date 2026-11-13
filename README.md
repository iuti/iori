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
時間帯が切り替わるとき、前の曲は `crossfadeSeconds`(既定 8 秒)かけてフェードアウトし、次の曲はフェードインせず頭から鳴る(イントロの張りを残すため)。
`mode: "quiet"` の時間帯は曲を流さず、環境音とたまに鳴るフレーズだけになる(ブレワイ式)。

## Android

- サービスは Media3 の `MediaSessionService`。再生中は通知が出てフォアグラウンドで動き、通知・ロック画面・イヤホンのボタンで再生/一時停止できる。
- 音は APK 内の StreamingAssets(`asset:///IORI/Audio/...`)から ExoPlayer で鳴らす。
- Player Settings の **Mute Other Audio Sources** はオフにしておく(Unity が音のフォーカスを奪わないように)。
