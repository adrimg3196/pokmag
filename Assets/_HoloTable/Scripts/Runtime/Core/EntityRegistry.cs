using System.Collections.Generic;
using HoloTable.Domain;
using HoloTable.Domain.Combat;
using HoloTable.Entities;
using UnityEngine;

namespace HoloTable.Core
{
    /// <summary>
    /// Live list of holograms on the table. Cheap spatial queries (nearest rival, by id)
    /// for look-at, combat and game modules. Entities register themselves.
    /// </summary>
    public static class EntityRegistry
    {
        private static readonly List<LivingEntityController> Entities = new List<LivingEntityController>();
        private static readonly Dictionary<int, LivingEntityController> ById = new Dictionary<int, LivingEntityController>();
        private static readonly List<LookCandidate> CandidateBuffer = new List<LookCandidate>();

        public static IReadOnlyList<LivingEntityController> All => Entities;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Entities.Clear();
            ById.Clear();
        }

        internal static void Register(LivingEntityController entity)
        {
            if (ById.ContainsKey(entity.EntityId)) return;
            Entities.Add(entity);
            ById.Add(entity.EntityId, entity);
        }

        internal static void Unregister(LivingEntityController entity)
        {
            if (!ById.Remove(entity.EntityId)) return;
            Entities.Remove(entity);
        }

        public static LivingEntityController Find(int entityId) =>
            ById.TryGetValue(entityId, out LivingEntityController e) ? e : null;

        public static LivingEntityController FindNearestRival(LivingEntityController self, float radius)
        {
            CandidateBuffer.Clear();
            foreach (LivingEntityController e in Entities)
            {
                CandidateBuffer.Add(new LookCandidate(e.EntityId, e.transform.position.ToNumerics(), e.Side, e.IsAlive));
            }

            int id = LookTargetSelector.SelectNearestRival(
                self.EntityId, self.transform.position.ToNumerics(), self.Side, CandidateBuffer, radius);

            return id == LookTargetSelector.NoTarget ? null : Find(id);
        }

        /// <summary>Nearest living entity on <paramref name="side"/> to a point, optionally filtered.</summary>
        public static LivingEntityController FindNearest(Vector3 point, PlayerSide side, float radius, System.Predicate<LivingEntityController> filter = null)
        {
            LivingEntityController best = null;
            float bestSqr = radius * radius;
            foreach (LivingEntityController e in Entities)
            {
                if (!e.IsAlive || e.Side != side) continue;
                if (filter != null && !filter(e)) continue;

                float sqr = (e.transform.position - point).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = e;
                }
            }

            return best;
        }

        public static void CollectBySide(PlayerSide side, List<LivingEntityController> results)
        {
            results.Clear();
            foreach (LivingEntityController e in Entities)
            {
                if (e.IsAlive && e.Side == side) results.Add(e);
            }
        }
    }
}
