# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-09-24
### Added
- Installable Unity package (`com.adrimg.holotable`) via git URL, with a *Demo Cards* sample.
- **HoloTable ▸ Create Demo Scene** and **HoloTable ▸ Validate Project Setup** editor menus.
- `DesktopTableSimulator`: play all three games with mouse and keyboard, no AR hardware.
- Procedural placeholder holograms for cards without a prefab, and a code-built default HUD.
- Community files: MIT license, trademark notice, contributing guide, code of conduct, security policy, issue/PR templates.
### Changed
- Input System is now an optional dependency (only the desktop simulator uses it); the wizard offers to install it.
### Fixed
- Missing `.meta` for `VfxPool.cs` (required for immutable git installs); CI now checks metas, orphans and duplicate GUIDs.
- Missing asmdef references (`UnityEngine.UI` in Runtime, `HoloTable.Domain` in the Input System adapter) that would
  break compilation in Unity; CI now compiles each assembly separately, exactly as Unity does.

## [0.1.0] - 2026-09-24
### Added
- Rules domain for Pokémon TCG, MTG and Warhammer 40K (132 xUnit tests).
- `LivingEntityController`, `HoloSpawnDirector`, `ARCombatManager`, game modules, AR dice, VFX and HUD.
- AR Foundation, Vuforia, XR Hands and Input System adapters.
