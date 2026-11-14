# IORI — エージェント向けメモ

コードの主導権はエージェントにある。見た目の調整と「美しいか」の判断は chico が Unity Editor で行う。
ユーザーへの応答は日本語の柔らかいタメ口(〜したよ)。

## 決まり

- 選曲などのロジックは `Assets/IORI/Runtime/Audio/Core/` に置き、UnityEngine を参照しない(asmdef の `noEngineReferences: true`)。テストは `Assets/IORI/Tests/EditMode/` に書き、Unity なしでも `dotnet test tools/CoreTests` で通ること。
- 何を鳴らすかを決めるのは `RadioScheduler` だけ。Android の `PlanEngine.java` と PC の `UnityAudioBackend.cs` は予定表を鳴らすだけで、同じ決まり(フェードの式、再生位置の式、消えたクリップのフェードアウト、単発音の遅延許容)を持つ。片方を変えたらもう片方も直す。
- 予定表の JSON の形は `PlaybackPlan.cs` が正。`Plan.java` はそれに合わせる。
- 音楽プレーヤー(端末の曲)の鳴らす順を決めるのは `PlayQueue` だけ。再生係(`IMusicBackend`: PC は `UnityMusicBackend`、Android は `AndroidMusicBackend` と再生サービスの中の `MusicEngine.java`)は渡された列を鳴らすだけで、曲が終わったときの決まり(くり返しなしは最後で止まる、全部は最初へ回る、1 曲は同じ曲、読めない曲は飛ばす)を同じにする。片方を変えたらもう片方も直す。鳴らすのはラジオかプレーヤーのどちらか片方(`MusicDirector.Active` の間、ラジオは黙って環境音だけ。chico が決めた仕様)。
- Android の曲の一覧は `MusicStore.java` が MediaStore から集めて JSON で渡し、Core の `MediaStoreJson.cs` が読む。形を変えたら両方直す。集めるのは `AndroidMusicBackend.Folder`(既定は `PC-Music`)という名前のフォルダの下だけ。
- Android のネイティブコードは Java(Unity 6 の .androidlib で Kotlin プラグインを抱えないため)。
- Unity のコンパイル、PlayMode、実機ビルドは chico のデスクトップ(`C:\ai\iori`)でしかできない。クラウドでは Core の .NET テストまで。
- 本番の UI は `Assets/IORI/Runtime/UI/` にあり、uGUI と TextMeshPro をコードで組む(プレハブなし)。形(面、線、影、ガラス)はシェーダー `Resources/IoriUI/Shape.shader` 1 本で描き、画像素材は足さない(アイコンだけ PNG)。寸法は 390 幅の案の数値をそのまま使う(`UiKit` は左上からの位置で置く)。スマホは縦持ちだけ。
- UI の見た目の決まり(chico のレビューから): 角は丸めない。光るぼかし(グロー)は使わない。文字は中身(時間帯の名前、曲名、時刻)だけにして、説明やラベルは置かず配置とアイコンで見せる。ボタンはアイコンだけ(丸い台は置かない)。パネルは画面の下端にぴったり付く藍のガラスのシート。琥珀色は「いま鳴っている」ことを示す細い印(針、いまの時間帯の下線、再生位置、レベルの棒)だけ。色と寸法は `UiTheme` に置く。
- UI を変えたら、デスクトップのビルドを `-ioriUiShots <フォルダ>` で起動して実際の画面を撮り(`UiShotRunner`)、見た目の案と見比べてから chico に見せる。音楽プレーヤーの画面は見本の曲で撮る(`-ioriUiShotJackets Docs/ui-shots/sample-jackets` を足すと見本のジャケットが付く)。chico の PC の曲は撮らない。
- iOS は対象外。
