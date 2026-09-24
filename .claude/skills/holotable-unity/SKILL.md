---
name: holotable-unity
description: How to extend HoloTable XR (Unity AR tabletop for Pokémon TCG, MTG and Warhammer 40K) safely — add a card, a game rule, a whole new game module or a tracking SDK adapter, and verify it without opening Unity. Use for any change under Packages/com.adrimg.holotable or its Samples~.
metadata:
  origin: pokmag (built on ECC tdd-workflow, hexagonal-architecture and verification-loop)
---

# HoloTable Unity

## Mental model (ports & adapters)

```
Tracking SDK ─► Adapter ─► HoloSpawnDirector ─► IGameRuleModule (Pokémon/MTG/WH) ─► AttackRequest ─► ARCombatManager
                                     │                         │
                                     └──► LivingEntityController ◄──────────────┘
Rules: HoloTable.Domain (pure C#, no UnityEngine, 100% unit-tested)
```

## Recipes

### Add a card / datasheet (no code)
1. `Create → HoloTable → Pokemon/Card | MTG/Card | Warhammer/Datasheet` under `Assets/Games/<Game>/Data/...`.
2. `Reference Image Names` = exact name in the XRReferenceImageLibrary / Vuforia database.
3. Assign the `HOLO_*` prefab (root: `LivingEntityController`; child: model with Animator).
4. Add it to the game's `CardCatalog`.

### Add or change a rule (TDD — ECC `tdd-workflow`)
1. RED: write the failing xUnit test in `Tests/HoloTable.Domain.Tests` first (cite the official rule in the test name).
2. GREEN: implement in `Packages/com.adrimg.holotable/Domain/<Game>/` — immutable records, `with`, no `UnityEngine`.
3. REFACTOR, then call it from the game module in `Runtime/Games/<Game>/`.

### Add a new game (e.g. Yu-Gi-Oh, Lorcana)
1. `Domain/<Game>/` rules + tests.
2. `Data/<Game>CardDefinition : EntityDefinition` (override `System`, stats, `BuildStatLine`).
3. Add a `GameSystem` enum value **at the end** (values are serialized as ints).
4. `Runtime/Games/<Game>/GameLogic<Game> : MonoBehaviour, IGameRuleModule` — register in `OnEnable`,
   unregister in `OnDisable`; intercept non-creature cards in `TryInterceptSpawn`; send fights as `AttackRequest`.

### Add a tracking SDK
New folder `Scripts/Adapters/<Sdk>/` with its own asmdef (`versionDefines` + `defineConstraints`) that only calls
`HoloSpawnDirector.Instance.ReportFound / ReportLimited / ReportLost`. Add the SDK's minimal API to
`Tools/UnityCompileCheck/Stubs/<AssemblyName>/` with its real signatures.

## Verification loop (run before every commit — the PostToolUse hook runs it too)

```bash
dotnet test  Tests/HoloTable.Domain.Tests      # rules
python3 Tools/UnityCompileCheck/check.py           # every Unity script, warnings as errors
```

Then review with the project agents in `.claude/agents` (`csharp-reviewer`, `silent-failure-hunter`,
`performance-optimizer`, `pr-test-analyzer`). Final gate: open the project in Unity and play the Sandbox scene.

## Unity pitfalls this codebase already guards against
- Holograms are never children of image targets (the director owns lifetime and smoothing).
- Never `?.` / `??` on `UnityEngine.Object`.
- Static registries reset in `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` (Enter Play Mode Options).
- Every combat coroutine has a timeout; `LivingEntityController.StopAction` always fires the pending impact.
