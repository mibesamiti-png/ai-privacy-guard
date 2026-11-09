# First-time GitHub setup / 初回導入手順

## 重要

このキットはR4.2ソースのGitHub導入版です。**GitHubへのアップロードやWindows Actions実行は、このファイルを作った時点では未実施**です。アカウントの代わりにGitHubリポジトリを作る権限がないため、下記の操作が必要です。

### 1. 非公開リポジトリを作成

1. https://github.com/new を開く。
2. Owner = `mibesamiti-png`、Repository name = `ai-privacy-guard`。
3. Visibility = **Private**。
4. README、.gitignore、ライセンスの自動追加は**すべてオフ**（空のリポジトリ）。
5. `Create repository` を押す。

### 2. 初回アップロード

1. 配布ZIPをPCで解凍。
2. 解凍したプロジェクトフォルダで PowerShell を起動。
3. 下記を実行（コマンド内にトークンは不要）。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\first-push.ps1
```

4. 操作確認の `YES` と、コミット対象のファイル名を見て `PUSH` を入力。
5. GitHubログイン画面が出た場合、Git Credential Managerに従って認証する。

GitHub Desktopや既存のGit機能でアップロードする場合も、**このフォルダ内容をリポジトリのルート**へ配置する。ZIPファイルを1個だけアップロードしてもActionsは起動しない。

### 3. GitHub Actionsの確認

- https://github.com/mibesamiti-png/ai-privacy-guard/actions を開く。
- `Windows Build and Tests` の初回実行結果を確認する。
- 成功した場合、実行詳細の **Artifacts → AIPrivacyGuard-R4.2-Windows-x64** から出力物を取得する。
- 失敗した場合、赤いステップを開き、**エラーログ全文**を取得して修正する。成功するまで『ビルド済み』とは扱わない。

### 4. 注意事項

- `AIPrivacyGuard.exe` が生成されても、アプリ起動・映像検出精度・配信の安全性が保証されるわけではない。
- FFmpeg / FFprobe は外部ランタイム依存。成果物に同梱していない。
- `samples` は合成素材。個人的な画像、動画、レビュー情報、APIキー、学習データ、モデル本体をGitHubに追加しない。
- PrivateでもGitHubは外部サービス。アップロード前にステージしたファイル一覧を確認する。
- PrivateリポジトリのActions実行分数/保存容量にはプランに応じた上限がある。
