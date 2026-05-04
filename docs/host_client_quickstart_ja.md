# Shapez2Multiplayer 簡易指示書（ホスト/クライアント）

最終更新: 2026-05-04

## 1. 現在の到達点（重要）

- 本 Mod は **0.1.0 の実験段階**です。
- 現在は「Mod ロード確認」「プロトコル/Transport/Authority の土台実装」まで完了しています。
- **実ゲーム内の Host/Join UI と実接続フローは未実装**です（これから実装予定）。
- そのため、現時点では 2 クライアントでの実接続検証はまだできません。

## 2. 前提条件（ホスト/クライアント共通）

1. shapez 2（1.0.x）
2. Shapez Shifter（steam:3542611357）
3. 同一バージョンの Mod ファイル

## 3. ホスト側の導入手順（開発環境）

リポジトリのルートで実行:

```powershell
dotnet build .\Shapez2Multiplayer.csproj -c Release -p:DeployToShapez2Mods=true
```

配置先（既定）:

`C:\Users\aod\AppData\LocalLow\tobspr Games\shapez 2\mods\aod.Shapez2Multiplayer`

配置される主要ファイル:

- `Shapez2Multiplayer.dll`
- `manifest.json`

## 4. クライアント側の導入手順（検証参加者向け）

1. Steam で shapez 2 と Shapez Shifter を導入
2. ホスト側と同一の `aod.Shapez2Multiplayer` フォルダを受け取る
3. クライアント PC の以下へ配置  
   `C:\Users\<ユーザー名>\AppData\LocalLow\tobspr Games\shapez 2\mods\aod.Shapez2Multiplayer`
4. shapez 2 を起動

## 5. ロード確認（ホスト/クライアント共通）

ログファイル:

`C:\Users\<ユーザー名>\AppData\LocalLow\tobspr Games\shapez 2\Player.log`

以下のような行があればロード成功:

```text
Loading C:/Users/.../mods/aod.Shapez2Multiplayer/Shapez2Multiplayer.dll
aod.Shapez2Multiplayer[0.1.0] [MP_INIT] Shapez2Multiplayer initialized version=0.1.0 protocol=1
```

## 6. 接続方法について（現時点）

- **未実装**です。  
  `Host Multiplayer Game` / `Join Multiplayer Game` は将来フェーズ（仕様書 Phase 7）で追加予定です。
- 現時点で確認できるのは「Mod が正しくロードされること」までです。

## 7. 実接続実装後の予定運用（先行案）

実装後は次の流れを想定しています。

1. ホストが `Host Multiplayer Game` を選択
2. クライアントが `Join Multiplayer Game` で Lobby Code/Friend Join
3. Join Snapshot 受信後に同期開始
4. HUD で Ping と接続状態を確認
