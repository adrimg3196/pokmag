# Estructura de carpetas en Unity

Objetivo: que el **código** sea único y compartido (el paquete `com.adrimg.holotable`), y que el **contenido** de cada juego (cartas, modelos,
animaciones, VFX, audio) viva aislado, se pueda cargar bajo demanda y se pueda licenciar/eliminar por separado
(las IP de Pokémon, Wizards y Games Workshop tienen condiciones distintas).

```
Packages/com.adrimg.holotable/          ← el paquete que instala la comunidad (código compartido)
├── Domain/        reglas puras C# (sin UnityEngine) · HoloTable.Domain.asmdef
├── Runtime/       Core · Data · Entities · Tracking · Combat · UI · VFX · Games/{Pokemon,MTG,Warhammer}
├── Adapters/      ARFoundation · Vuforia · XRHands · InputSystem (un asmdef por SDK)
├── Editor/        asistente HoloTable ▸ Create Demo Scene
└── Samples~/DemoCards/                 datos de ejemplo importables desde Package Manager

Assets/                                 ← TU proyecto: contenido de cada juego, aislado
├── HoloTableDemo/                      escena generada por el asistente
├── Samples/HoloTable XR/<versión>/Demo Cards/   cartas demo importadas (Package Manager ▸ Samples)
├── Games/
│   ├── Pokemon/
│   │   ├── Data/Catalog_Pokemon.asset
│   │   ├── Data/Cards/<Set>/                PKM_<Set>_<Nº>_<Nombre>.asset   (p. ej. PKM_SV03_125_Charizard)
│   │   ├── ReferenceImages/<Set>/          escaneos + RIL_Pokemon.asset (AR Foundation) / DB Vuforia
│   │   ├── Models/<Especie>/               FBX, texturas, materiales
│   │   ├── Animations/<Especie>/           clips + AOC_<Especie>.overrideController
│   │   ├── Prefabs/Holograms/              HOLO_PKM_<Especie>.prefab
│   │   ├── VFX/Elements/<Tipo>/            Aura, AttachBurst, Projectile (Fire, Water, Lightning…)
│   │   ├── VFX/Evolution/                  Cocoon, Burst
│   │   └── Audio/Cries/
│   ├── MTG/
│   │   ├── Data/Catalog_MTG.asset · Data/Cards/<SetCode>/MTG_<SetCode>_<Nº>_<Nombre>.asset
│   │   ├── ReferenceImages/<SetCode>/ · Models/Creatures/ · Models/Tokens/ · Animations/
│   │   ├── Prefabs/Holograms/{Creatures,Tokens}/ · VFX/Spells/<Arquetipo>/ · VFX/Abilities/ · Audio/
│   └── Warhammer/
│       ├── Data/Catalog_Warhammer.asset · Data/Datasheets/<Facción>/WH_<Facción>_<Unidad>.asset
│       ├── ModelTargets/<Facción>/ · Models/<Facción>/<Unidad>/ · Animations/<Facción>/
│       ├── Prefabs/Holograms/<Facción>/ · Prefabs/Terrain/ (capa VirtualTerrain → LoS)
│       └── VFX/Weapons/ · Dice/ · Audio/
├── Shared/                             shaders (SG_Hologram), VFX comunes, AC_HoloCreature_Base.controller
└── ThirdParty/                         SDKs y packs de la Asset Store, nunca mezclados con lo propio
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
