# Shapez2Multiplayer 簡易指示書（ホスト/クライアント）

最終更新: 2026-05-05

## 1. 現在の到達点（重要）

- 本 Mod は **0.1.0 の実験段階**です。
- 現在は「Mod ロード」「インゲーム Debug UI」「Lobby Host/Join の基本疎通（Hello/Welcome/Ping/Pong）」に加えて、Shadow World の Snapshot 同期と Build/Delete コマンド同期（ネットワーク層）を実装済みです。
- **ホスト側の Lobby 作成ログ確認は完了**しています。
- shapez 2 実ワールドの建築/削除に直接反映する Hook は未実装です。

## 2. 前提条件（ホスト/クライアント共通）

1. shapez 2（1.0.x）
2. Shapez Shifter（steam:3542611357）
3. 同一バージョンの `aod.Shapez2Multiplayer` フォルダ

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
3. クライアント PC の以下へ配置する  
   `C:\Users\<ユーザー名>\AppData\LocalLow\tobspr Games\shapez 2\mods\aod.Shapez2Multiplayer`
4. shapez 2 を起動する

## 5. ロード確認（ホスト/クライアント共通）

ログファイル:

`C:\Users\<ユーザー名>\AppData\LocalLow\tobspr Games\shapez 2\Player.log`

以下のような行があればロード成功:

```text
Loading C:/Users/.../mods/aod.Shapez2Multiplayer/Shapez2Multiplayer.dll
aod.Shapez2Multiplayer[0.1.0] [MP_INIT] Shapez2Multiplayer initialized version=0.1.0 protocol=1
```

## 6. 接続手順（現行 UI）

1. ホスト側でワールドを開く
2. `F8` で Debug UI を表示
3. `Host Lobby` を押す
4. `Copy Lobby ID` で ID を共有
5. クライアント側で次のいずれかを実行
- `Join Lobby ID` にIDを貼り付けて `Join Lobby`
- クリップボードにIDをコピーした状態で `Join From Clipboard`
- クリップボードにIDをコピーした状態で `F9`
6. `Status`, `Connected Peers`, `RTT` と `Player.log` を確認

## 7. 注意事項

- Debug UI 下部の `Build/Delete Command Test` で、コマンド送受信と `World Revision` / `World Entities` の変化を検証できます。
- 現段階では実ワールドへの直接反映 Hook が未実装のため、実プレイ同期ではなくネットワーク同期確認が主目的です。
- 実験版のため、セーブデータのバックアップを推奨します。
