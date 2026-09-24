# Contributing to HoloTable XR

Thanks for helping bring cards and miniatures to life! Contributions of all sizes are welcome:
bug reports, new rules, new games, tracking adapters, art-free VFX, docs and translations.

## Ground rules

- Be kind: this project follows the [Code of Conduct](CODE_OF_CONDUCT.md).
- **No copyrighted game assets** (card scans, official art, logos, models). See [TRADEMARKS.md](TRADEMARKS.md).
- Keep pull requests focused: one feature or fix per PR.

## Project layout

```
Packages/com.adrimg.holotable/   ← the Unity package (what users install)
  Domain/     pure C# rules, no UnityEngine (unit-tested with dotnet)
  Runtime/    MonoBehaviours: entities, director, combat, game modules, UI, VFX
  Adapters/   one assembly per SDK (AR Foundation, Vuforia, XR Hands, Input System)
  Editor/     setup wizard (HoloTable ▸ Create Demo Scene)
  Samples~/   demo card data
Tests/        xUnit tests for Domain
Tools/        compile check against UnityEngine reference DLLs, .meta generator
Docs/         setup guides
```

## Development workflow

You do **not** need Unity to work on rules. You do need it to try gameplay.

1. **Rules change?** Write the failing test first in `Tests/HoloTable.Domain.Tests`, cite the official
   rule in the test name, then implement in `Domain/`. Keep types immutable (`record`, `with`).
2. **Unity change?** Open any Unity 2021.3.18+ project, `Package Manager ▸ + ▸ Add package from disk…`
   and pick `Packages/com.adrimg.holotable/package.json`, then `HoloTable ▸ Create Demo Scene`.
3. Before pushing, run:

```bash
dotnet test  Tests/HoloTable.Domain.Tests      # rules
python3 Tools/UnityCompileCheck/check.py      # every asmdef compiled like Unity, warnings are errors
python3 Tools/generate_meta.py                 # create .meta files for new files (required!)
python3 Tools/generate_meta.py --check
```

CI runs the same commands on every pull request.

## Conventions

- C# 9 (Unity), `private` + `[SerializeField]` fields, read-only properties.
- Serialized enums: explicit values, only append.
- Every coroutine wait has a timeout and logs a warning when it trips.
- One-shot particles go through `VfxPool.Play`.
- New SDK API used by an adapter → add its minimal signature to `Tools/UnityCompileCheck/Stubs/<AssemblyName>/`.
- More detail for AI agents and humans alike: [CLAUDE.md](CLAUDE.md) and `.claude/skills/holotable-unity`.

## Adding a new game

See `.claude/skills/holotable-unity/SKILL.md` → *Add a new game*. In short: domain rules + tests,
an `EntityDefinition` subclass, a `GameSystem` value appended at the end, and a `GameLogic<Game>`
implementing `IGameRuleModule`.

## Reporting bugs

Use the bug report template. Include Unity version, render pipeline, device, tracking SDK and the
Console log (HoloTable messages start with `[HoloTable]`).
