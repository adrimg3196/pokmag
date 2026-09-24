using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Entities;

namespace HoloTable.Tracking
{
    /// <summary>Everything a game module needs to decide what a newly seen card does.</summary>
    public sealed class SpawnContext
    {
        public SpawnContext(EntityDefinition definition, TrackedTarget target, PlayerSide side, HoloSpawnDirector director)
        {
            Definition = definition;
            Target = target;
            Side = side;
            Director = director;
        }

        public EntityDefinition Definition { get; }
        public TrackedTarget Target { get; }
        public PlayerSide Side { get; }
        public HoloSpawnDirector Director { get; }
    }

    /// <summary>
    /// Inbound port for per-game rules (Pokémon, MTG, Warhammer, or your own).
    /// The spawn director asks modules first, so a module can turn a card into an
    /// evolution, an energy attachment or a spell instead of a plain creature.
    /// </summary>
    public interface IGameRuleModule
    {
        GameSystem System { get; }

        /// <summary>
        /// Return true when the module fully handled the card (no default creature spawn).
        /// Only called for definitions of this module's <see cref="System"/>.
        /// </summary>
        bool TryInterceptSpawn(SpawnContext context);

        /// <summary>A creature of this system was spawned and is starting its entrance.</summary>
        void OnEntitySpawned(LivingEntityController entity, SpawnContext context);

        /// <summary>
        /// The card left the camera for longer than the grace period.
        /// Return true to claim the entity (the director will not despawn it).
        /// <paramref name="entity"/> is null for cards that never spawned a creature.
        /// </summary>
        bool OnTargetLost(TrackedTarget target, LivingEntityController entity);
    }
}
