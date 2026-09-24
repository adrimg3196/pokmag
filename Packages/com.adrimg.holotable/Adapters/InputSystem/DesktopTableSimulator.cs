using System.Collections.Generic;
using HoloTable.Core;
using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Entities;
using HoloTable.Games.MTG;
using HoloTable.Games.Pokemon;
using HoloTable.Games.Warhammer;
using HoloTable.Tracking;
using HoloTable.UI;
using HoloTable.VFX;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HoloTable.Adapters
{
    /// <summary>
    /// Play HoloTable in the Editor or on desktop with no AR hardware and no card scans.
    /// Virtual cards on a virtual table are reported to <see cref="HoloSpawnDirector"/> exactly as a
    /// tracking SDK would: placing = found, removing/covering/lifting = lost, rotating 90° = tap.
    /// Controls are listed on screen (OnGUI) and in Docs/DESKTOP_SIMULATOR.md.
    /// </summary>
    public sealed class DesktopTableSimulator : MonoBehaviour
    {
        private const int PageSize = 9;
        private static readonly Vector2 CardSize = new Vector2(0.063f, 0.088f);

        private sealed class SimCard
        {
            public string Id;
            public EntityDefinition Definition;
            public Transform Root;
            public Material Material;
            public bool Tapped;
            public bool Hidden;
            public bool Covered;
            public float Footprint;
        }

        [SerializeField] private CardCatalog catalog;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Vector2 tableSize = new Vector2(1.2f, 0.8f);
        [SerializeField] private bool buildTableVisual = true;
        [SerializeField] private bool showHelp = true;

        [Header("Camera")]
        [SerializeField, Min(0.2f)] private float distance = 1.05f;
        [SerializeField, Range(10f, 89f)] private float pitch = 55f;
        [SerializeField, Min(1f)] private float orbitSpeed = 0.25f;
        [SerializeField, Min(0.01f)] private float zoomSpeed = 0.0015f;

        private readonly List<SimCard> _cards = new List<SimCard>();
        private readonly List<EntityDefinition> _palette = new List<EntityDefinition>();
        private HoloSpawnDirector _director;
        private GameLogicPokemon _pokemon;
        private GameLogicMTG _mtg;
        private GameLogicWarhammer _warhammer;
        private SimCard _hover;
        private SimCard _dragging;
        private Vector3 _dragOffset;
        private int _paletteIndex;
        private int _nextId = 1;
        private float _yaw;
        private PlayerSide _turn = PlayerSide.PlayerOne;
        private bool _warnedNoInput;
        private bool _warnedMissingDependency;
        private GUIStyle _style;

        private void Start()
        {
            _director = HoloSpawnDirector.Instance;
            if (_director == null) Debug.LogError("[HoloTable] DesktopTableSimulator needs a HoloSpawnDirector in the scene.", this);
            if (catalog == null && _director != null) catalog = _director.Catalog;
            if (catalog != null) _palette.AddRange(catalog.AllDefinitions());
            if (_palette.Count == 0) Debug.LogWarning("[HoloTable] DesktopTableSimulator: the catalog has no cards. Import the 'Demo Cards' sample.", this);

            _pokemon = FindFirstObjectByType<GameLogicPokemon>();
            _mtg = FindFirstObjectByType<GameLogicMTG>();
            _warhammer = FindFirstObjectByType<GameLogicWarhammer>();

            if (viewCamera == null) viewCamera = Camera.main;
            if (buildTableVisual) BuildTable();
            TmpFontCheck.WarnIfMissing(this);
        }

        private void OnDestroy()
        {
            foreach (SimCard card in _cards)
            {
                if (card.Material != null) Destroy(card.Material);
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null)
            {
                if (!_warnedNoInput)
                {
                    _warnedNoInput = true;
                    Debug.LogWarning("[HoloTable] No keyboard/mouse from the Input System (Project Settings ▸ Player ▸ Active Input Handling must include 'Input System Package').", this);
                }

                return;
            }

            if (viewCamera == null) viewCamera = Camera.main;
            if (_director == null) _director = HoloSpawnDirector.Instance;
            if (viewCamera == null || _director == null)
            {
                if (!_warnedMissingDependency)
                {
                    _warnedMissingDependency = true;
                    Debug.LogError(viewCamera == null
                        ? "[HoloTable] DesktopTableSimulator has no camera: assign 'View Camera' or tag one as MainCamera."
                        : "[HoloTable] DesktopTableSimulator needs a HoloSpawnDirector in the scene.", this);
                }

                return;
            }

            UpdateCamera(mouse);
            bool onTable = TryGetTablePoint(mouse.position.ReadValue(), out Vector3 point);
            _hover = _dragging ?? (onTable ? CardAt(point) : null);

            HandlePalette(kb);
            HandleMouse(mouse, onTable, point);
            HandleCardKeys(kb);
            HandleGameKeys(kb);
            ReportVisibleCards();
        }

        // ─────────────────────────────── Input ───────────────────────────────

        private void HandlePalette(Keyboard kb)
        {
            if (_palette.Count == 0) return;

            if (kb[Key.Tab].wasPressedThisFrame)
            {
                int pages = (_palette.Count + PageSize - 1) / PageSize;
                int page = (_paletteIndex / PageSize + 1) % pages;
                _paletteIndex = Mathf.Min(page * PageSize, _palette.Count - 1);
            }

            for (int i = 0; i < PageSize; i++)
            {
                if (!kb[Key.Digit1 + i].wasPressedThisFrame) continue;
                int index = _paletteIndex / PageSize * PageSize + i;
                if (index < _palette.Count) _paletteIndex = index;
            }
        }

        private void HandleMouse(Mouse mouse, bool onTable, Vector3 point)
        {
            if (mouse.leftButton.wasPressedThisFrame && onTable)
            {
                if (_hover != null)
                {
                    _dragging = _hover;
                    _dragOffset = _dragging.Root.position - point;
                }
                else if (_palette.Count > 0)
                {
                    PlaceCard(_palette[_paletteIndex], point);
                }
            }

            if (_dragging != null && mouse.leftButton.isPressed && onTable)
            {
                _dragging.Root.position = TableSpace.Current.ProjectOnTable(point + _dragOffset) + TableSpace.Current.Normal * 0.001f;
            }

            if (_dragging != null && mouse.leftButton.wasReleasedThisFrame)
            {
                _dragging = null;
                RefreshCovering();
            }

            if (mouse.rightButton.wasPressedThisFrame && _hover != null) ToggleTap(_hover);
        }

        private void HandleCardKeys(Keyboard kb)
        {
            if (_hover == null) return;

            if (kb[Key.X].wasPressedThisFrame || kb[Key.Delete].wasPressedThisFrame)
            {
                RemoveCard(_hover);
                _hover = null;
                return;
            }

            if (kb[Key.H].wasPressedThisFrame)
            {
                // Cover the card with a hand / lift the miniature: tracking lost, card still exists.
                _hover.Hidden = !_hover.Hidden;
                SetCardVisible(_hover, !_hover.Hidden);
            }
        }

        private void HandleGameKeys(Keyboard kb)
        {
            LivingEntityController entity = _hover != null ? _director.GetEntity(_hover.Id) : null;

            if (kb[Key.N].wasPressedThisFrame)
            {
                _turn = _turn.Opponent();
                if (_mtg != null) _mtg.BeginTurn(_turn);
                if (_warhammer != null) _warhammer.ResetMovement();
            }

            if (kb[Key.Space].wasPressedThisFrame && (entity == null || entity.Definition.System != GameSystem.Pokemon))
            {
                DamagePopupService.ShowInfo(TableSpace.Current.Origin + TableSpace.Current.Normal * 0.15f, "Espacio: pon el ratón sobre un Pokémon");
            }
            else if (entity != null && _pokemon != null && entity.Definition.System == GameSystem.Pokemon && kb[Key.Space].wasPressedThisFrame)
            {
                bool shift = kb[Key.LeftShift].isPressed || kb[Key.RightShift].isPressed;
                _pokemon.DeclareAttackOnNearestRival(entity, shift ? 1 : 0);
            }

            if (_warhammer == null) return;

            if (kb[Key.S].wasPressedThisFrame && entity != null && entity.Definition.System != GameSystem.Warhammer)
            {
                DamagePopupService.ShowInfo(entity.TopWorld, "S es para unidades de Warhammer");
            }
            else if (kb[Key.S].wasPressedThisFrame && entity == null)
            {
                DamagePopupService.ShowInfo(TableSpace.Current.Origin + TableSpace.Current.Normal * 0.15f, "Pon el ratón sobre una unidad");
            }
            else if (kb[Key.S].wasPressedThisFrame)
            {
                if (_warhammer.Selected != null && _warhammer.Selected.Side.IsRivalOf(entity.Side)) _warhammer.SetTarget(entity);
                else _warhammer.Select(entity);
            }

            if (kb[Key.F].wasPressedThisFrame) _warhammer.Shoot();
            if (kb[Key.V].wasPressedThisFrame) _warhammer.Advance();
            if (kb[Key.C].wasPressedThisFrame) _warhammer.ConfirmMove();
            if (kb[Key.Q].wasPressedThisFrame) _warhammer.CycleWeapon();
            if (kb[Key.Escape].wasPressedThisFrame) _warhammer.Deselect();
        }

        private void UpdateCamera(Mouse mouse)
        {
            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * orbitSpeed;
                pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, 10f, 89f);
            }

            distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * zoomSpeed, 0.3f, 3f);

            TableSpace table = TableSpace.Current;
            Quaternion orbit = Quaternion.AngleAxis(_yaw, table.Normal) * Quaternion.AngleAxis(pitch, table.transform.right);
            Vector3 offset = orbit * (-table.Forward * distance);
            viewCamera.transform.position = table.Origin + offset;
            viewCamera.transform.rotation = Quaternion.LookRotation(table.Origin - viewCamera.transform.position, table.Normal);
        }

        // ─────────────────────────────── Cards ───────────────────────────────

        private void PlaceCard(EntityDefinition definition, Vector3 point)
        {
            TableSpace table = TableSpace.Current;
            bool isMini = definition.System == GameSystem.Warhammer;
            float footprint = definition.ResolveFootprintMeters(CardSize.x);

            // Face the opponent: Player Two's cards are turned around, like on a real table.
            Vector3 forward = table.ResolveSide(point) == PlayerSide.PlayerTwo ? -table.Forward : table.Forward;
            var root = new GameObject($"SimCard {definition.DisplayName}").transform;
            root.SetPositionAndRotation(table.ProjectOnTable(point) + table.Normal * 0.001f, Quaternion.LookRotation(forward, table.Normal));

            var card = new SimCard
            {
                Id = $"sim:{_nextId++}",
                Definition = definition,
                Root = root,
                Footprint = footprint,
                Material = HoloMaterials.CreateUnlitTransparent(CardColor(definition), 2998),
            };

            GameObject face = GameObject.CreatePrimitive(isMini ? PrimitiveType.Cylinder : PrimitiveType.Quad);
            RemoveCollider(face);
            face.transform.SetParent(root, false);
            if (isMini)
            {
                face.transform.localScale = new Vector3(footprint, 0.002f, footprint);
            }
            else
            {
                face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                face.transform.localScale = new Vector3(CardSize.x, CardSize.y, 1f);
            }

            face.GetComponent<MeshRenderer>().sharedMaterial = card.Material;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.002f, isMini ? -footprint * 0.7f : -CardSize.y * 0.35f);
            labelGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            TextMeshPro label = labelGo.AddComponent<TextMeshPro>();
            label.text = definition.DisplayName;
            label.rectTransform.sizeDelta = new Vector2(Mathf.Max(CardSize.x, footprint) * 1.3f, 0.02f);
            label.enableAutoSizing = true; // fit the name inside the card whatever its length
            label.fontSizeMin = 0.02f;
            label.fontSizeMax = 0.2f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;

            _cards.Add(card);
            RefreshCovering();
            Report(card);
        }

        private void RemoveCard(SimCard card)
        {
            _director.ReportLost(card.Id);
            _cards.Remove(card);
            if (card.Material != null) Destroy(card.Material);
            Destroy(card.Root.gameObject, 1f); // keep the anchor alive through the grace period
            card.Root.gameObject.SetActive(false);
            RefreshCovering();
        }

        private void ToggleTap(SimCard card)
        {
            card.Tapped = !card.Tapped;
            card.Root.rotation = Quaternion.AngleAxis(card.Tapped ? 90f : -90f, TableSpace.Current.Normal) * card.Root.rotation;
        }

        /// <summary>A card placed on top of another hides it from the camera (enables evolution by stacking).</summary>
        private void RefreshCovering()
        {
            TableSpace table = TableSpace.Current;
            for (int i = 0; i < _cards.Count; i++)
            {
                SimCard lower = _cards[i];
                bool covered = false;
                for (int j = i + 1; j < _cards.Count && !covered; j++)
                {
                    float overlap = Mathf.Max(Mathf.Min(lower.Footprint, CardSize.x), Mathf.Min(_cards[j].Footprint, CardSize.x)) * 0.5f;
                    covered = table.TableDistance(lower.Root.position, _cards[j].Root.position) < overlap;
                }

                if (covered == lower.Covered) continue;
                lower.Covered = covered;
                SetCardVisible(lower, !covered && !lower.Hidden);
            }
        }

        private void SetCardVisible(SimCard card, bool visible)
        {
            foreach (Renderer r in card.Root.GetComponentsInChildren<Renderer>()) r.enabled = visible;
            if (!visible) _director.ReportLost(card.Id);
        }

        private void ReportVisibleCards()
        {
            foreach (SimCard card in _cards)
            {
                if (!card.Hidden && !card.Covered) Report(card);
            }
        }

        private void Report(SimCard card)
        {
            Vector2 size = card.Definition.System == GameSystem.Warhammer ? new Vector2(card.Footprint, card.Footprint) : CardSize;
            _director.ReportFound(card.Id, FirstReferenceName(card.Definition), card.Root, size);
        }

        private SimCard CardAt(Vector3 point)
        {
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                SimCard card = _cards[i];
                Vector3 local = card.Root.InverseTransformPoint(point);
                bool inside = card.Definition.System == GameSystem.Warhammer
                    ? new Vector2(local.x, local.z).magnitude <= card.Footprint * 0.5f
                    : Mathf.Abs(local.x) <= CardSize.x * 0.5f && Mathf.Abs(local.z) <= CardSize.y * 0.5f;
                if (inside) return card;
            }

            return null;
        }

        private bool TryGetTablePoint(Vector2 screen, out Vector3 point)
        {
            TableSpace table = TableSpace.Current;
            Ray ray = viewCamera.ScreenPointToRay(screen);
            var plane = new Plane(table.Normal, table.Origin);
            if (plane.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                Vector3 local = table.transform.InverseTransformPoint(point);
                return Mathf.Abs(local.x) <= tableSize.x * 0.5f && Mathf.Abs(local.z) <= tableSize.y * 0.5f;
            }

            point = default;
            return false;
        }

        private static void RemoveCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c == null) return;
            c.enabled = false; // ignored by raycasts right away; destroyed at end of frame
            Destroy(c);
        }

        private static string FirstReferenceName(EntityDefinition definition) =>
            definition.ReferenceImageNames.Count > 0 ? definition.ReferenceImageNames[0] : definition.name;

        private static Color CardColor(EntityDefinition definition) => definition.System switch
        {
            GameSystem.Pokemon => new Color(0.95f, 0.8f, 0.25f, 0.95f),
            GameSystem.MagicTheGathering => new Color(0.35f, 0.22f, 0.15f, 0.95f),
            _ => new Color(0.25f, 0.25f, 0.28f, 0.95f),
        };

        private void BuildTable()
        {
            TableSpace table = TableSpace.Current;
            Material felt = HoloMaterials.CreateUnlitTransparent(new Color(0.05f, 0.18f, 0.12f, 1f), 1990);
            Material line = HoloMaterials.CreateUnlitTransparent(new Color(1f, 1f, 1f, 0.25f), 1991);

            GameObject top = HoloMaterials.CreateFlatQuad("[SimTable]", felt);
            top.transform.SetPositionAndRotation(table.Origin, Quaternion.LookRotation(-table.Normal, table.Forward));
            top.transform.localScale = new Vector3(tableSize.x, tableSize.y, 1f);
            top.AddComponent<OwnedMaterials>().Own(felt);

            GameObject middle = HoloMaterials.CreateFlatQuad("[SimTableMidline]", line);
            middle.transform.SetPositionAndRotation(table.Origin + table.Normal * 0.0005f, Quaternion.LookRotation(-table.Normal, table.Forward));
            middle.transform.localScale = new Vector3(tableSize.x, 0.004f, 1f);
            middle.AddComponent<OwnedMaterials>().Own(line);
        }

        // ─────────────────────────────── On-screen help ───────────────────────────────

        private void OnGUI()
        {
            if (!showHelp) return;
            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true, wordWrap = true };

            string selected = _palette.Count > 0 ? _palette[_paletteIndex].DisplayName : "—";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>HoloTable · Desktop simulator</b>");
            sb.AppendLine($"Card: <b>{selected}</b>   (1-9 pick · Tab page)");
            int pageStart = _paletteIndex / PageSize * PageSize;
            for (int i = pageStart; i < Mathf.Min(pageStart + PageSize, _palette.Count); i++)
            {
                sb.AppendLine($"{(i == _paletteIndex ? "▶" : "  ")} {i - pageStart + 1}. {_palette[i].DisplayName} <i>({_palette[i].System})</i>");
            }

            sb.AppendLine();
            sb.AppendLine("L-click table: place · drag: move · stack on a card: evolve/cover");
            sb.AppendLine("R-click: tap/untap (90°) · H: hide/lift · X: remove");
            sb.AppendLine("Middle-drag: orbit · Wheel: zoom · N: next turn");
            sb.AppendLine("Pokémon: Space attack 1 · Shift+Space attack 2");
            sb.AppendLine("Warhammer: S select/target · F shoot · V advance · C confirm move · Q weapon · Esc");
            if (_mtg != null)
            {
                sb.AppendLine($"MTG life  P1 {_mtg.GetLife(PlayerSide.PlayerOne)} · P2 {_mtg.GetLife(PlayerSide.PlayerTwo)}");
            }

            sb.AppendLine($"Turn: {_turn}   Hover: {(_hover != null ? _hover.Definition.DisplayName : "—")}");
            GUI.Box(new Rect(10f, 10f, 430f, 150f + 20f * Mathf.Min(PageSize, _palette.Count)), sb.ToString(), _style);
        }
    }

    /// <summary>Destroys runtime materials together with their GameObject.</summary>
    public sealed class OwnedMaterials : MonoBehaviour
    {
        private readonly List<Material> _materials = new List<Material>();

        public void Own(Material material) => _materials.Add(material);

        private void OnDestroy()
        {
            foreach (Material m in _materials)
            {
                if (m != null) Destroy(m);
            }
        }
    }
}
