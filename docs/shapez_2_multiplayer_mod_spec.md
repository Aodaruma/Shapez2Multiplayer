# shapez 2 Multiplayer Mod 実装仕様書

## 0. この仕様書の目的

この文書は、Codex に shapez 2 向けマルチプレイ Mod の実装を進めさせるための作業仕様である。

採用方針は **C# / Shapez Shifter / Steam P2P / ホスト権威型** とする。

Rust 実装は初期スコープから除外する。理由は、shapez 2 の公式 Mod API が C# アセンブリをロードする構造であり、Rust を主実装にすると C ABI ブリッジ、ネイティブDLL配布、プラットフォーム別ビルド、クラッシュデバッグ、Steamworks初期化の二重化リスクが増えるためである。

---

## 1. プロジェクト名

仮称: `Shapez2Multiplayer`

Mod ID 仮称: `aod.Shapez2Multiplayer`

---

## 2. 技術スタック

### 必須

- Language: C#
- Target Framework: `netstandard2.1`
- Game: shapez 2 `1.0.x`
- Mod API: Shapez Shifter `1.0.*`
- Transport: Steam P2P
- Steam wrapper: 原則 `Steamworks.NET`
- Serialization: 独自バイナリ serializer
- Compression: 初期は `System.IO.Compression` が使えれば `DeflateStream`、不可なら無圧縮で開始

### 後回し

- Rust native library
- Dedicated server
- Host migration
- 完全ロックステップ同期
- 完全ロールバックネットコード
- クロスプラットフォーム独自リレー

---

## 3. 基本アーキテクチャ

### 採用モデル

P2P 接続だが、ゲーム権威はホストに集中させる。

```text
Steam Lobby
 ├─ Host / Authority
 │   ├─ 正規セーブデータ
 │   ├─ 正規WorldRevision
 │   ├─ 正規コマンド順序
 │   └─ ClientCommandの検証・適用・配信
 │
 ├─ Client A
 │   ├─ 操作コマンド送信
 │   ├─ 限定的なローカル予測
 │   └─ Host確定コマンドを反映
 │
 └─ Client B
     ├─ 操作コマンド送信
     ├─ 限定的なローカル予測
     └─ Host確定コマンドを反映
```

### 同期対象

同期するもの:

- 建物配置
- 建物削除
- ベルト配置
- 回転
- ブループリント貼り付け
- 研究・アップグレード
- ゲーム速度
- Pause / Resume
- Join 時のセーブスナップショット
- 定期 WorldHash

同期しないもの:

- 毎Tickのベルト上アイテム状態
- 毎Tickの機械内部状態
- 描画状態
- UI hover状態
- カメラ状態

---

## 4. 実装フェーズ

### Phase 0: プロジェクト雛形

目的: shapez 2 の Mod として読み込まれる最小構成を作る。

成果物:

- `manifest.json`
- `Shapez2Multiplayer.csproj`
- `Shapez2MultiplayerMod.cs`
- ロガー出力
- ゲーム起動時に Mod 初期化ログが出る

完了条件:

- ゲーム内 Mod Manager で有効化できる
- 起動時に `Shapez2Multiplayer initialized` がログに出る
- Shapez Shifter 依存が正しく解決される

---

### Phase 1: Steam P2P 疎通

目的: Host / Client の2台間でメッセージを送受信できる。

成果物:

- Steam Lobby 作成
- Lobby 参加
- Hello / Welcome
- Ping / Pong
- PlayerId割り当て
- 接続状態HUD

完了条件:

- Host が Lobby を作れる
- Client が Lobby に参加できる
- Ping round-trip が HUD に表示される
- Client切断を検出できる

---

### Phase 2: Join Snapshot

目的: Client が Host の現在ワールドをロードできる。

成果物:

- Host セーブスナップショット取得
- Snapshot chunk 分割
- Snapshot 受信・再構築
- Client 側ロード
- Join 完了後の WorldRevision 同期

完了条件:

- Client が Host と同じワールド状態を表示できる
- Join 完了直後に BuildLayoutHash が一致する
- Snapshot 中にタイムアウトした場合は JoinReject になる

---

### Phase 3: 単一建築・削除同期

目的: 1人の操作が他プレイヤーに反映される。

対象操作:

- 単一建物配置
- 単一建物削除
- ベルト配置
- 回転

完了条件:

- Host 操作が全 Client に反映される
- Client 操作が Host 検証後に全員へ反映される
- 同一座標の同時配置で破綻しない
- Reject された Client 操作はローカルから巻き戻される

