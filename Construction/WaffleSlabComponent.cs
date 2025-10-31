using Eto.Forms;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Housefly.Construction
{
    public class WaffleSlabComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the WaffleSlabComponent class.
        /// </summary>
        public WaffleSlabComponent()
          : base("WaffleSlabComponent", "WaffleSlab",
              "Creates a waffle slabs as basic lines or as a brep",
              "Housefly", "Construction")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddRectangleParameter("Base Rectangle", "R", "Overall slab boundary", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Pillar Planes", "P", "Pillar center planes", GH_ParamAccess.list);
            pManager.AddNumberParameter("Offset Length", "L", "Half-size of pillar solid from center", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Rib Width", "W_rib", "Rib width", GH_ParamAccess.item, 0.2);
            pManager.AddNumberParameter("Void Length", "L_void", "Void (square) length", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Border Widths", "Borders", "Widths for solid borders [left, right, bottom, top]", GH_ParamAccess.list, 0.5);
            pManager.AddBooleanParameter("CalculateBrep", "B", "Wether calculate the whole brep or not", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Void Height", "H_void", "Height of the void space (beneath top slab)", GH_ParamAccess.item, 0.2);
            pManager.AddNumberParameter("Top Slab Height", "H_top", "Thickness of the top solid layer", GH_ParamAccess.item, 0.1);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddRectangleParameter("Solids", "S", "Pillar solids (expanded to grid boundaries)", GH_ParamAccess.list);
            pManager.AddRectangleParameter("Borders", "B", "Solid border rectangles", GH_ParamAccess.list);
            pManager.AddRectangleParameter("Ribs", "R", "Rib grid rectangles", GH_ParamAccess.list);
            pManager.AddRectangleParameter("Voids", "V", "Void grid rectangles", GH_ParamAccess.list);
            pManager.AddBrepParameter("Slab Brep", "Slab", "Concrete slab Brep (final geometry)", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Rectangle3d rect = Rectangle3d.Unset;
            var planes = new List<Plane>();
            double offset = 0, 
                ribWidth = 0,
                voidLength = 0,
                voidHeight = 0,
                topSlabHeight = 0;
            var borderWidths = new List<double>();
            bool calculateBrep = false;


            if (!DA.GetData(0, ref rect)) return;
            if (!DA.GetDataList(1, planes)) return;
            if (!DA.GetData(2, ref offset)) return;
            if (!DA.GetData(3, ref ribWidth)) return;
            if (!DA.GetData(4, ref voidLength)) return;
            if (!DA.GetDataList(5, borderWidths)) return;
            if (!DA.GetData(6, ref calculateBrep)) return;
            if (!DA.GetData(7, ref voidHeight)) return;
            if (!DA.GetData(8, ref topSlabHeight)) return;
            if (!rect.IsValid) return;

            // Normalize border widths
            while (borderWidths.Count < 4) borderWidths.Add(0);
            double left = borderWidths[0];
            double right = borderWidths[1];
            double bottom = borderWidths[2];
            double top = borderWidths[3];

            Plane basePlane = rect.Plane;
            basePlane.RemapToPlaneSpace(rect.Corner(0), out Point3d p0);
            basePlane.RemapToPlaneSpace(rect.Corner(2), out Point3d p2);

            double xMin = Math.Min(p0.X, p2.X);
            double xMax = Math.Max(p0.X, p2.X);
            double yMin = Math.Min(p0.Y, p2.Y);
            double yMax = Math.Max(p0.Y, p2.Y);

            // Define inner region after border offsets
            double innerXMin = xMin + left;
            double innerXMax = xMax - right;
            double innerYMin = yMin + bottom;
            double innerYMax = yMax - top;

            // 1) Border rectangles
            var bordersOut = new List<Rectangle3d>
        {
            new Rectangle3d(basePlane, new Interval(xMin, innerXMin), new Interval(yMin, yMax)), // Left
            new Rectangle3d(basePlane, new Interval(innerXMax, xMax), new Interval(yMin, yMax)), // Right
            new Rectangle3d(basePlane, new Interval(innerXMin, innerXMax), new Interval(yMin, innerYMin)), // Bottom
            new Rectangle3d(basePlane, new Interval(innerXMin, innerXMax), new Interval(innerYMax, yMax))  // Top
        };

            // 2) Grid (ribs + voids)
            var xIntervals = BuildAlternatingIntervals(innerXMin, innerXMax, ribWidth, voidLength);
            var yIntervals = BuildAlternatingIntervals(innerYMin, innerYMax, ribWidth, voidLength);

            var ribsOut = new List<Rectangle3d>();
            var voidsOut = new List<Rectangle3d>();

            for (int ix = 0; ix < xIntervals.Count; ix++)
            {
                for (int iy = 0; iy < yIntervals.Count; iy++)
                {
                    var xi = xIntervals[ix];
                    var yi = yIntervals[iy];
                    bool isVoid = (!xi.IsRib) && (!yi.IsRib);

                    var r3 = new Rectangle3d(basePlane, new Interval(xi.Start, xi.End), new Interval(yi.Start, yi.End));

                    if (isVoid) voidsOut.Add(r3);
                    else ribsOut.Add(r3);
                }
            }

            // 3) Solids (pillars)
            var solidsOut = new List<Rectangle3d>();

            foreach (var pl in planes)
            {
                basePlane.RemapToPlaneSpace(pl.Origin, out Point3d pt);
                double cx = pt.X;
                double cy = pt.Y;

                // Pillar extents in local coordinates
                double r0 = cx - offset;
                double r1 = cx + offset;
                double s0 = cy - offset;
                double s1 = cy + offset;

                // Skip if outside slab rectangle
                if (r1 < xMin || r0 > xMax || s1 < yMin || s0 > yMax)
                    continue;

                // Expand to match grid boundaries
                double newX0 = r0, newX1 = r1, newY0 = s0, newY1 = s1;

                foreach (var xi in xIntervals)
                    if (RangesOverlap(r0, r1, xi.Start, xi.End))
                    { newX0 = Math.Min(newX0, xi.Start); newX1 = Math.Max(newX1, xi.End); }

                foreach (var yi in yIntervals)
                    if (RangesOverlap(s0, s1, yi.Start, yi.End))
                    { newY0 = Math.Min(newY0, yi.Start); newY1 = Math.Max(newY1, yi.End); }

                // Clamp to full outer rectangle
                newX0 = Math.Max(newX0, xMin);
                newX1 = Math.Min(newX1, xMax);
                newY0 = Math.Max(newY0, yMin);
                newY1 = Math.Min(newY1, yMax);

                if (newX1 > newX0 && newY1 > newY0)
                    solidsOut.Add(new Rectangle3d(basePlane, new Interval(newX0, newX1), new Interval(newY0, newY1)));
            }


            // 4) Outputs
            DA.SetDataList(0, solidsOut);
            DA.SetDataList(1, bordersOut);
            DA.SetDataList(2, ribsOut);
            DA.SetDataList(3, voidsOut);
            DA.SetData(4, calculateBrep ? CreateConcreteSlab(rect, voidsOut, voidHeight, topSlabHeight) : null);
        }

        // ---------- Helpers ----------

        private List<Interval1D> BuildAlternatingIntervals(double start, double end, double ribW, double voidL)
        {
            var list = new List<Interval1D>();
            double cur = start;
            bool isRib = true;
            const double eps = 1e-9;

            while (cur + eps < end)
            {
                double len = isRib ? ribW : voidL;
                double s = cur;
                double e = Math.Min(end, cur + len);
                if (e - s <= eps) break;

                list.Add(new Interval1D(s, e, isRib));
                cur = e;
                isRib = !isRib;
            }
            return list;
        }

        private bool RangesOverlap(double a0, double a1, double b0, double b1)
        {
            return (a1 > b0) && (b1 > a0);
        }

        private class Interval1D
        {
            public double Start, End;
            public bool IsRib;
            public Interval1D(double s, double e, bool r) { Start = s; End = e; IsRib = r; }
        }

        // create solid brep for final concrete slab

        private Brep CreateConcreteSlab(Rectangle3d rect, List<Rectangle3d> voidsOut, double voidHeight, double topSlabHeight)
        {
            if (!rect.IsValid) return null;
            if (voidHeight <= 0 && topSlabHeight <= 0) return null;

            // Total height
            double totalHeight = voidHeight + topSlabHeight;

            // Create the full concrete slab (as extrusion of the full rectangle)
            var slabCrv = rect.ToNurbsCurve();

            Brep slabBrep = Extrusion.CreateExtrusion(slabCrv, rect.Plane.ZAxis * totalHeight).ToBrep();
            if (!slabBrep.IsSolid)
                slabBrep = slabBrep.CapPlanarHoles(RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);
            if (slabBrep == null) return null;

            // Create void solids (extruded downward)
            var voidBreps = new List<Brep>();
            Vector3d down = -rect.Plane.ZAxis * voidHeight;

            foreach (var vRect in voidsOut)
            {
                if (!vRect.IsValid) continue;
                var vCrv = vRect.ToNurbsCurve();

                Brep voidB = Extrusion.CreateExtrusion(vCrv, down).ToBrep();
                if (!voidB.IsSolid)
                    voidB = voidB.CapPlanarHoles(RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);
                if (voidB != null) voidBreps.Add(voidB);
            }

            if (voidBreps.Count == 0)
                return slabBrep; // no voids -> solid slab

            // Perform boolean difference (subtract voids)
            var diff = Brep.CreateBooleanDifference(new List<Brep>() { slabBrep }, voidBreps, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);

            if (diff == null || diff.Length == 0)
                return slabBrep; // fallback

            return diff[0];
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("1191602B-13B7-4220-BF7A-D96416F3167B"); }
        }
    }
}