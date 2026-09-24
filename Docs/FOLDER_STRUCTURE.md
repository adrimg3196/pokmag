# Estructura de carpetas en Unity

Objetivo: que el **código** sea único y compartido, y que el **contenido** de cada juego (cartas, modelos,
animaciones, VFX, audio) viva aislado, se pueda cargar bajo demanda y se pueda licenciar/eliminar por separado
(las IP de Pokémon, Wizards y Games Workshop tienen condiciones distintas).

```
Assets/
├── _HoloTable/                         ← motor compartido (el "_" lo mantiene arriba)
│   ├── Scripts/
│   │   ├── Domain/                     ← reglas puras C# (sin UnityEngine) · HoloTable.Domain.asmdef
│   │   │   ├── Common/  Combat/  Pokemon/  Mtg/  Warhammer/
│   │   ├── Runtime/                    ← MonoBehaviours · HoloTable.Runtime.asmdef
│   │   │   ├── Core/        TableSpace, EntityRegistry, HoloMath
│   │   │   ├── Data/        EntityDefinition, CardCatalog, *Definition / Datasheet
│   │   │   ├── Entities/    LivingEntityController, AnimationEventRelay
│   │   │   ├── Tracking/    HoloSpawnDirector, IGameRuleModule, TargetEventBridge
│   │   │   ├── Combat/      ARCombatManager, HoloProjectile, AttackRequest
│   │   │   ├── UI/          EntityHUD, DamagePopupService, FloatingDamageNumber
│   │   │   ├── VFX/         HologramMaterialDriver, EnvironmentDimmer, ContactShadow, LightningBoltEffect
│   │   │   └── Games/
│   │   │       ├── Pokemon/     GameLogicPokemon
│   │   │       ├── MTG/         GameLogicMTG, MtgSpellCaster
│   │   │       └── Warhammer/   GameLogicWarhammer, MovementRangeVisualizer, LineOfSightLaser, DiceTray, ARDie
│   │   └── Adapters/                   ← un asmdef por SDK, solo compila si el paquete está instalado
│   │       ├── ARFoundation/  Vuforia/  XRHands/  InputSystem/
│   ├── Shaders/                        SG_Hologram.shadergraph, SG_ContactShadow…
│   ├── VFX/Shared/                     Portal de invocación, disolución, impactos, proyectiles genéricos
│   ├── Prefabs/Core/                   HoloTableRig, EntityHUD, DamageNumber, DiceTray, D6
│   ├── Animation/                      AC_HoloCreature_Base.controller (controller base compartido)
│   ├── Audio/Shared/
│   ├── Settings/                       URP, XR Plug-in, Vuforia/ARF configs, Input Actions
│   └── Scenes/                         Bootstrap, HoloTable_Main, Sandbox_Pokemon, Sandbox_MTG, Sandbox_WH
│
├── Games/
│   ├── Pokemon/
│   │   ├── Data/
│   │   │   ├── Catalog_Pokemon.asset
│   │   │   └── Cards/<Set>/                PKM_<Set>_<Nº>_<Nombre>.asset   (p. ej. PKM_SV03_125_Charizard)
│   │   ├── ReferenceImages/<Set>/          escaneos + RIL_Pokemon.asset (AR Foundation) / DB Vuforia
│   │   ├── Models/<Especie>/               FBX, texturas, materiales
│   │   ├── Animations/<Especie>/           clips + AOC_<Especie>.overrideController
│   │   ├── Prefabs/Holograms/              HOLO_PKM_<Especie>.prefab
│   │   ├── VFX/Elements/<Tipo>/            Aura, AttachBurst, Projectile (Fire, Water, Lightning…)
│   │   ├── VFX/Evolution/                  Cocoon, Burst
│   │   └── Audio/Cries/
│   │
│   ├── MTG/
│   │   ├── Data/
│   │   │   ├── Catalog_MTG.asset
│   │   │   └── Cards/<SetCode>/            MTG_<SetCode>_<Nº>_<Nombre>.asset
│   │   ├── ReferenceImages/<SetCode>/
│   │   ├── Models/Creatures/<Tipo>/  Models/Tokens/
│   │   ├── Animations/<Criatura>/
│   │   ├── Prefabs/Holograms/Creatures/  Prefabs/Holograms/Tokens/
│   │   ├── VFX/Spells/<Arquetipo>/         Lightning, Darkness, Fire, Heal, Buff
│   │   ├── VFX/Abilities/
│   │   └── Audio/
│   │
│   └── Warhammer/
│       ├── Data/
│       │   ├── Catalog_Warhammer.asset
│       │   └── Datasheets/<Facción>/       WH_<Facción>_<Unidad>.asset
│       ├── ModelTargets/<Facción>/         Vuforia Model Targets de las miniaturas / marcadores de peana
│       ├── Models/<Facción>/<Unidad>/
│       ├── Animations/<Facción>/
│       ├── Prefabs/Holograms/<Facción>/
│       ├── Prefabs/Terrain/                Ruinas / bosques virtuales (capa VirtualTerrain → LoS)
│       ├── VFX/Weapons/                    Bolter, Plasma, Flamer, Melee
│       ├── Dice/                           D6 físico, materiales por facción
│       └── Audio/
│
└── ThirdParty/                         ← SDKs y packs de la Asset Store, nunca mezclados con lo propio
```

## Convenciones

| Prefijo | Tipo |
|---|---|
| `PKM_` / `MTG_` / `WH_` | ScriptableObject de datos (carta, datasheet) |
| `HOLO_` | Prefab de holograma con `LivingEntityController` |
| `AC_` / `AOC_` | Animator Controller / Animator Override Controller |
| `VFX_` / `SFX_` | Sistemas de partículas / audio |
| `RIL_` | Reference Image Library de AR Foundation |
| `SG_` | Shader Graph |

## Reglas que hacen que escale

1. **Datos ≠ código.** Añadir una carta nueva = crear un `.asset` y una imagen de referencia. Cero código.
2. **Un controller base + Override Controllers.** Todas las criaturas comparten parámetros (`Spawn`, `Attack`,
   `TakeDamage`, `Die`, `IsFlying`), así `LivingEntityController` funciona con cualquier modelo.
3. **Catálogo por juego + catálogo maestro.** `Catalog_Master` usa `includes` para agregar los tres;
   un build “solo Pokémon” asigna directamente `Catalog_Pokemon`.
4. **Addressables por juego y por set** (`Pokemon_SV03`, `MTG_MKM`, `WH_SpaceMarines`): los modelos 3D pesan mucho;
   descárgalos bajo demanda en móvil y Quest.
5. **Capas físicas**: `Holograms`, `VirtualTerrain`, `ARMesh` (malla de la habitación), `Dice`.
   `LineOfSightLaser.obstacleLayers = VirtualTerrain | ARMesh` → la cobertura puede ser real o virtual.
6. **Nada de SDK en la lógica.** Los juegos solo conocen `HoloSpawnDirector`; cambiar Vuforia por AR Foundation o por un
   detector propio en Quest es cambiar un adaptador.