---

### Phase 4: ブループリント同期

目的: 大きめの配置操作をコマンドとして同期する。

対象操作:

- Copy
- Cut
- Paste
- Blueprint rotation
- Blueprint flip が存在する場合はそれも対象

方針:

- Blueprint は Host 側で合法性チェックする
- 初期は atomic に扱う
- 一部でも配置できない場合は全体 Reject
- Payload サイズ上限を設ける

完了条件:

- 100 building 程度の Blueprint を同期できる
- 大きすぎる Blueprint は Reject される
- 競合時に Host の順序で確定する

---

### Phase 5: 研究・速度・Pause同期

目的: ワールド進行状態を一致させる。

対象操作:

- ResearchCommand
- UpgradeCommand
- GameSpeedCommand
- PauseCommand
- ResumeCommand

方針:

- GameSpeed と Pause は Host のみ実行可能
- Research / Upgrade は Client から要求可能だが Host が検証する
- 既に完了済みの Research は冪等成功扱いにする

完了条件:

- 研究状態が全員で一致する
- Client が速度変更を直接確定できない
- Pause / Resume が全員に反映される

---

### Phase 6: Desync検出・再同期

目的: 状態ズレを検出し、最低限復旧できる。

成果物:

- BuildLayoutHash
- ResearchStateHash
- WorldRevision
- DesyncReport
- ResyncRequest
- Host Snapshot 再送

完了条件:

- 5秒ごとに Hash を比較できる
- 不一致時に Client が Desync 状態になる
- Resync により Host snapshot へ戻れる

---

### Phase 7: UX整備

目的: 実使用できる最低限の UI を作る。

成果物:

- Main Menu に `Host Multiplayer Game`
- Main Menu に `Join Multiplayer Game`
- Lobby Code / Friend Join
- Player List HUD
- Ping表示
- Host表示
- 他プレイヤーカーソル
- 簡易チャット

完了条件:

- 新規ユーザーがUIだけで Host / Join できる
- 接続状態とエラー理由が画面で分かる

---

## 5. ディレクトリ構造

```text
Shapez2Multiplayer/
  manifest.json
  Shapez2Multiplayer.csproj
  Shapez2MultiplayerMod.cs

  Net/
    INetworkTransport.cs
    SteamMessagesTransport.cs
    SteamLobbyService.cs
    PacketHeader.cs
    PacketReader.cs
    PacketWriter.cs
    PacketSerializer.cs
    NetChannel.cs
    NetSendFlags.cs
    NetStats.cs

  Protocol/
    ProtocolConstants.cs
    MessageType.cs
    JoinRejectReason.cs
    HelloMessage.cs
    WelcomeMessage.cs
    JoinRequestMessage.cs
    JoinAcceptMessage.cs
    JoinRejectMessage.cs
    SnapshotBeginMessage.cs
    SnapshotChunkMessage.cs
    SnapshotEndMessage.cs
    ClientCommandMessage.cs
    AuthoritativeCommandMessage.cs
    CommandAckMessage.cs
    CommandRejectMessage.cs
    WorldHashMessage.cs
    DesyncReportMessage.cs
    ResyncRequestMessage.cs
    CursorMessage.cs
    ChatMessage.cs
    PingMessage.cs
    PongMessage.cs

  Commands/
    ICommand.cs
    CommandType.cs
    BuildCommand.cs
    DeleteCommand.cs
    PasteBlueprintCommand.cs
    ResearchCommand.cs
    UpgradeCommand.cs
    GameSpeedCommand.cs
    PauseCommand.cs
    ResumeCommand.cs

  Authority/
    HostAuthority.cs
    CommandValidator.cs
    CommandSequencer.cs
    WorldRevisionService.cs
    PlayerRegistry.cs
    JoinGate.cs

  Client/
    ClientSession.cs
    PredictionBuffer.cs
    ReconciliationService.cs
    ClientCommandSender.cs

  Hooks/
    HookInstaller.cs
    PlacementHooks.cs
    BlueprintHooks.cs
    ResearchHooks.cs
    SaveLoadHooks.cs
    SimulationTickHooks.cs
    UndoRedoHooks.cs
    GameSpeedHooks.cs

  Sync/
    SnapshotService.cs
    SnapshotChunker.cs
    SnapshotAssembler.cs
    WorldHasher.cs
    DesyncDetector.cs
    ResyncService.cs

  UI/
    MultiplayerMenu.cs
    LobbyPanel.cs
    PlayerListHud.cs
    CursorOverlay.cs
    ChatPanel.cs
    ErrorDialog.cs

  Diagnostics/
    MultiplayerLogger.cs
    DesyncDump.cs
    ReplayLog.cs
    DebugOverlay.cs

  Tests/
    PacketSerializerTests.cs
    CommandSequencerTests.cs
    CommandValidatorTests.cs
    SnapshotChunkerTests.cs
```

