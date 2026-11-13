# IORI — エージェント向けメモ

コードの主導権はエージェントにある。見た目の調整と「美しいか」の判断は chico が Unity Editor で行う。
ユーザーへの応答は日本語の柔らかいタメ口(〜したよ)。

## 決まり

- 選曲などのロジックは `Assets/IORI/Runtime/Audio/Core/` に置き、UnityEngine を参照しない(asmdef の `noEngineReferences: true`)。テストは `Assets/IORI/Tests/EditMode/` に書き、Unity なしでも `dotnet test tools/CoreTests` で通ること。
- 何を鳴らすかを決めるのは `RadioScheduler` だけ。Android の `PlanEngine.java` と PC の `UnityAudioBackend.cs` は予定表を鳴らすだけで、同じ決まり(フェードの式、再生位置の式、消えたクリップのフェードアウト、単発音の遅延許容)を持つ。片方を変えたらもう片方も直す。
- 予定表の JSON の形は `PlaybackPlan.cs` が正。`Plan.java` はそれに合わせる。
- Android のネイティブコードは Java(Unity 6 の .androidlib で Kotlin プラグインを抱えないため)。
- Unity のコンパイル、PlayMode、実機ビルドは chico のデスクトップ(`C:\ai\iori`)でしかできない。クラウドでは Core の .NET テストまで。
- 本番の UI は `Assets/IORI/Runtime/UI/` にあり、uGUI と TextMeshPro をコードで組む(プレハブなし)。形(角丸、光、影、ガラス)はシェーダー `Resources/IoriUI/Shape.shader` 1 本で描き、画像素材は足さない(アイコンだけ PNG)。寸法は 390 幅の案の数値をそのまま使う(`UiKit` は左上からの位置で置く)。
- UI の色の決まり: 暖色(琥珀)は「いま鳴っている、点いている」ものだけ。それ以外は藍のガラスと薄い藤色の文字。色は `UiTheme` に置く。
- UI を変えたら、デスクトップのビルドを `-ioriUiShots <フォルダ>` で起動して実際の画面を撮り(`UiShotRunner`)、見た目の案と見比べてから chico に見せる。
- iOS は対象外。
