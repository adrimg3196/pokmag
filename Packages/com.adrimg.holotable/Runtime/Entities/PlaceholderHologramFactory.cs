using System.Collections.Generic;
using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Domain.Mtg;
using UnityEngine;

namespace HoloTable.Entities
{
    /// <summary>
    /// Builds a recognisable stand-in hologram from primitives when a card has no prefab yet,
    /// so the whole game loop (spawn, look-at, attack, evolve, fly, shoot, die) works on day one
    /// with zero art. Creatures get body, head, eyes, legs, tail and wings when they fly;
    /// Warhammer units get a base, a torso, a helmet and a gun with a "Muzzle".
    /// Dimensions are ~1 m tall: <see cref="ScaleRules"/> resizes them to the table.
    /// </summary>
    public static class PlaceholderHologramFactory
    {
        public static LivingEntityController Create(EntityDefinition definition, Vector3 position)
        {
            var root = new GameObject($"{definition.DisplayName} (placeholder)");
            root.transform.position = position;
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var model = new GameObject("Model").transform;
            model.SetParent(visual, false);

            PlaceholderHologram owner = root.AddComponent<PlaceholderHologram>();
            Color tint = definition.HologramTint;
            Material body = owner.Own(CreateMaterial(tint));
            Material accent = owner.Own(CreateMaterial(Color.Lerp(tint, Color.white, 0.45f)));
            Material dark = owner.Own(CreateMaterial(new Color(0.05f, 0.05f, 0.08f)));

            Transform head;
            Transform muzzle;
            if (definition.System == GameSystem.Warhammer)
            {
                BuildTrooper(model, body, accent, dark, out head, out muzzle);
            }
            else
            {
                BuildCreature(model, body, accent, dark, Flies(definition), out head, out muzzle);
            }

            // Added last: Awake finds the finished "Visual" hierarchy.
            LivingEntityController entity = root.AddComponent<LivingEntityController>();
            entity.ConfigureRig(head, muzzle);
            return entity;
        }

        private static bool Flies(EntityDefinition definition) =>
            (definition is MtgCardDefinition mtg && mtg.IsCreature && (mtg.Keywords & MtgKeyword.Flying) != 0)
            || definition.Element == ElementType.Dragon
            || definition.SizeClass >= SizeClass.Huge;

        private static void BuildCreature(Transform model, Material body, Material accent, Material dark, bool wings, out Transform head, out Transform muzzle)
        {
            Part(PrimitiveType.Sphere, model, "Body", new Vector3(0f, 0.45f, 0f), new Vector3(0.75f, 0.62f, 0.95f), body);
            Part(PrimitiveType.Sphere, model, "Belly", new Vector3(0f, 0.4f, 0.12f), new Vector3(0.55f, 0.45f, 0.7f), accent);

            head = Part(PrimitiveType.Sphere, model, "Head", new Vector3(0f, 0.92f, 0.32f), Vector3.one * 0.5f, body);
            Part(PrimitiveType.Sphere, head, "EyeL", new Vector3(-0.2f, 0.12f, 0.38f), Vector3.one * 0.18f, dark);
            Part(PrimitiveType.Sphere, head, "EyeR", new Vector3(0.2f, 0.12f, 0.38f), Vector3.one * 0.18f, dark);
            Part(PrimitiveType.Sphere, head, "Snout", new Vector3(0f, -0.08f, 0.45f), new Vector3(0.35f, 0.25f, 0.3f), accent);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(head, false);
            muzzle.localPosition = new Vector3(0f, -0.05f, 0.6f);

            foreach (float x in new[] { -0.25f, 0.25f })
            {
                foreach (float z in new[] { -0.25f, 0.25f })
                {
                    Part(PrimitiveType.Capsule, model, "Leg", new Vector3(x, 0.12f, z), new Vector3(0.18f, 0.14f, 0.18f), body);
                }
            }

            Transform tail = Part(PrimitiveType.Capsule, model, "Tail", new Vector3(0f, 0.45f, -0.55f), new Vector3(0.14f, 0.3f, 0.14f), body);
            tail.localRotation = Quaternion.Euler(-60f, 0f, 0f);

            if (!wings) return;
            foreach (float side in new[] { -1f, 1f })
            {
                Transform wing = Part(PrimitiveType.Cube, model, side < 0f ? "WingL" : "WingR",
                    new Vector3(0.55f * side, 0.75f, -0.05f), new Vector3(0.75f, 0.04f, 0.5f), accent);
                wing.localRotation = Quaternion.Euler(0f, 0f, 25f * side);
            }
        }

        private static void BuildTrooper(Transform model, Material body, Material accent, Material dark, out Transform head, out Transform muzzle)
        {
            Part(PrimitiveType.Cylinder, model, "Base", new Vector3(0f, 0.02f, 0f), new Vector3(0.9f, 0.02f, 0.9f), dark);
            Part(PrimitiveType.Capsule, model, "Legs", new Vector3(0f, 0.3f, 0f), new Vector3(0.35f, 0.25f, 0.3f), body);
            Part(PrimitiveType.Cube, model, "Torso", new Vector3(0f, 0.68f, 0f), new Vector3(0.5f, 0.42f, 0.32f), body);
            Part(PrimitiveType.Sphere, model, "PauldronL", new Vector3(-0.32f, 0.85f, 0f), Vector3.one * 0.26f, accent);
            Part(PrimitiveType.Sphere, model, "PauldronR", new Vector3(0.32f, 0.85f, 0f), Vector3.one * 0.26f, accent);

            head = Part(PrimitiveType.Sphere, model, "Head", new Vector3(0f, 1.02f, 0.02f), Vector3.one * 0.26f, accent);
            Part(PrimitiveType.Cube, head, "Visor", new Vector3(0f, 0.05f, 0.4f), new Vector3(0.7f, 0.2f, 0.2f), dark);

            Transform gun = Part(PrimitiveType.Cube, model, "Gun", new Vector3(0.22f, 0.7f, 0.3f), new Vector3(0.1f, 0.12f, 0.55f), dark);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.55f);
        }

        private static Transform Part(PrimitiveType type, Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // no physics: LoS, dice and taps ignore holograms
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        private static Shader _primitiveShader;

        private static Material CreateMaterial(Color color)
        {
            if (_primitiveShader == null)
            {
                // Same shader the active render pipeline uses for primitives (Standard, URP Lit…).
                GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _primitiveShader = probe.GetComponent<MeshRenderer>().sharedMaterial.shader;
                Object.DestroyImmediate(probe);
            }

            return new Material(_primitiveShader) { color = color, name = "HoloPlaceholder" };
        }
    }

    /// <summary>Owns the runtime materials of a placeholder hologram and frees them with it.</summary>
    [DisallowMultipleComponent]
    public sealed class PlaceholderHologram : MonoBehaviour
    {
        private readonly List<Material> _materials = new List<Material>();

        internal Material Own(Material material)
        {
            _materials.Add(material);
            return material;
        }

        private void OnDestroy()
        {
            foreach (Material m in _materials)
            {
                if (m != null) Destroy(m);
            }
        }
    }
}