---

## 6. manifest.json 仕様

初期案:

```json
{
  "Name": "aod.Shapez2Multiplayer",
  "Version": "0.1.0",
  "Title": "Shapez 2 Multiplayer",
  "Description": "Experimental host-authoritative P2P multiplayer for shapez 2.",
  "Author": "Aod",
  "SavedModVersionCompabilityRangeWithSelf": "<0.2.0",
  "GameVersionSupportRange": "1.0.*",
  "AffectsSaveGames": true,
  "DisablesAchievements": true,
  "Conflicts": [],
  "Assemblies": [
    "Shapez2Multiplayer.dll"
  ],
  "Dependencies": [
    {
      "ModId": "steam:3542611357",
      "ModTitle": "Shapez Shifter",
      "Version": "1.0.*"
    }
  ]
}
```

---

## 7. パケット仕様

### 7.1 Header

すべてのパケットは以下のヘッダを持つ。

```csharp
public readonly struct MpPacketHeader
{
    public const uint MagicValue = 0x50324D53; // "S2MP" little-endian想定

    public uint Magic { get; }
    public ushort ProtocolVersion { get; }
    public ushort MessageType { get; }
    public uint SessionId { get; }
    public uint SenderPlayerId { get; }
    public uint Sequence { get; }
    public uint AckSequence { get; }
    public ulong WorldRevision { get; }
}
```

### 7.2 MessageType

```csharp
public enum MessageType : ushort
{
    Hello = 1,
    Welcome = 2,
    JoinRequest = 3,
    JoinAccept = 4,
    JoinReject = 5,

    SnapshotBegin = 10,
    SnapshotChunk = 11,
    SnapshotEnd = 12,

    ClientCommand = 20,
    AuthoritativeCommand = 21,
    CommandAck = 22,
    CommandReject = 23,

    WorldHash = 30,
    DesyncReport = 31,
    ResyncRequest = 32,

    PlayerCursor = 40,
    PlayerSelection = 41,
    Chat = 42,

    Ping = 50,
    Pong = 51
}
```

### 7.3 Channel

```csharp
public enum NetChannel : int
{
    Control = 0,
    Commands = 1,
    Snapshot = 2,
    Ephemeral = 3,
    Chat = 4
}
```

送信方針:

| Channel | 内容 | 信頼性 |
|---|---|---|
| Control | Hello, Join, Reject, Ping | Reliable |
| Commands | Build/Delete/Paste/Research | Reliable |
| Snapshot | Snapshot chunks | Reliable |
| Ephemeral | Cursor, Selection | Unreliable |
| Chat | Chat | Reliable |

---

## 8. コマンド仕様

### 8.1 共通 interface

```csharp
public interface ICommand
{
    CommandType Type { get; }
    uint LocalCommandId { get; }
    uint IssuerPlayerId { get; }

    void Serialize(PacketWriter writer);
}
```

### 8.2 CommandType

```csharp
public enum CommandType : ushort
{
    Build = 1,
    Delete = 2,
    PasteBlueprint = 3,
    Research = 4,
    Upgrade = 5,
    GameSpeed = 6,
    Pause = 7,
    Resume = 8
}
```

### 8.3 BuildCommand

```csharp
public sealed class BuildCommand : ICommand
{
    public CommandType Type => CommandType.Build;
    public uint LocalCommandId { get; init; }
    public uint IssuerPlayerId { get; init; }

    public string BuildingDefinitionId { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public byte Rotation { get; init; }
    public byte Layer { get; init; }
    public byte[] ExtraPayload { get; init; } = Array.Empty<byte>();

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16((ushort)Type);
        writer.WriteUInt32(LocalCommandId);
        writer.WriteUInt32(IssuerPlayerId);
        writer.WriteString(BuildingDefinitionId);
        writer.WriteInt32(X);
        writer.WriteInt32(Y);
        writer.WriteInt32(Z);
        writer.WriteByte(Rotation);
        writer.WriteByte(Layer);
        writer.WriteBytesWithLength(ExtraPayload, ProtocolLimits.MaxExtraPayloadBytes);
    }
}
```

