using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using CombatPrep.Core;
using CombatPrep.Skins;
using CombatPrep.Weapons;

namespace CombatPrep.UI
{
    /// <summary>
    /// Pre-game loadout screen: pick one of five weapons and one of six skins, see the
    /// result on a live rotating model, then drop in.
    ///
    /// The preview is a real weapon built by the same WeaponModelBuilder the game uses, on
    /// its own little stage parked far below the range with its own camera. That means what
    /// you see here is literally the gun you spawn with, skin included - there is no
    /// separate preview art to drift out of sync.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public event Action<WeaponEntry, SkinDefinition> OnStart;

        static readonly Vector3 StagePosition = new Vector3(0f, -500f, 0f);

        Canvas _canvas;
        RectTransform _root;
        Font _font;

        Camera _previewCam;
        Transform _stage;
        Transform _previewPivot;
        WeaponModel _previewModel;

        int _weaponIndex, _skinIndex;
        readonly List<Button> _weaponButtons = new();
        readonly List<Button> _skinButtons = new();
        Text _statLine, _roleLine;

        WeaponEntry Weapon => WeaponLibrary.All[_weaponIndex];
        SkinDefinition Skin => SkinLibrary.All[_skinIndex];

        /// <summary>The stage camera, so Bootstrap can park the audio listener on it.</summary>
        public Camera PreviewCamera => _previewCam;

        // ---------------------------------------------------------------------- lifecycle

        void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                 ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            EnsureEventSystem();
            BuildStage();
            BuildUi();
            RefreshSelection();

