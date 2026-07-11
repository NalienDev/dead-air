# Dead Air

A co-op horror extraction game. Players descend into a procedurally generated dungeon,
grab loot to meet a bandwidth quota, and get out before the sound-hunting Conductor or the
voice-mimicking Echo catch them. Networking is handled by PurrNet and proximity voice chat
by Dissonance.

## Requirements

- **Unity 6000.3.10f1** (open the project with this exact version via Unity Hub).
- A microphone, for voice chat.

## Running from the Editor

1. Open the project in Unity Hub.
2. Open a scene from `Assets/Scenes/`:
   - `SteamLobby` — full flow with the in-world lobby PC (create/join a Steam lobby).
   - `BootstrapScene` — skips the menu and connects directly.
3. Press **Play**.

Pressing Play directly in `BootstrapScene` defaults to **Host**, so it's the quickest way to
test on one machine.

## Playing with others

Connections use room codes over PurrTransport:

- **Host** starts a session and gets a 6-character room code.
- **Join** enters that code to connect.

To test multiplayer locally, run one Editor instance as Host and build a standalone player
(or use a second Editor/ParrelSync clone) as the client.

## Basic controls

| Key | Action |
| --- | --- |
| WASD / Mouse | Move / look |
| Shift | Sprint (louder footsteps) |
| E | Interact / pick up |
| Q | Drop held item |
| G | Throw held item |
| Mouse wheel | Switch inventory slot |
| F | Flashlight |
| Esc | Pause menu |

Voice chat is proximity-based while alive; dead players spectate and talk on a separate
channel.

## Debug keys

| Key | Action |
| --- | --- |
| F1 | Toggle the runtime log console |
| F3 | Open the upgrade machine (only with debug upgrades enabled) |
| X | Grant oxygen |

## Project layout

- `Assets/Scripts/` — all gameplay code (enemies, dungeon generation, rover/quota flow,
  upgrades, voice).
- `Assets/Scenes/` — playable scenes and test scenes.
- `Assets/Plugins/Dissonance/`, `Assets/Packages/` — third-party voice and starter assets.