### 8.4 DeleteCommand

```csharp
public sealed class DeleteCommand : ICommand
{
    public CommandType Type => CommandType.Delete;
    public uint LocalCommandId { get; init; }
    public uint IssuerPlayerId { get; init; }

    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public byte Layer { get; init; }

    public void Serialize(PacketWriter writer) { /* implement */ }
}
```

### 8.5 PasteBlueprintCommand

```csharp
public sealed class PasteBlueprintCommand : ICommand
{
    public CommandType Type => CommandType.PasteBlueprint;
    public uint LocalCommandId { get; init; }
    public uint IssuerPlayerId { get; init; }

    public int AnchorX { get; init; }
    public int AnchorY { get; init; }
    public int AnchorZ { get; init; }
    public byte Rotation { get; init; }
    public byte PlacementMode { get; init; }
    public byte[] BlueprintPayload { get; init; } = Array.Empty<byte>();

    public void Serialize(PacketWriter writer) { /* implement */ }
}
```

---

## 9. プロトコル制限

```csharp
public static class ProtocolLimits
{
    public const ushort ProtocolVersion = 1;
    public const int MaxPacketBytes = 1024 * 1024;
    public const int MaxUnreliablePayloadBytes = 1200;
    public const int MaxExtraPayloadBytes = 64 * 1024;
    public const int MaxBlueprintBytes = 4 * 1024 * 1024;
    public const int MaxSnapshotChunkBytes = 512 * 1024;
    public const int MaxChatBytes = 1024;
    public const int MaxCommandsPerSecondPerClient = 60;
    public const int MaxSnapshotBytes = 512 * 1024 * 1024;
}
```

---

## 10. HostAuthority

### 責務

- ClientCommand を受信する
- rate limit を適用する
- CommandValidator で検証する
- globalSeq を採番する
- Host ワールドへ適用する
- 全 Client へ AuthoritativeCommand を配信する
- Reject 理由を Client に返す

### 疑似コード

```csharp
public sealed class HostAuthority
{
    private readonly CommandSequencer sequencer;
    private readonly CommandValidator validator;
    private readonly WorldRevisionService worldRevision;
    private readonly INetworkTransport transport;

    public void OnClientCommand(uint playerId, ICommand command)
    {
        if (!RateLimitAllows(playerId))
        {
            Reject(playerId, command.LocalCommandId, CommandRejectReason.RateLimited);
            return;
        }

        CommandValidationResult result = validator.Validate(playerId, command);
        if (!result.Success)
        {
            Reject(playerId, command.LocalCommandId, result.Reason);
            return;
        }

        ulong globalSeq = sequencer.Next();
        ApplyToLocalWorld(command);
        ulong revision = worldRevision.Increment();

        BroadcastAuthoritativeCommand(globalSeq, revision, command);
        Ack(playerId, command.LocalCommandId, globalSeq, revision);
    }
}
```

---

## 11. ClientSession

### 責務

- Host へ JoinRequest を送る
- Snapshot を受信する
- ユーザー操作を ClientCommand に変換する
- Host 確定コマンドを適用する
- Reject 時に予測を巻き戻す
- Desync 時に Resync を要求する

### 予測方針

予測あり:

- 単一建物配置
- 単一削除

予測なし:

- Blueprint paste
- Research
- Upgrade
- GameSpeed
- Pause / Resume

---

## 12. Hook 方針

### PlacementHooks

目的:

- プレイヤーが建物を置いた操作を捕捉する
- Client では直接確定せず Host へ送る
- Host または AuthoritativeCommand 適用中は再帰送信しない

必要なフラグ:

```csharp
public static class HookContext
{
    [ThreadStatic]
    public static bool IsApplyingAuthoritativeCommand;

    [ThreadStatic]
    public static bool IsApplyingLocalPrediction;
}
```

### BlueprintHooks

目的:

- Blueprint paste を捕捉する
- Payload 化する
- Host に送信する
- AuthoritativeCommand として反映する

### ResearchHooks

目的:

- 研究実行を捕捉する
- Host 検証へ送る

### SaveLoadHooks

目的:

- Snapshot 取得
- Snapshot ロード
- Join 中の状態遷移制御

### SimulationTickHooks

目的:

- Network pump
- Periodic WorldHash
- Deferred command apply
- UI update

---

## 13. Join フロー

