# LimitResetWatch

Claude・Codex・Gemini・Grok のサブスクの利用枠が「あとどれだけでリセットされるか」を、Windows のタスクトレイと小さな浮かぶ窓に出し続ける常駐アプリ。リセット前に枠を使い切るための目安にする。DeepSeek の残高も浮かぶ窓に出す。

## 表示

### トレイアイコン（サービスごとに 1 個）

- **数字**: 枠がリセットされるまでの時間（`45m` = 45 分、`18h` = 18 時間、`5d` = 5 日）。
- **下のバー**: その枠の使用率。
- **バーの上の目盛り**: 枠の期間のうち経過した割合。使用率のバーが目盛りより左なら、まだ使い切るペースに届いていない。
- アイコンに出す枠は、まだリセットされていない枠のうち **最も長い枠**（週枠など）。同じ長さの枠が複数あれば、リセットが早いほう。
- マウスを乗せると、全部の枠の使用率と残り時間が出る。左クリックで浮かぶ窓の表示・非表示、右クリックでメニュー。
- 値を取得できないときは、前回の値を薄い色で出す（リセット時刻は分かっているので、残り時間は数え続ける）。

Windows 11 では、新しいトレイアイコンは最初「∧」の中に隠れる。常に見えるようにするには、「∧」の中のアイコンをタスクバーへドラッグするか、設定 → 個人用設定 → タスク バー → 「その他のシステム トレイ アイコン」でオンにする。Windows はアイコンごとに覚えるので、サービスごとにオンにする。アイコンは起動のたびに同じ順（Claude・Codex・Gemini・Grok・アプリのアイコン）で作るので、この設定は次の起動でも保たれる。

### 浮かぶ窓

全サービスの全部の枠を、使用率・経過の目盛り・残り時間・リセット日時つきで並べる。常に最前面に出るが、フォーカスは奪わず、Alt+Tab にも出ない。ドラッグで動かすと位置を覚える。Windows のテーマ（明るい / 暗い）に合わせて色が変わる。

### メニュー（右クリック）

| 項目 | 内容 |
|---|---|
| 浮かぶ窓を表示 | 浮かぶ窓の表示・非表示 |
| 今すぐ更新 | すぐに取り直す |
| 更新間隔 | 1 / 5 / 15 / 30 分（既定は 5 分）。残り時間の表示は 30 秒ごとに進む |
| Windows の起動時に開始 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` に登録する |
| 終了 | 終了する |

## サービスごとの取得元と前提

各 CLI・アプリがこの PC に保存しているログイン情報を読むだけで、書き換えはしない。ログイン情報は、それを発行したサービスにだけ送る。

| サービス | 出る値 | 取得元 | 前提 |
|---|---|---|---|
| Claude | 5時間枠・週枠（モデル別の枠を含む） | `api.anthropic.com/api/oauth/usage` | Claude デスクトップアプリか Claude Code（`~/.claude/.credentials.json`）でログインしていること。期限の遅いほうを使う |
| Codex | 週枠など | `chatgpt.com/backend-api/wham/usage` | `~/.codex/auth.json`（ChatGPT でログイン）。API が失敗したら最後のセッションログの値を出す |
| Gemini | Google AI Pro の週枠・5時間枠（モデルのグループ別） | 起動中の Antigravity（`agy.exe`）のローカルサーバー | Antigravity が起動していること |
| Grok | 週枠と製品別の内訳 | `cli-chat-proxy.grok.com/v1/billing` | `~/.grok/auth.json` のログインが有効なこと（1 時間ほどで切れる。切れていたら `grok` を起動すると更新される） |
| DeepSeek | 残高（浮かぶ窓だけ） | `api.deepseek.com/user/balance` | 環境変数 `DEEPSEEK_API_KEY` |

Claude Code のログインは数時間で切れ、`claude` を使うまで更新されない。そのため、Claude デスクトップアプリが保存しているログイン（`%APPDATA%\Claude\config.json` の `oauth:tokenCache`。Windows の DPAPI と AES-GCM で暗号化されている）も読む。

どの取得元も公開された API ではないため、各サービスの変更で取れなくなることがある。

## ビルドと実行

.NET 10 の SDK が必要（Windows 専用）。

```bash
dotnet build LimitResetWatch.slnx
```

```bash
dotnet run --project src/LimitResetWatch
```

テスト:

```bash
dotnet test LimitResetWatch.slnx
```

### 見た目の確認用の書き出し

画面を出さずに 1 回取得し、取得結果（`snapshots.json`）・トレイアイコンの拡大図（`tray-icons.png`）・浮かぶ窓（`floating-*.png`）を書き出して終わる。

```bash
dotnet run --project src/LimitResetWatch -- --dump dump
```

## 保存するもの

`%APPDATA%\LimitResetWatch\` に次の 2 つを置く。ログイン情報は保存しない。

- `settings.json`: 更新間隔・浮かぶ窓の表示と位置
- `snapshots.json`: 最後に取れた値（起動直後やログイン切れのときに、リセットまでの時間を出すため）

## 出典

- 取得処理は、同じ作者の CLI `ai-usage`（`usage.mjs`）を C# に移したもの。
- Claude デスクトップアプリのログインを読む場所と、使ってよいトークンの条件は [Gengchou](https://github.com/ynjmxn/gengchou)（MIT License）の `src/claude_desktop.rs` を参考にした。表示の構成（トレイアイコンと浮かぶ窓）も Gengchou を参考にしている。
