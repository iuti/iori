# IORI — エージェント向けメモ

コードの主導権はエージェントにある。見た目の調整と「美しいか」の判断は chico が Unity Editor で行う。
ユーザーへの応答は日本語の柔らかいタメ口(〜したよ)。

## 決まり

- 選曲などのロジックは `Assets/IORI/Runtime/Audio/Core/` に置き、UnityEngine を参照しない(asmdef の `noEngineReferences: true`)。テストは `Assets/IORI/Tests/EditMode/` に書き、Unity なしでも `dotnet test tools/CoreTests` で通ること。
- 何を鳴らすかを決めるのは `RadioScheduler` だけ。Android の `PlanEngine.java` と PC の `UnityAudioBackend.cs` は予定表を鳴らすだけで、同じ決まり(フェードの式、再生位置の式、消えたクリップのフェードアウト、単発音の遅延許容)を持つ。片方を変えたらもう片方も直す。
- 予定表の JSON の形は `PlaybackPlan.cs` が正。`Plan.java` はそれに合わせる。
- 音楽プレーヤー(端末の曲)の鳴らす順を決めるのは `PlayQueue` だけ。再生係(`IMusicBackend`: PC は `UnityMusicBackend`、Android は `AndroidMusicBackend` と再生サービスの中の `MusicEngine.java`)は渡された列を鳴らすだけで、曲が終わったときの決まり(くり返しなしは最後で止まる、全部は最初へ回る、1 曲は同じ曲、読めない曲は飛ばす)を同じにする。片方を変えたらもう片方も直す。鳴らすのはラジオかプレーヤーのどちらか片方(`MusicDirector.Active` の間、ラジオは黙って環境音だけ。chico が決めた仕様)。
- 曲の一覧のタブは 曲 / アルバム / 演奏者 / リスト。「リスト」は自分で作る曲の並び(プレイリスト)で、中身は Core の `MusicPlaylists`(作る、名前、足す、外す、並べ替え、JSON の保存)。保存は `MusicDirector` が端末の `playlists.json` に書く(`Persist` が false の撮影中は読み書きしない)。曲は Id と、曲名・演奏者・アルバム名の鍵で覚え、見つからない曲は消さずに見せるときだけ飛ばす。曲の行を長押しすると `PlaylistPicker` が出る。検索は `MusicLibrary.Search` で、文字は `TextFold` でそろえる(大文字小文字、全角半角、ひらがなカタカナ、空白)。
- Android の曲の一覧は `MusicStore.java` が MediaStore から集めて JSON で渡し、Core の `MediaStoreJson.cs` が読む。形を変えたら両方直す。集めるのは `AndroidMusicBackend.Folder`(既定は `PC-Music`)という名前のフォルダの下だけ。
- イオリが場面で何を言うか(黙るか)を決めるのは Core の `VoicePicker` だけ(セリフ帳は `StreamingAssets/IORI/voice.json`)。時報の声はチャイムと同じくラジオの予定表(`RadioScheduler`、`radio.json` の `jiho`、種類 `voice`)で、静けさの時間でもプレーヤーの番でも毎正時に言う(chico の指定)。「おかえり」は家に帰ってきたとき用にとっておく(アプリに戻っただけでは言わない)。口パクは `VoiceDirector` が `LipSync` で声から作った値を `IoriMouth` に渡し、口の相手は `IMouthTarget` を実装して受け取るだけ(モデルの種類を声の側に持ち込まない)。人形の値(パーツ、動き)は声の側から触らず、`mouthOpen` と `vowel` だけを書く。
- 設定(いまは音量だけ)は Core の `SettingsStore` 1 か所に持つ。鍵と決まりの一覧は `IoriSettings`(`volume.radio` ラジオの曲、`volume.player` プレーヤー、`volume.voice` 声、`volume.ui` 画面の音、`volume.ambient.<id>` 環境音ごと)。端末への保存は `SettingsDirector`(`settings.json`、変わってから少し待って書く。`Persist` が false の撮影中は書かない)。音量を変えるのも読むのもこのストア経由で、ラジオ、プレーヤー、声、焚き火の炎はストアを `Watch` して合わせる。シートのつまみは `UiSlider.Bind(鍵)` でつなぐ(同じ鍵のつまみはどこに置いても同じ 1 本のバーとして動く)。声のオンオフや会話の設定も、新しい鍵を `IoriSettings` に足して同じストアに載せる。設定の画面はまだ無い。
- イオリを動かす人形(1 枚絵をレイヤーに分けて動かす)の計算は `Assets/IORI/Runtime/Puppet/Core/` に置き、ここも UnityEngine を参照しない。テストは `Assets/IORI/Tests/EditMode/Puppet/`、`dotnet test tools/CoreTests` で通ること。`PuppetRig.cs` は LivLiv Player(Apache-2.0)の一部を移したものなので、冒頭の出どころの記載と `Assets/IORI/Runtime/Puppet/NOTICE.md` を残す。
- 人形の絵は、Unity なしでも `tools/PuppetPreview`(Core をそのまま動かして、ヘッドレスの Chromium の WebGL で重ねる)で原寸で描ける。ポーズや動きの大きさを変えたら、まずこれで穴やずれを見る(使い方は `tools/PuppetPreview/README.md`)。光とノーマルは Unity でしか見られない。
- イオリと LLM で話す機能は、LLM なしで成り立つ作りの上に足したもの(chico の決定)。キーが無い、通信できない、ならアプリは今までどおりで、場面の一言と時報は変えない。何をどう言わせるか(人となり、見本、返事の長さ、覚える量)は `StreamingAssets/IORI/talk.json` と Core の `TalkPrompt`、返事を決まりの形に整える(1〜2 文、文字数、かぎかっこやト書きを外す)のは Core の `TalkReply`、記憶(直近のやり取りと短い覚え書き)は Core の `TalkMemory` だけ。`TalkDirector` は通信して鳴らすだけ(LLM は Claude の Messages API、声は ElevenLabs。送る形と読む形は Core の `TalkApi`)。キーは `SecretStore` で端末の中だけに暗号化して置き(Android は `SecretBox.java` の Keystore、Windows は DPAPI)、ログにもリポジトリにも書かない。記憶は端末の `talk_memory.json`(`MusicDirector.Persist` が false の撮影中は読み書きしない)。入口はイオリの長押し(`CampProp.Held` → `UiRoot.OpenTalk` → `TalkPanel`)で、話せないとき(キーなし、眠っている)は長押しも今までどおりの一言になる。返事の声は `VoiceDirector.SpeakReply` が言い、時報と重なるなら時報のあとに回す。返事を言っている間は場面の声を言わない。会話の履歴の画面は作らない。
- Android のネイティブコードは Java(Unity 6 の .androidlib で Kotlin プラグインを抱えないため)。
- Unity のコンパイル、PlayMode、実機ビルドは chico のデスクトップ(`C:\ai\iori`)でしかできない。クラウドでは Core の .NET テストまで。
- 本番の UI は `Assets/IORI/Runtime/UI/` にあり、uGUI と TextMeshPro をコードで組む(プレハブなし)。形(面、線、影、ガラス)はシェーダー `Resources/IoriUI/Shape.shader` 1 本で描き、画像素材は足さない(アイコンだけ PNG)。寸法は 390 幅の案の数値をそのまま使う(`UiKit` は左上からの位置で置く)。スマホは縦持ちだけ。
- UI の見た目の決まり(chico のレビューから): 角は丸めない。光るぼかし(グロー)は使わない。文字は中身(時間帯の名前、曲名、時刻)だけにして、説明やラベルは置かず配置とアイコンで見せる。ボタンはアイコンだけ(丸い台は置かない)。パネルは画面の下端にぴったり付く藍のガラスのシート。琥珀色は「いま鳴っている」ことを示す細い印(針、いまの時間帯の下線、再生位置、レベルの棒)だけ。色と寸法は `UiTheme` に置く。
- シートは下向きに払うとホームに戻る(`UiPanel` が指についていき、離した位置か勢いで閉じる。スライダー `UiSlider` と曲の一覧 `UiListScroll` は、下向きの動きを自分で使わずシートに渡す。新しい部品がドラッグを受けるときも同じにする)。音楽プレーヤーの音量は `MusicDirector.Volume`(ラジオの曲の音量とは別)。炎の大きさは焚き火の音量のつまみに合わせ、音量ゼロで火は消える(`CampAtmosphere.fireFollowsVolume`。chico の指定で、いちばん大きいときでも、つまみと関係なく決めていたころより小さい)。
- UI を変えたら、デスクトップのビルドを `-ioriUiShots <フォルダ>` で起動して実際の画面を撮り(`UiShotRunner`)、見た目の案と見比べてから chico に見せる。音楽プレーヤーの画面は見本の曲で撮る(`-ioriUiShotJackets Docs/ui-shots/sample-jackets` を足すと見本のジャケットが付く)。chico の PC の曲は撮らない。
- スマホ向けの軽さ(`CampQuality` の Phone。実機 Reno7 A の計測で決めた): 炎は立体の計算ではなく、`tools/camp/bake_fire.py` が焼いた連番の絵 `Resources/IoriCamp/fire_flipbook.png` の板(`IoriFireCard.shader`)。`IoriFire.shader` の炎の形を変えたら焼き直す。空と遠景は毎フレーム描かず、`CampSkyCache` が少しずつ描きためた絵から読む(描く中身は `IoriSkyPaint.hlsl` 1 か所。焚き火の空気の光だけはカメラの位置で変わるので `IoriSky.shader` 側で毎フレーム足す)。景色を描く大きさは機種の速さに合わせて自分で上下する(`CampQuality.FitScale`、0.5〜0.85 倍、30fps が目標。落ち着いた値は次の起動に持ち越す。計測と撮影の間は止まる)。重さを測るときは `-ioriPerfSweep`(`PerfSweep`、Android は `adb shell am start ... --es iori -ioriPerfSweep`)。スマホ向けの絵は PC でも `-ioriQuality phone` で出せる。
- iOS は対象外。
