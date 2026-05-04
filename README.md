# Shapez2Multiplayer

shapez 2 向けの実験的マルチプレイ Mod（ホスト権威型 P2P）です。  
詳細仕様は [docs/shapez_2_multiplayer_mod_spec.md](docs/shapez_2_multiplayer_mod_spec.md) を参照してください。

## Important Notes

This is an experimental multiplayer mod.  
Back up your saves before use.  
Only identical game/mod versions are supported.  
Host migration is not supported.  
Desyncs may require rejoining the session.

## 採用方針（仕様固定）

- 言語: C#
- ターゲット: `netstandard2.1`
- ゲーム: shapez 2 `1.0.x`
- Mod API: Shapez Shifter `1.0.*`
- 通信: Steam P2P（`Steamworks.NET` 想定）
- 同期モデル: Host-authoritative（クライアントは要求送信、確定はホスト）

## 現状

- 2026-05-04 時点では、実装コードは未着手で仕様書中心の状態です。
- これから Phase 0（雛形作成）から順に実装します。

## 開発環境の前提

- Windows + Steam
- shapez 2 本体（`1.0.x`）
- Shapez Shifter（`1.0.*`）
- .NET SDK（`8.x` 以上推奨。`netstandard2.1` のビルドに使用）
- Git

## セットアップ手順（Phase 0）

1. Steam で shapez 2 をインストールし、起動確認する
2. Shapez Shifter（`steam:3542611357`）を導入する
3. このリポジトリで Mod 雛形を作成する

```powershell
dotnet new classlib --framework netstandard2.1 --name Shapez2Multiplayer --output . --force
```

4. `manifest.json` と Mod エントリクラス（`Shapez2MultiplayerMod.cs`）を追加する
5. Shapez Shifter / Steamworks.NET 参照を `.csproj` に設定する
6. ビルドして DLL を出力する

```powershell
dotnet build .\Shapez2Multiplayer.csproj -c Release
```

7. 生成 DLL と `manifest.json` を shapez 2 の Mod 配置先に配置して読み込み確認する
8. このリポジトリでは次のコマンドで自動配置できる（`DeployToShapez2Mods` ターゲット）

```powershell
dotnet build .\Shapez2Multiplayer.csproj -c Release -p:DeployToShapez2Mods=true
```

## このリポジトリでの事前確認結果（2026-05-04）

- `dotnet --version`: `8.0.420`
- `git --version`: `2.54.0.windows.1`
- shapez 2 本体: `D:\SteamLibrary\steamapps\common\shapez 2`
- Shapez Shifter: `D:\SteamLibrary\steamapps\workshop\content\2162800\3542611357\ShapezShifter.dll`
- ローカル Mod 配置先（既定）: `C:\Users\aod\AppData\LocalLow\tobspr Games\shapez 2\mods\aod.Shapez2Multiplayer`

上記から、ローカルの .NET ビルド基盤は利用可能です。  
ただし、実際にゲーム連携まで確認するには shapez 2 / Shapez Shifter / Steam 実行環境が必要です。

## 実装ロードマップ（仕様準拠）

1. Phase 0: Mod 雛形（`manifest.json`, `Shapez2Multiplayer.csproj`, エントリクラス）
2. Phase 1: Steam Lobby と Ping/Pong
3. Phase 2: Join Snapshot（分割転送・再構築）
4. Phase 3: 単一建築 / 削除同期
5. Phase 4: Blueprint 同期
6. Phase 5: 研究 / 速度 / Pause 同期
7. Phase 6: Desync 検出と Resync
8. Phase 7: UI/UX（Host/Join メニュー、Player List、簡易チャット）

## 直近の実装手順（Task 1〜10）

1. Task 1: 雛形作成（ビルド可能・Modロード可能）
2. Task 2: Protocol 基盤（Header/Reader/Writer/Tests）
3. Task 3: Transport 抽象（`INetworkTransport` / Loopback）
4. Task 4: LobbyService（Host/Join API）
5. Task 5: Ping/Pong（RTT 表示）
6. Task 6: Commands skeleton（Build/Delete など）
7. Task 7: HostAuthority skeleton（検証・採番・配信）
8. Task 8: Hook 調査ログ（配置イベント特定）
9. Task 9: 単一建築同期
10. Task 10: 削除同期

## 未確定の技術調査ポイント

- shapez 2 の配置/削除確定 API
- Blueprint payload 形式
- Save snapshot 取得/ロード API
- 研究状態の取得/反映 API
- Steamworks.NET を Mod DLL で安全に使うための初期化連携

これらがブロッカー化した場合は、まず Hook ログを追加して実際の型・メソッドを特定する方針です。
