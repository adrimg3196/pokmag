# Desktop simulator

Play every HoloTable feature in the Unity Editor (or a desktop build) with **no AR device, no card scans and no
3D models**. `DesktopTableSimulator` puts virtual cards on a virtual table and reports them to `HoloSpawnDirector`
exactly like a tracking SDK: placing = *found*, covering / lifting / removing = *lost*, rotating 90° = *tap*.

Create it with **HoloTable ▸ Create Demo Scene**, or add the component to any scene that has a `HoloSpawnDirector`.
It needs Unity's Input System package (optional dependency: included in Unity 6 templates, and the wizard offers to
install it) and *Player ▸ Active Input Handling* set to *Input System Package* or *Both*.

## Controls

| Input | Action |
|---|---|
| `1`–`9` | Select a card from the current catalog page (shown top-left) |
| `Tab` | Next catalog page |
| Left-click on the table | Place the selected card. The near half is Player One, the far half Player Two |
| Left-drag a card | Move it. Dropping it onto another card covers the lower one (it stops being "seen") |
| Right-click a card | Tap / untap (rotates 90°) |
| `H` over a card | Hide it (hand over the card / miniature lifted). Press again to show it |
| `X` / `Delete` over a card | Remove it from the table |
| `N` | Next turn: MTG untap step clears summoning sickness and damage, Warhammer movement resets |
| Middle-drag / wheel | Orbit / zoom the camera |

### Pokémon
- Place **Charmander**, then drop **Charmeleon** on top → evolution. Drop **Charizard** on Charmeleon → Stage 2.
- Place an **Energy** card next to a Pokémon (within ~12 cm) → it attaches, the aura grows.
- Hover a Pokémon and press `Space` (attack 1) or `Shift+Space` (attack 2) to hit the nearest rival.
  Water vs Charizard shows *¡Es súper eficaz!* (weakness ×2).

### Magic: The Gathering
- Place creatures on both halves. **Shivan Dragon** / **Serra Angel** fly 30 cm above the table.
- Right-click to tap a creature → it attacks. Within 3 s, drag a defending creature next to it to block
  (fliers can only be blocked by Flying/Reach, e.g. **Giant Spider**). Unblocked damage hits the opponent's life.
- Place **Lightning Bolt** → the room darkens and lightning strikes the nearest enemy.
  **Pestilent Haze** hits every enemy creature.

### Warhammer 40K
- Place **Intercessor** (near side) and **Ork Boy** (far side).
- Hover your unit, press `S` → movement ring. Drag the base: the remaining inches update and the ring turns red if
  you move too far. `V` advances (D6), `C` confirms the move.
- Hover the enemy, press `S` → red line-of-sight laser (orange = cover). `Q` switches weapon, `F` shoots:
  hit → wound → save dice are rolled physically in the dice tray, then the wounds are applied.
- `H` on a model simulates lifting it: it stays as a ghost with its wounds and re-attaches when shown again.
