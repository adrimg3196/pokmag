# HoloTable XR — guía para agentes

Unity AR tabletop (Pokémon TCG · MTG · Warhammer 40K). Estilo y flujo inspirados en
[ECC — everything-claude-code](https://github.com/affaan-m/ecc): arquitectura hexagonal, TDD en el dominio,
archivos pequeños y cohesionados.

## Capas y reglas de dependencia

- `Assets/_HoloTable/Scripts/Domain` — reglas puras. **Prohibido** `using UnityEngine`. Tipos inmutables (`record`, `with`).
  Vectores con `System.Numerics`. Toda regla nueva va con test en `Tests/HoloTable.Domain.Tests`.
- `Assets/_HoloTable/Scripts/Runtime` — MonoBehaviours. Depende de Domain, nunca de un SDK de tracking.
- `Assets/_HoloTable/Scripts/Adapters/<SDK>` — un asmdef por SDK con `versionDefines` + `defineConstraints`.
- Los juegos se integran implementando `IGameRuleModule` y registrándose en `HoloSpawnDirector`.
  Los módulos calculan reglas; `ARCombatManager` solo coreografía (`AttackRequest`).

## Convenciones C# (Unity)

- Versión mínima: Unity 2021.3.18 (`FindFirstObjectByType`, `UnityEngine.Pool`). Recomendado 2022.3 LTS / Unity 6.
- Enums serializados (`ElementType`, `SizeClass`, `GameSystem`…): valores explícitos, solo añadir al final.
- Efectos de partículas one-shot: `VfxPool.Play`, nunca `Instantiate` + `Destroy` en combate.
- Toda espera en corrutina (dados, impactos, picos del atenuador) lleva timeout y aviso en consola.

- C# 9 (sin `record struct`, sin `file`-scoped namespaces, sin `required`).
- Campos serializados `private` + `[SerializeField]`, exponer propiedades de solo lectura.
- `ScriptableObject` = datos inmutables en runtime; el estado vivo va en la entidad o el módulo.
- Unity 6: usar `#if UNITY_6000_0_OR_NEWER` para `Rigidbody.linearVelocity`.
- No usar `?.`/`??` sobre `UnityEngine.Object` que pueda estar destruido.

## Verificar antes de hacer commit

```bash
dotnet test  Tests/HoloTable.Domain.Tests
dotnet build Tools/UnityCompileCheck
```

Ambos deben terminar sin errores ni warnings (`TreatWarningsAsErrors`). Si usas una API nueva de un SDK
externo, añádela a `Tools/UnityCompileCheck/Stubs` con la firma real.
