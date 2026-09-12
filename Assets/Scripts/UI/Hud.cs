using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CombatPrep.Weapons;

namespace CombatPrep.UI
{
    /// <summary>
    /// Entire HUD built in code from uGUI Images and the built-in font - no sprites, no
    /// prefabs. The crosshair gap is derived from the real spread cone rather than faked,
    /// so the UI actually teaches the mechanic: it blooms exactly as much as your bullets
    /// scatter.
    ///
    /// Hit reporting is per trigger pull, not per bullet, so a shotgun's nine pellets read
    /// as one hit with one combined damage number instead of nine of everything.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public static Hud I { get; private set; }

        [Header("Crosshair")]
        public Color CrosshairColor = new Color(1f, 0.22f, 0.16f, 0.95f);
        public float LineLength = 9f;
        public float LineThickness = 2f;
        public float MinGap = 3f;

        Camera _cam;
        RectTransform _root;
        GameObject _gameplayRoot;
        readonly RectTransform[] _lines = new RectTransform[4];
        Image _dot;
        RectTransform _hitmarker;
        readonly Image[] _hitmarkerLines = new Image[4];
        float _hitmarkerUntil;
        Color _hitmarkerColor = Color.white;

        Text _ammo, _stats, _weaponName;
        Font _font;
        CrosshairStyle _style = CrosshairStyle.Cross;
        bool _crosshairVisible = true;

        readonly List<Text> _damagePool = new();
        readonly List<Vector3> _damageWorld = new();
        readonly List<float> _damageExpiry = new();

        int _shots, _hits, _headshots, _kills;

        public Font Font => _font;
        public RectTransform Root => _root;

        void Awake()
        {
            I = this;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                 ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildCanvas();
            SetGameplayVisible(false);
        }

        public void SetCamera(Camera cam) => _cam = cam;

        public void SetGameplayVisible(bool visible)
        {
            if (_gameplayRoot != null) _gameplayRoot.SetActive(visible);
        }

        public void ResetStats()
        {
            _shots = _hits = _headshots = _kills = 0;
            RefreshStats();
        }

