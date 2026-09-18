# Murder Mystery - s&box (Source 2)

A multiplayer social deduction game for the [s&box](https://sbox.game) platform (Source 2, C#). Fully released and playable directly on sbox.game.

The server hosts a shared lobby (matchmaking, trading, shooting range, shop, leaderboards) along with multiple isolated game rooms. Gameplay follows the classic formula: Murderer vs. Innocents, with one Innocent assigned a Pistol.

**Tech Stack:** C# 13 / .NET 10, s&box API (Source 2), Razor UI, host-authoritative netcode, cloud KV backend.

| Metric | Value |
|---|---|
| Gameplay Code | ~11,300 lines of C# |
| UI | ~12,200 lines of Razor (35 components) |
| Localization | EN / RU (666 keys) |
| Capacity | Up to 64 players per server (6–16 per room) |
| Manual Test Cases | ~200 test scenarios (P0–P2) covering gameplay, network sync, and UI flows |

---

## Round Logic & Rules

The game loop is driven by the `GameRoom.GameState` state machine (`WaitingForPlayers`, `StartingRound`, `Playing`, `RoundEnd`). State machine logic executes **strictly on the host** (`GameRoom.RunHostStateMachine()`).

### State Transitions:
1. **WaitingForPlayers:** Waits for a minimum of 6 players → starts a 30-second countdown.
2. **StartingRound (5s):** Selects and assigns roles, spawns coins across the map.
3. **Playing (300s):** Core gameplay loop with continuous win-condition checks.
4. **RoundEnd (10s):** Displays results, awards rewards, resets the room, and returns players to the lobby.

### Roles & Mechanics:
* **Murderer:** Equipped with a knife for instant kills (120-unit hitscan, 5s cooldown). Possesses increased stamina and a faster sprint multiplier (×1.4). Gains a radar near the end of the round that reveals surviving Innocents through walls.
* **Detective:** Armed with a pistol with infinite ammo (5s cooldown). Shooting an Innocent kills both the Innocent and the Detective. Drops the pistol upon death.
* **Innocent:** Unarmed by default. Can pick up a dropped pistol (single-use). Picking up a fallen Detective's pistol promotes the Innocent to Detective.

---

## Architecture & Optimization

* **Room Scope Culling (`RoomScopeManager`):** Because multiple rooms share a single scene, clients default to evaluating animations and physics for all players on the map. The `GameManager.IsInLocalScope(Guid)` method disables `ModelRenderer`, `Collider`, and `PlayerController` for entities outside the local player's current room.
* **State Synchronization:** Due to `[Sync]` attribute edge cases on static scene objects in s&box, room states are synchronized via dual pathways: synced fields and redundant RPC broadcasts (`[Rpc.Broadcast] BroadcastStateChange`).
* **RPC Validation:** Inventory or cosmetic mutation requests verify ownership (`Rpc.Caller == Network.Owner`), while round state transitions and reward payouts are restricted to the host (`Rpc.Caller.IsHost`).
* **Hybrid Data Persistence (`StatsPersistence`):**
  1. *Local:* Instant writes to `stats.json` to eliminate UI latency.
  2. *Cloud:* Saves to a KV database via API endpoints (`load-player-data` / `save-player-data`). API requests are throttled to `12±3s` to prevent rate-limit errors.
  3. *Steam:* Global leaderboards via `Sandbox.Services.Stats`.

---

## Economy & Inventory

* **Cosmetics & Cases:** Supports knives, hats, accessories, emotes, and weapon skins. Weapon models with varying pivot points use a `CosmeticOverrides` offset table (including separate first-person view model offsets via `FirstPersonHeldItemOffset`).
* **Trading (`TradeManager`):** Enables secure item trades between players in the lobby. Default items cannot be traded. Any modification to an active offer automatically resets both players' ready statuses.
* **Currency & Anti-Abuse:** Coins spawn during rounds, while crystals are rewarded for wins and active gameplay. Rounds lasting under 60 seconds or containing fewer than 3 players grant no crystals. Daily earnings are capped at 1,500 crystals.

---

## UI & Audio

* **Razor UI:** Comprises 35 components. To prevent unnecessary re-renders, UI panels compare state changes against static singletons (`MenuState`, `TradeUIState`) within `BuildHash()`.
* **Chat:** Partitioned into isolated channels (Global Lobby, Living Room Players, Dead/Spectators).
* **Voice Chat:** Push-to-talk voice system with distance attenuation (up to 600 units). Deceased players can only speak to and hear other spectators.

---

## Project Structure

```text
Code/
├── GameManager.cs       # Scope caching, global player management
├── GameRoom.cs          # Round state machine, role & reward distribution
├── PlayerStats.cs       # Player logic, inventory, health, RPCs
├── Admin/               # SteamID-based admin access verification
├── Cosmetics/           # Cosmetic catalog, case opening logic
├── Inventory/           # Hotbar slot handling and item visibility
├── Lobby/               # Room portals, shooting range, matchmaking
├── Player/              # Bone attachment, scope manager
├── Stats/               # Local and cloud progress persistence
├── Trade/               # Player trading logic
├── UI/                  # Razor components and localization
├── Voice/               # Push-to-talk voice chat and volume settings
└── Weapons/             # Base weapon classes

Editor/                  # s&box editor extensions