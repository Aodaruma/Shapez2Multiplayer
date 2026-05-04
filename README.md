# Shapez2Multiplayer

shapez 2 向けの実験的マルチプレイ Mod（ホスト権威型 P2P）です。  
詳細仕様は [docs/shapez_2_multiplayer_mod_spec.md](docs/shapez_2_multiplayer_mod_spec.md) を参照してください。  
接続の簡易手順は [docs/host_client_quickstart_ja.md](docs/host_client_quickstart_ja.md) にあります。

## Important Notes

This is an experimental multiplayer mod.  
Back up your saves before use.  
Only identical game/mod versions are supported.  
Host migration is not supported.  
Desyncs may require rejoining the session.

## 現在のステータス（2026-05-05）

- Mod のロードと初期化ログ出力（`[MP_INIT]`）は確認済み
- インゲーム Debug UI（`F8` で表示切替）を実装済み
- `Host Lobby` / `Join Lobby` / `Leave Lobby` / `Copy Lobby ID` / `Join From Clipboard`（`F9`）を実装済み
- `Hello/Welcome` と `Ping/Pong` による P2P 基本疎通、接続状態ログ（`[MP_NET] Status ...`）を実装済み
- ホスト側での Lobby 作成ログ確認は完了
- 建築・削除・Blueprint・Snapshot の実ワールド同期は未実装（次フェーズ）

## 必要環境

- Windows + Steam
- shapez 2（`1.0.x`）
- Shapez Shifter（`steam:3542611357`）
- .NET SDK `8.x`
- Git

## 開発セットアップ

1. shapez 2 と Shapez Shifter をインストールし、最低1回起動する
2. このリポジトリをクローンする
3. 参照パスを確認する（必要ならビルド引数で上書き）

既定値は [`Shapez2Multiplayer.csproj`](Shapez2Multiplayer.csproj) にあります。

- `GameManagedDir` 例: `D:\SteamLibrary\steamapps\common\shapez 2\shapez 2_Data\Managed`
- `ShapezShifterDir` 例: `D:\SteamLibrary\steamapps\workshop\content\2162800\3542611357`
- `ModDeployDir` 例: `C:\Users\<user>\AppData\LocalLow\tobspr Games\shapez 2\mods\aod.Shapez2Multiplayer`

## ビルド

ゲーム連携あり（通常開発用）:

```powershell
dotnet build .\Shapez2Multiplayer.csproj -c Release
```

ビルド後に Mod フォルダへ自動配置:

```powershell
dotnet build .\Shapez2Multiplayer.csproj -c Release -p:DeployToShapez2Mods=true
```

CI/ヘッドレス検証用（ゲーム DLL なし）:

```powershell
dotnet build .\Shapez2Multiplayer.csproj -c Release -p:UseGameAssemblies=false
dotnet test .\tests\Shapez2Multiplayer.Tests\Shapez2Multiplayer.Tests.csproj -c Release -p:UseGameAssemblies=false
```

## Host / Join の使い方（現行 UI）

1. shapez 2 を起動してワールドを開く
2. `F8` で `Shapez2Multiplayer Debug` パネルを表示する
3. ホスト側で `Host Lobby` を押す
4. `Copy Lobby ID` で ID をコピーしてクライアントへ共有する
5. クライアント側で次のいずれかを実行する
- `Join Lobby ID` に貼り付けて `Join Lobby`
- クリップボードに ID を入れた状態で `Join From Clipboard`
- クリップボードに ID を入れた状態で `F9`
6. `Status` / `Connected Peers` / `RTT` と `Player.log` を確認する

## ログ

ログファイル:

`C:\Users\<user>\AppData\LocalLow\tobspr Games\shapez 2\Player.log`

主なプレフィックス:

- `[MP_INIT]`: 初期化
- `[MP_LOBBY]`: Host/Join/Leave
- `[MP_NET]`: Hello/Welcome/Ping/Pong/接続状態
- `[MP_UI]`: UI 操作

## CI/CD

- [`ci-main.yml`](.github/workflows/ci-main.yml)
- `main` への push で自動テスト
- `UseGameAssemblies=false` でユニットテスト実行

- [`release-tag.yml`](.github/workflows/release-tag.yml)
- `v*` タグ push でリリース自動公開
- 実ゲーム DLL 参照が必要なため self-hosted Windows runner 前提
- 成果物は `aod.Shapez2Multiplayer` フォルダ構成の zip

## 依存ファイルで詰まった場合の対処

1. self-hosted runner を用意する
- shapez 2 と Shapez Shifter を導入した Windows マシンに GitHub Runner を登録
- runner ラベルに `self-hosted`, `windows`, `shapez2-mod` を付与

2. リポジトリ変数を設定する
- `GAME_MANAGED_DIR`
- `SHAPEZ_SHIFTER_DIR`

3. まずは CI モードで品質確認を進める
- `UseGameAssemblies=false` で build/test を先行
- 実機依存箇所は手元または self-hosted で段階検証
