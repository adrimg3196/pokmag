using System;
using System.IO;
using System.Linq;
using HoloTable.Combat;
using HoloTable.Core;
using HoloTable.Data;
using HoloTable.Games.MTG;
using HoloTable.Games.Pokemon;
using HoloTable.Games.Warhammer;
using HoloTable.Tracking;
using HoloTable.UI;
using HoloTable.VFX;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HoloTable.Editor
{
    /// <summary>
    /// One-click onboarding: imports the demo cards and TMP essentials, then builds a fully wired
    /// scene (director, combat, popups, the three game modules, AR dice and the desktop simulator)
    /// that plays in the Editor with no AR hardware.
    /// </summary>
    public static class HoloTableSetupWizard
    {
        private const string PackageName = "com.adrimg.holotable";
        private const string SampleName = "Demo Cards";
        private const string DemoFolder = "Assets/HoloTableDemo";
        private const string ScenePath = DemoFolder + "/HoloTableDemo.unity";
        private const string SimulatorType = "HoloTable.Adapters.DesktopTableSimulator, HoloTable.Adapters.InputSystem";
        private const string TmpEssentialsMenu = "Window/TextMeshPro/Import TMP Essential Resources";

        [MenuItem("HoloTable/Create Demo Scene", priority = 0)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (!HasTmpEssentials())
            {
                EditorApplication.ExecuteMenuItem(TmpEssentialsMenu);
                EditorUtility.DisplayDialog("HoloTable",
                    "TextMeshPro Essential Resources are being imported (needed for HUD and labels).\n\n" +
                    "When the import finishes, run  HoloTable ▸ Create Demo Scene  again.", "OK");
                return;
            }

            CardCatalog catalog = FindOrImportDemoCatalog();
            if (catalog == null)
            {
                EditorUtility.DisplayDialog("HoloTable",
                    "Could not find or import the 'Demo Cards' sample.\n\nWindow ▸ Package Manager ▸ HoloTable XR ▸ Samples ▸ Import, then try again.", "OK");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildDemo(catalog);

            EnsureFolder(DemoFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[HoloTable] Demo scene created at {ScenePath}. Press Play.");

            EditorUtility.DisplayDialog("HoloTable",
                "Demo scene ready. Press Play.\n\n" +
                "• 1-9 pick a card, left-click the table to place it\n" +
                "• Right-click taps (MTG) · drag onto a card to evolve (Pokémon)\n" +
                "• S / F select and shoot (Warhammer) · N next turn\n\n" +
                "Full controls are shown on screen.", "Play!");
        }

        [MenuItem("HoloTable/Validate Project Setup", priority = 20)]
        public static void ValidateSetup()
        {
            int problems = 0;

            if (!HasTmpEssentials())
            {
                problems++;
                Debug.LogWarning($"[HoloTable] TMP Essential Resources missing: {TmpEssentialsMenu}.");
            }

            if (Type.GetType(SimulatorType) == null)
            {
                problems++;
                Debug.LogWarning("[HoloTable] Desktop simulator not compiled: install the Input System package and enable it in Player ▸ Active Input Handling.");
            }

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(CardCatalog)))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<CardCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog == null) continue;

                foreach (EntityDefinition def in catalog.AllDefinitions())
                {
                    if (def.ReferenceImageNames.Count == 0)
                    {
                        problems++;
                        Debug.LogWarning($"[HoloTable] '{def.name}' has no Reference Image Names: tracking can never find it.", def);
                    }
                }
            }

            string summary = problems == 0 ? "No problems found." : $"{problems} problem(s): see the Console.";
            EditorUtility.DisplayDialog("HoloTable ▸ Validate", summary, "OK");
        }

        private static void BuildDemo(CardCatalog catalog)
        {
            var root = new GameObject("HoloTable");

            TableSpace table = root.AddComponent<TableSpace>();
            SetField(table, "autoOrientFromCamera", false);

            HoloSpawnDirector director = root.AddComponent<HoloSpawnDirector>();
            SetField(director, "catalog", catalog);

            root.AddComponent<ARCombatManager>();
            root.AddComponent<DamagePopupService>();
            root.AddComponent<EnvironmentDimmer>();
            root.AddComponent<GameLogicPokemon>();
            root.AddComponent<GameLogicMTG>();

            var trayGo = new GameObject("DiceTray");
            trayGo.transform.SetParent(root.transform, false);
            DiceTray tray = trayGo.AddComponent<DiceTray>();

            GameLogicWarhammer warhammer = root.AddComponent<GameLogicWarhammer>();
            SetField(warhammer, "diceTray", tray);
            SetField(warhammer, "gazeSelection", false); // desktop: select with the mouse (S key)
            SetField(warhammer, "autoThrowDice", true);

            Type simulator = Type.GetType(SimulatorType);
            if (simulator != null)
            {
                new GameObject("DesktopSimulator").AddComponent(simulator);
            }
            else
            {
                Debug.LogWarning("[HoloTable] Input System not available: the demo scene has no desktop simulator. Add cards through a tracking adapter or TargetEventBridge.");
            }

            Selection.activeGameObject = root;
        }

        private static CardCatalog FindOrImportDemoCatalog()
        {
            CardCatalog catalog = FindCatalog("Catalog_Master");
            if (catalog != null) return catalog;

            Sample sample = Sample.FindByPackage(PackageName, null).FirstOrDefault(s => s.displayName == SampleName);
            if (string.IsNullOrEmpty(sample.displayName)) return null;

            sample.Import(Sample.ImportOptions.OverridePreviousImports);
            AssetDatabase.Refresh();
            return FindCatalog("Catalog_Master");
        }

        private static CardCatalog FindCatalog(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:{nameof(CardCatalog)}"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<CardCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog != null && catalog.name == name) return catalog;
            }

            return null;
        }

        private static bool HasTmpEssentials() => AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;

        private static void SetField(UnityEngine.Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[HoloTable] Field '{field}' not found on {target.GetType().Name}.", target);
                return;
            }

            switch (value)
            {
                case bool b: property.boolValue = b; break;
                case UnityEngine.Object o: property.objectReferenceValue = o; break;
                default: throw new ArgumentException($"Unsupported value type {value?.GetType().Name}.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