```text
Client -> Host: Hello
Host -> Client: Welcome
Client -> Host: JoinRequest
Host:
  validate gameVersion
  validate modVersion
  validate protocolVersion
  validate modListHash
  assign playerId
Host -> Client: JoinAccept
Host -> Client: SnapshotBegin
Host -> Client: SnapshotChunk * N
Host -> Client: SnapshotEnd
Client:
  assemble snapshot
  load snapshot
  compute BuildLayoutHash
Client -> Host: WorldHash
Host:
  compare hash
  mark player active
```

JoinRejectReason:

```csharp
public enum JoinRejectReason : ushort
{
    Unknown = 0,
    ProtocolMismatch = 1,
    GameVersionMismatch = 2,
    MultiplayerModVersionMismatch = 3,
    ShapezShifterVersionMismatch = 4,
    ModListMismatch = 5,
    SnapshotTooLarge = 6,
    HostBusy = 7,
    SaveIncompatible = 8
}
```

---

## 14. WorldHash

### Phase 6 初期実装

最初は建物配置だけを Hash 化する。

対象:

- BuildingDefinitionId
- Position
- Rotation
- Layer
- 主要 configuration

含めない:

- ベルト上の移動中アイテム
- Renderer state
- UI state

### API

```csharp
public sealed class WorldHasher
{
    public ulong ComputeBuildLayoutHash()
    {
        // TODO: enumerate map entities in stable order
        // TODO: feed definition id, position, rotation, layer into stable hash
        // TODO: return xxHash64 or FNV-1a 64bit
    }

    public ulong ComputeResearchStateHash()
    {
        // TODO
    }
}
```

---

## 15. SnapshotService

### API

```csharp
public sealed class SnapshotService
{
    public byte[] CaptureSnapshot()
    {
        // TODO: use shapez 2 save API or hook existing save serialization
    }

    public void LoadSnapshot(byte[] snapshot)
    {
        // TODO: use shapez 2 load API or hook existing load path
    }
}
```

### 注意

- Snapshot 取得・ロード中はコマンド処理を止める
- Join 中に Host 側のゲームを完全 Pause するかは設定化する
- 初期は Host が Join 中に短時間 Pause してよい

---

## 16. エラー処理

### NetworkError

```csharp
public enum NetworkError
{
    None,
    SteamNotInitialized,
    LobbyCreateFailed,
    LobbyJoinFailed,
    PeerTimeout,
    PacketTooLarge,
    InvalidMagic,
    InvalidProtocol,
    DeserializationFailed,
    TransportSendFailed
}
```

### CommandRejectReason

```csharp
public enum CommandRejectReason : ushort
{
    Unknown = 0,
    RateLimited = 1,
    InvalidPayload = 2,
    UnknownBuilding = 3,
    PositionOutOfRange = 4,
    PlacementBlocked = 5,
    ResearchLocked = 6,
    PermissionDenied = 7,
    BlueprintTooLarge = 8,
    WorldRevisionTooOld = 9
}
```

---

## 17. Security / Robustness

すべての Client 由来 payload を検証する。

必須検証:

- Packet Magic
- ProtocolVersion
- MessageType
- Payload length
- String length
- Blueprint size
- Snapshot size
- Command rate
- Building ID existence
- Position bounds
- Research availability
- Permission

禁止:

- Client から任意アセンブリ名を受け取ってロードする
- Client から任意ファイルパスを受け取って読み書きする
- Snapshot 受信中に上限なしメモリ確保を行う

---

## 18. 他Mod互換

初期は同一Modセット必須。

JoinRequest に含める:

```csharp
public sealed class ModIdentity
{
    public string ModId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public bool AffectsSaveGames { get; init; }
}
```

比較対象:

- GameVersion
- ShapezShifterVersion
- MultiplayerModVersion
- EnabledModIds
- EnabledModVersions
- LoadOrder

不一致時:

- JoinReject: `ModListMismatch`

---

## 19. ログ

ログカテゴリ:

- `MP_INIT`
- `MP_STEAM`
- `MP_LOBBY`
- `MP_NET`
- `MP_JOIN`
- `MP_COMMAND`
- `MP_SNAPSHOT`
- `MP_DESYNC`
- `MP_UI`

ログ例:

```text
[MP_INIT] Shapez2Multiplayer initialized version=0.1.0 protocol=1
[MP_LOBBY] Created lobby id=...
[MP_JOIN] JoinRequest accepted playerId=2 steamId=...
[MP_COMMAND] Accepted Build local=44 global=812 revision=1512 player=2
[MP_DESYNC] BuildLayoutHash mismatch player=2 host=... client=...
```