        void BuildCanvas()
        {
            var canvasGo = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvasGo.GetComponent<RectTransform>();

            var holder = new GameObject("Gameplay", typeof(RectTransform));
            holder.transform.SetParent(_root, false);
            var hrt = holder.GetComponent<RectTransform>();
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            _gameplayRoot = holder;
            var parent = hrt;

            // --- crosshair: four lines around a centre dot ---
            for (int i = 0; i < 4; i++)
            {
                bool vertical = i < 2;
                var img = MakeImage(parent, $"Cross{i}", CrosshairColor);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = vertical
                    ? new Vector2(LineThickness, LineLength)
                    : new Vector2(LineLength, LineThickness);
                _lines[i] = rt;
            }

            _dot = MakeImage(parent, "CrossDot", CrosshairColor);
            _dot.rectTransform.anchorMin = _dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _dot.rectTransform.sizeDelta = new Vector2(2f, 2f);

            // --- hitmarker: four diagonals, hidden until a hit lands ---
            var hmGo = new GameObject("Hitmarker", typeof(RectTransform));
            hmGo.transform.SetParent(parent, false);
            _hitmarker = hmGo.GetComponent<RectTransform>();
            _hitmarker.anchorMin = _hitmarker.anchorMax = new Vector2(0.5f, 0.5f);
            _hitmarker.sizeDelta = Vector2.zero;

            for (int i = 0; i < 4; i++)
            {
                var img = MakeImage(_hitmarker, $"Hm{i}", Color.white);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(2.5f, 12f);
                float d = 13f;
                rt.anchoredPosition = i switch
                {
                    0 => new Vector2(-d, d),
                    1 => new Vector2(d, d),
                    2 => new Vector2(-d, -d),
                    _ => new Vector2(d, -d)
                };
                rt.localRotation = Quaternion.Euler(0f, 0f, (i == 0 || i == 3) ? 45f : -45f);
                _hitmarkerLines[i] = img;
            }
            _hitmarker.gameObject.SetActive(false);

            // --- text ---
            _ammo = MakeText(parent, "Ammo", 44, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-48f, 44f));
            _weaponName = MakeText(parent, "WeaponName", 22, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-48f, 104f));
            _weaponName.color = new Color(1f, 1f, 1f, 0.55f);
            _stats = MakeText(parent, "Stats", 20, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(40f, -36f));
            _stats.color = new Color(1f, 1f, 1f, 0.7f);
        }

        public Image MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;          // no sprite assigned renders a solid rect
            img.raycastTarget = false;
            return img;
        }

        public Text MakeText(Transform parent, string name, int size, TextAnchor anchor,
                             Vector2 pivotAnchor, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = pivotAnchor;
            rt.pivot = pivotAnchor;
            rt.sizeDelta = new Vector2(520f, 60f);
            rt.anchoredPosition = offset;
            return t;
        }

        /// <summary>Crosshair gap from the true cone half-angle, projected into screen pixels.</summary>
        public void SetSpread(float spreadDegrees, float fovDegrees)
        {
            float halfHeight = _root.rect.height * 0.5f;
            float pixels = Mathf.Tan(spreadDegrees * Mathf.Deg2Rad)
                         / Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad) * halfHeight;
            float gap = MinGap + pixels;

            _lines[0].anchoredPosition = new Vector2(0f, gap + LineLength * 0.5f);
            _lines[1].anchoredPosition = new Vector2(0f, -(gap + LineLength * 0.5f));
            _lines[2].anchoredPosition = new Vector2(-(gap + LineLength * 0.5f), 0f);
            _lines[3].anchoredPosition = new Vector2(gap + LineLength * 0.5f, 0f);
        }

        /// <summary>Per-weapon hip reticle. Called once when a weapon is equipped.</summary>
        public void SetCrosshairStyle(CrosshairStyle style, Color color)
        {
            _style = style;

            foreach (var l in _lines)
            {
                var img = l.GetComponent<Image>();
                if (img != null) img.color = color;
            }
            _dot.color = color;

            // A lone point needs to be big enough to find; the cross already reads, so its
            // centre dot stays small.
            _dot.rectTransform.sizeDelta = style == CrosshairStyle.Dot
                ? new Vector2(5f, 5f)
                : new Vector2(2f, 2f);

            SetCrosshairVisible(_crosshairVisible);
        }

        public void SetCrosshairVisible(bool visible)
        {
            _crosshairVisible = visible;

            bool showArms = visible && _style == CrosshairStyle.Cross;
            bool showDot = visible && _style != CrosshairStyle.None;

            foreach (var l in _lines) l.gameObject.SetActive(showArms);
            _dot.gameObject.SetActive(showDot);
        }

        public void SetAmmo(int mag, int reserve, bool reloading)
        {
            _ammo.text = reloading ? "-- / " + reserve : $"{mag} / {reserve}";
            _ammo.color = (!reloading && mag == 0) ? new Color(1f, 0.3f, 0.25f) : Color.white;
        }

        public void SetWeaponName(string n) => _weaponName.text = n;

        /// <summary>One call per trigger pull, so accuracy counts shots and not pellets.</summary>
        public void RegisterShot(bool hit, bool headshot, bool killed)
        {
            _shots++;
            if (hit) _hits++;
            if (headshot) _headshots++;
            if (killed) _kills++;
            RefreshStats();
        }

        /// <summary>Combined result of one trigger pull: hitmarker plus one damage number.</summary>
        public void ReportHit(float totalDamage, bool headshot, bool killed, Vector3 point)
        {
            _hitmarkerColor = killed ? new Color(1f, 0.35f, 0.3f)
                            : headshot ? new Color(1f, 0.85f, 0.3f)
                            : Color.white;
            _hitmarkerUntil = Time.time + 0.11f;
            _hitmarker.gameObject.SetActive(true);
            _hitmarker.localScale = Vector3.one * (killed ? 1.35f : 1f);

            SpawnDamageNumber(totalDamage, headshot, point);
        }

        void RefreshStats()
        {
            float acc = _shots > 0 ? 100f * _hits / _shots : 0f;
            float hs = _hits > 0 ? 100f * _headshots / _hits : 0f;
            _stats.text = $"Down {_kills}    Acc {acc:0.0}%    HS {hs:0.0}%    Shots {_shots}";
        }

        void SpawnDamageNumber(float damage, bool headshot, Vector3 point)
        {
            Text t = null;
            for (int i = 0; i < _damagePool.Count; i++)
                if (!_damagePool[i].gameObject.activeSelf) { t = _damagePool[i]; break; }

            if (t == null)
            {
                t = MakeText(_gameplayRoot.transform, "Dmg", 26, TextAnchor.MiddleCenter,
                             new Vector2(0.5f, 0.5f), Vector2.zero);
                t.rectTransform.sizeDelta = new Vector2(220f, 40f);
                _damagePool.Add(t);
                _damageWorld.Add(Vector3.zero);
                _damageExpiry.Add(0f);
            }

            int idx = _damagePool.IndexOf(t);
            t.gameObject.SetActive(true);
            t.text = Mathf.RoundToInt(damage).ToString();
            t.color = headshot ? new Color(1f, 0.85f, 0.3f) : Color.white;
            t.fontSize = headshot ? 34 : 26;
            _damageWorld[idx] = point + Random.insideUnitSphere * 0.1f;
            _damageExpiry[idx] = Time.time + 0.85f;
        }

        void LateUpdate()
        {
            if (_hitmarker.gameObject.activeSelf)
            {
                float remain = _hitmarkerUntil - Time.time;
                if (remain <= 0f) _hitmarker.gameObject.SetActive(false);
                else
                {
                    float a = Mathf.Clamp01(remain / 0.11f);
                    var c = _hitmarkerColor; c.a = a;
                    foreach (var l in _hitmarkerLines) l.color = c;
                }
            }

            if (_cam == null) return;

            for (int i = 0; i < _damagePool.Count; i++)
            {
                var t = _damagePool[i];
                if (!t.gameObject.activeSelf) continue;

                float remain = _damageExpiry[i] - Time.time;
                if (remain <= 0f) { t.gameObject.SetActive(false); continue; }

                float age = 0.85f - remain;
                Vector3 world = _damageWorld[i] + Vector3.up * (age * 0.55f);
                Vector3 screen = _cam.WorldToScreenPoint(world);

                if (screen.z < 0f) { t.gameObject.SetActive(false); continue; }

                t.rectTransform.position = screen;
                var c = t.color; c.a = Mathf.Clamp01(remain / 0.45f); t.color = c;
            }
        }
    }
}
