using System.Collections.Generic;
using UnityEngine;
using CombatPrep.Core;

namespace CombatPrep.Targets
{
    /// <summary>Everything the Target component needs to drive one built target.</summary>
    public class TargetVisual
    {
        public Transform Root;
        public Transform Swing;    // pivots at the top bar; the board hangs from it
        public Transform Board;
        public readonly List<Renderer> Renderers = new();
    }

    /// <summary>
    /// Builds a paper silhouette target hung in a steel frame - the shooting-range kind.
    /// The printed sheet is a generated texture (see Tex.TargetSheet) on a quad, backed by
    /// a card board, with separate colliders over the head and centre-mass regions of the
    /// print so the hitzones line up with what you actually see.
    /// </summary>
    public static class TargetBuilder
    {
        // Board is 2:3 to match the sheet texture, so the printed rings stay circular.
        public const float BoardWidth = 0.62f;
        public const float BoardHeight = 0.93f;
        public const float PivotHeight = 1.62f;

        static Material _sheetMat;
        static Material _backMat;

        static Material SheetMaterial
        {
            get
            {
                if (_sheetMat == null)
                {
                    // One 512x768 sheet, generated once and shared by every target.
                    var tex = Tex.TargetSheet(512);
                    _sheetMat = Mat.Textured(tex, 0f, 0.10f);
                }
                return _sheetMat;
            }
        }

        static Material BackMaterial
        {
            get
            {
                if (_backMat == null)
                    _backMat = Mat.Textured(Tex.Weathered(new Color(0.58f, 0.50f, 0.38f),
                                                          Mat.RustDark, 0.25f, 4), 0f, 0.12f);
                return _backMat;
            }
        }

        public static TargetVisual Build(Transform parent, Target owner)
        {
            var v = new TargetVisual();
            v.Root = Prim.Empty(parent, "PaperTarget");

            BuildFrame(v.Root, v);

            // --- swing pivot at the top bar; everything below hangs off it ---
            v.Swing = Prim.Empty(v.Root, "Swing", new Vector3(0f, PivotHeight, 0f));

            float boardCY = -(BoardHeight * 0.5f + 0.035f);   // below the pivot
            var board = Prim.Empty(v.Swing, "Board", new Vector3(0f, boardCY, 0f));
            v.Board = board;

            // Hangers connecting the board to the bar.
            Prim.Box(board, "HangL", new Vector3(-0.22f, BoardHeight * 0.5f + 0.018f, 0f),
                     new Vector3(0.018f, 0.045f, 0.012f), Mat.Steel, 0.7f, 0.4f);
            Prim.Box(board, "HangR", new Vector3(0.22f, BoardHeight * 0.5f + 0.018f, 0f),
                     new Vector3(0.018f, 0.045f, 0.012f), Mat.Steel, 0.7f, 0.4f);

            // --- card backing, and the printed sheet on its front face ---
            var backing = Prim.Box(board, "Backing", Vector3.zero,
                                   new Vector3(BoardWidth, BoardHeight, 0.022f),
                                   Color.white, 0f, 0.12f, collider: true);
            Prim.SetMaterial(backing, BackMaterial);
            Zone(backing, owner, Targets.Zone.Limb);

            // Quad normals face local -Z in Unity, which is toward the firing line.
            var sheet = Prim.Quad(board, "Sheet", new Vector3(0f, 0f, -0.0125f),
                                  new Vector2(BoardWidth, BoardHeight), SheetMaterial);

            // --- hitzones, positioned to match the printed silhouette ---
            // Derived from the Tex.TargetSheet layout: head sits at v=0.83 of the sheet,
            // centre mass around v=0.47, both converted into board-local metres.
            var head = Prim.Box(board, "HeadZone", new Vector3(0f, 0.307f, -0.026f),
                                new Vector3(0.150f, 0.190f, 0.020f), Color.white, 0f, 0.3f, collider: true);
            HideZone(head);
            Zone(head, owner, Targets.Zone.Head);

            var body = Prim.Box(board, "BodyZone", new Vector3(0f, -0.055f, -0.026f),
                                new Vector3(0.460f, 0.620f, 0.020f), Color.white, 0f, 0.3f, collider: true);
            HideZone(body);
            Zone(body, owner, Targets.Zone.Body);

            foreach (var r in v.Root.GetComponentsInChildren<Renderer>())
                v.Renderers.Add(r);

            return v;
        }

        static void BuildFrame(Transform root, TargetVisual v)
        {
            var frame = Prim.Empty(root, "Frame");

            Prim.Box(frame, "PostL", new Vector3(-0.40f, 0.85f, 0.03f),
                     new Vector3(0.045f, 1.70f, 0.045f), Mat.Steel, 0.55f, 0.35f, collider: true);
            Prim.Box(frame, "PostR", new Vector3(0.40f, 0.85f, 0.03f),
                     new Vector3(0.045f, 1.70f, 0.045f), Mat.Steel, 0.55f, 0.35f, collider: true);
            Prim.Box(frame, "TopBar", new Vector3(0f, PivotHeight + 0.045f, 0.03f),
                     new Vector3(0.90f, 0.045f, 0.045f), Mat.Steel, 0.55f, 0.35f, collider: true);

            // Feet, angled back so the frame reads as free-standing.
            Prim.Box(frame, "FootL", new Vector3(-0.40f, 0.03f, 0.14f),
                     new Vector3(0.07f, 0.06f, 0.42f), Mat.Steel, 0.5f, 0.3f);
            Prim.Box(frame, "FootR", new Vector3(0.40f, 0.03f, 0.14f),
                     new Vector3(0.07f, 0.06f, 0.42f), Mat.Steel, 0.5f, 0.3f);

            // Sandbag ballast at the base - reads as a real range, and breaks the silhouette.
            for (int i = 0; i < 3; i++)
            {
                float x = -0.30f + i * 0.30f;
                Prim.Capsule(frame, $"Bag{i}", new Vector3(x, 0.085f, 0.22f), 0.26f, 0.34f,
                             i % 2 == 0 ? Mat.Canvas : Mat.SandDark, false, new Vector3(0f, 0f, 90f));
            }

            // One hidden box over the ballast. The capsules themselves are collider-less
            // (non-uniformly scaled CapsuleColliders are unreliable), so without this you
            // walk straight through the bags at the foot of every target.
            var bagCol = Prim.Box(frame, "BagCollider", new Vector3(0f, 0.108f, 0.22f),
                                  new Vector3(0.94f, 0.215f, 0.28f), Color.white, 0f, 0.3f, collider: true);
            bagCol.GetComponent<MeshRenderer>().enabled = false;
        }

        static void Zone(Transform t, Target owner, Zone zone)
        {
            var hz = t.gameObject.AddComponent<HitZone>();
            hz.Owner = owner;
            hz.Zone = zone;
        }

        /// <summary>Hitzone volumes are collision-only; the print is what the player reads.</summary>
        static void HideZone(Transform t)
        {
            var r = t.GetComponent<MeshRenderer>();
            if (r != null) Object.Destroy(r);
        }
    }
}