---

## 20. テスト方針

### Unit Tests

対象:

- PacketWriter / PacketReader
- PacketSerializer
- CommandSequencer
- SnapshotChunker
- RateLimiter
- WorldRevisionService

### Integration Tests

手動でよい初期検証:

- 2つのローカルSteamアカウント、または2台PC
- Host作成
- Client参加
- Ping確認
- Snapshot同期
- 単一建築同期
- 同時配置競合
- Client切断
- 再参加

---

## 21. Codex 作業順序

Codex は次の順で実装する。

### Task 1: 雛形作成

- `manifest.json` を作成
- `.csproj` を作成
- `Shapez2MultiplayerMod.cs` を作成
- logger を受け取り初期化ログを出す

Acceptance:

- ビルド可能
- Mod がロード可能

### Task 2: Protocol 基盤

- `MessageType`
- `PacketHeader`
- `PacketWriter`
- `PacketReader`
- `ProtocolLimits`
- serializer tests

Acceptance:

- Header roundtrip test が通る
- 不正 Magic を reject する
- 最大サイズ超過を reject する

### Task 3: Transport 抽象

- `INetworkTransport`
- `SteamMessagesTransport` skeleton
- `LoopbackTransport` for tests

Acceptance:

- Loopback で Hello / Welcome を送受信できる

### Task 4: LobbyService

- Host lobby 作成
- Lobby 参加
- player identity 管理

Acceptance:

- Host / Join のメソッドが存在する
- Steam 未初期化時に明確なエラーを返す

### Task 5: Ping/Pong

- PingMessage / PongMessage
- RTT 計測
- Debug overlay または logger 表示

Acceptance:

- 1秒ごとに RTT が更新される

### Task 6: Commands skeleton

- ICommand
- BuildCommand
- DeleteCommand
- ClientCommandMessage
- AuthoritativeCommandMessage
- CommandAck / Reject

Acceptance:

- BuildCommand が serialize / deserialize 可能

### Task 7: HostAuthority skeleton

- CommandSequencer
- CommandValidator stub
- BroadcastAuthoritativeCommand

Acceptance:

- LoopbackTransport で ClientCommand -> AuthoritativeCommand の流れが動く

### Task 8: Hook調査用ログ

- PlacementHooks にログのみ仕込む
- 実際の配置関数/イベントを特定する

Acceptance:

- ゲーム内で建物を置いたときに building id / position / rotation がログに出る

### Task 9: 単一建築同期

- Client 操作を Host へ送信
- Host で配置適用
- AuthoritativeCommand を全員へ配信

Acceptance:

- 2台で単一建物配置が同期される

### Task 10: 削除同期

- DeleteCommand 実装
- 同期適用

Acceptance:

- 2台で削除が同期される

---

## 22. 実装上の重要ルール

1. Host が唯一の権威である。
2. Client は確定ワールド状態を直接決めない。
3. すべての ClientCommand は検証する。
4. Payload は必ずサイズ上限を持つ。
5. AuthoritativeCommand 適用中は Hook が再送信しない。
6. 初期は Classic mode を優先する。
7. Manufacture Mode は後から対応する。
8. Host migration は実装しない。
9. Rust は初期実装に入れない。
10. Desync は完全予防ではなく検出・再同期で扱う。

---

## 23. 未確定事項

Codex は以下を調査しながら実装する。

- shapez 2 の正確な配置確定メソッド
- 削除操作の確定メソッド
- Blueprint payload の内部形式
- セーブデータの取得・ロード API
- 研究状態の取得・変更 API
- Map entity enumeration の安定順序
- Steamworks.NET を Mod DLL から安全に利用できるか
- Game 内部の Steam API 初期化済みインスタンスを共有できるか

未確定事項がブロッカーになった場合は、まずログ用 Hook を追加し、実際の型名・メソッド名・引数を特定すること。

---

## 24. 初期リリース条件

`0.1.0-experimental` の公開条件:

- Host / Join 可能
- Snapshot 同期可能
- 単一建築同期可能
- 単一削除同期可能
- Ping表示可能
- Mod list mismatch を検出可能
- Desync検出の最低限ログあり
- 実績無効化
- セーブ影響ありとして manifest 設定済み

README には必ず以下を明記する。

```text
This is an experimental multiplayer mod.
Back up your saves before use.
Only identical game/mod versions are supported.
Host migration is not supported.
Desyncs may require rejoining the session.
```