            Player.GameInput.LockCursor(false);
        }

        void OnDestroy()
        {
            if (_stage != null) Destroy(_stage.gameObject);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            // The project uses the Input System package, so the UI module must be the
            // Input System one - StandaloneInputModule throws under the new backend.
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var module = go.GetComponent<InputSystemUIInputModule>();
            if (module != null) module.AssignDefaultActions();
        }

        // -------------------------------------------------------------------- 3D preview

        void BuildStage()
        {
            _stage = Prim.Empty(null, "MenuStage", StagePosition);

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_stage, false);
            camGo.transform.localPosition = Vector3.zero;
            _previewCam = camGo.AddComponent<Camera>();
            _previewCam.fieldOfView = 34f;
            _previewCam.nearClipPlane = 0.03f;
            _previewCam.farClipPlane = 60f;
            _previewCam.clearFlags = CameraClearFlags.SolidColor;
            _previewCam.backgroundColor = new Color(0.055f, 0.06f, 0.075f);
            // No AudioListener here: Bootstrap owns the only one and points its ListenerRig
            // at this camera while the menu is up. A second listener mutes a frame and warns.
            Bootstrap.ConfigureCamera(_previewCam);

            // Backdrop panel, lifted slightly so the gun reads against a gradient rather
            // than flat black.
            Prim.Box(_stage, "Backdrop", new Vector3(0f, 0f, 3.2f), new Vector3(9f, 5f, 0.2f),
                     new Color(0.10f, 0.11f, 0.14f), 0f, 0.25f);
            Prim.Box(_stage, "Riser", new Vector3(0f, -0.62f, 1.2f), new Vector3(3.2f, 0.06f, 1.6f),
                     new Color(0.14f, 0.15f, 0.18f), 0.3f, 0.5f);

            var keyGo = new GameObject("KeyLight");
            keyGo.transform.SetParent(_stage, false);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.9f;
            key.color = new Color(1f, 0.96f, 0.90f);
            keyGo.transform.localRotation = Quaternion.Euler(28f, 152f, 0f);

            var rimGo = new GameObject("RimLight");
            rimGo.transform.SetParent(_stage, false);
            rimGo.transform.localPosition = new Vector3(-1.4f, 0.6f, 1.9f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = new Color(0.45f, 0.65f, 1f);
            rim.intensity = 5f;
            rim.range = 7f;

            _previewPivot = Prim.Empty(_stage, "PreviewPivot", new Vector3(0f, -0.02f, 1.05f));
        }

        void RebuildPreview()
        {
            if (_previewModel != null && _previewModel.Root != null)
                Destroy(_previewModel.Root.gameObject);

            _previewModel = WeaponModelBuilder.Build(_previewPivot, Weapon.Shape);
            SkinApplier.Apply(_previewModel, Skin);

            // Centre the model on the pivot regardless of its length.
            _previewModel.Root.localPosition = new Vector3(-0.02f, 0f, -0.06f);
        }

        void Update()
        {
            if (_previewPivot != null)
                _previewPivot.localRotation = Quaternion.Euler(
                    -8f + Mathf.Sin(Time.time * 0.6f) * 4f,
                    Time.time * 22f + 40f,
                    0f);
        }

        // --------------------------------------------------------------------------- UI

        void BuildUi()
        {
            var go = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = go.GetComponent<RectTransform>();

            Title();
            WeaponColumn();
            SkinColumn();
            Footer();
        }

        void Title()
        {
            var t = Text_("COMBATPREP", 58, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -46f), 900f);
            t.color = Color.white;

            var s = Text_("SELECT LOADOUT", 22, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -112f), 900f);
            s.color = new Color(1f, 0.62f, 0.20f);
        }

        void WeaponColumn()
        {
            Header("WEAPON", new Vector2(0f, 1f), new Vector2(72f, -178f));

            var all = WeaponLibrary.All;
            for (int i = 0; i < all.Length; i++)
            {
                int index = i;
                var b = Button_(all[i].Def.DisplayName, new Vector2(0f, 1f),
                                new Vector2(72f, -222f - i * 72f), new Vector2(390f, 60f), 24,
                                () => { _weaponIndex = index; RefreshSelection(); });
                _weaponButtons.Add(b);
            }

            _roleLine = Text_("", 20, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(74f, -600f), 420f);
            _roleLine.color = new Color(1f, 0.62f, 0.20f);

            _statLine = Text_("", 19, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(74f, -632f), 420f);
            _statLine.color = new Color(1f, 1f, 1f, 0.72f);
            _statLine.rectTransform.sizeDelta = new Vector2(420f, 200f);
            _statLine.verticalOverflow = VerticalWrapMode.Overflow;
        }

        void SkinColumn()
        {
            Header("FINISH", new Vector2(1f, 1f), new Vector2(-72f, -178f), TextAnchor.UpperRight);

            var all = SkinLibrary.All;
            for (int i = 0; i < all.Length; i++)
            {
                int index = i;
                var b = Button_(all[i].DisplayName, new Vector2(1f, 1f),
                                new Vector2(-72f, -222f - i * 72f), new Vector2(390f, 60f), 22,
                                () => { _skinIndex = index; RefreshSelection(); });

                // Colour swatch on the left edge of each row.
                var sw = Hud.I != null ? Hud.I.MakeImage(b.transform, "Swatch", all[i].Swatch)
                                       : NewImage(b.transform, "Swatch", all[i].Swatch);
                var rt = sw.rectTransform;
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(30f, 30f);
                rt.anchoredPosition = new Vector2(14f, 0f);

                _skinButtons.Add(b);
            }
        }

        void Footer()
        {
            var start = Button_("DEPLOY", new Vector2(0.5f, 0f), new Vector2(0f, 74f), new Vector2(320f, 72f), 30,
                                () =>
                                {
                                    OnStart?.Invoke(Weapon, Skin);
                                });
            start.image.color = new Color(0.82f, 0.36f, 0.08f, 0.95f);

            var hint = Text_("WASD move   ·   RMB aim   ·   R reload   ·   SHIFT sprint   ·   CTRL crouch   ·   ESC loadout",
                             17, TextAnchor.LowerCenter, new Vector2(0.5f, 0f), new Vector2(0f, 30f), 1400f);
            hint.color = new Color(1f, 1f, 1f, 0.42f);
        }

        void RefreshSelection()
        {
            for (int i = 0; i < _weaponButtons.Count; i++) Tint(_weaponButtons[i], i == _weaponIndex);
            for (int i = 0; i < _skinButtons.Count; i++) Tint(_skinButtons[i], i == _skinIndex);

            var e = Weapon;
            var d = e.Def;
            _roleLine.text = e.Role.ToUpperInvariant();

            string dmg = d.PelletsPerShot > 1
                ? $"{d.Damage:0} x {d.PelletsPerShot} pellets"
                : $"{d.Damage:0}";

            _statLine.text =
                $"Damage      {dmg}\n" +
                $"Fire rate   {d.RoundsPerMinute:0} rpm  ({d.Mode})\n" +
                $"Magazine    {d.MagSize}  (+{d.ReserveAmmo})\n" +
                $"Recoil      {d.RecoilVertical:0.00} up / {d.RecoilHorizontal:0.00} side\n" +
                $"Sight       {e.Shape.Optic}\n" +
                $"Muzzle vel  {d.MuzzleVelocity:0} m/s";

            RebuildPreview();
        }

        static void Tint(Button b, bool selected)
        {
            b.image.color = selected
                ? new Color(0.85f, 0.42f, 0.12f, 0.92f)
                : new Color(1f, 1f, 1f, 0.09f);

            var label = b.GetComponentInChildren<Text>();
            if (label != null) label.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.78f);
        }

        // ----------------------------------------------------------------- UI factories

        void Header(string text, Vector2 anchor, Vector2 pos, TextAnchor align = TextAnchor.UpperLeft)
        {
            var t = Text_(text, 20, align, anchor, pos, 390f);
            t.color = new Color(1f, 1f, 1f, 0.45f);
        }

        Text Text_(string content, int size, TextAnchor align, Vector2 anchor, Vector2 pos, float width)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(_root, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.text = content;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.25f;

            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(width, 60f);
            rt.anchoredPosition = pos;
            return t;
        }

        Button Button_(string label, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(_root, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.09f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());

            var text = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            text.transform.SetParent(go.transform, false);
            var t = text.GetComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.text = label;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;

            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(48f, 0f);
            trt.offsetMax = new Vector2(-12f, 0f);

            return btn;
        }

        static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
