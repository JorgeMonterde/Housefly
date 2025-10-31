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
            pManager.AddNumberParameter("Border Widths", "Borders", "Widths for solid borders [left, right, bottom, top]", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddRectangleParameter("Solids", "S", "Pillar solids (expanded to grid boundaries)", GH_ParamAccess.list);
            pManager.AddRectangleParameter("Borders", "B", "Solid border rectangles", GH_ParamAccess.list);
            pManager.AddRectangleParameter("Ribs", "R", "Rib grid rectangles", GH_ParamAccess.list);
            pManager.AddRectangleParameter("Voids", "V", "Void grid rectangles (filtered: no overlaps under solids)", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Rectangle3d rect = Rectangle3d.Unset;
            var planes = new List<Plane>();
            double offset = 0;
            double ribWidth = 0;
            double voidLength = 0;
            var borderWidths = new List<double>();

            if (!DA.GetData(0, ref rect)) return;
            if (!DA.GetDataList(1, planes)) return;
            if (!DA.GetData(2, ref offset)) return;
            if (!DA.GetData(3, ref ribWidth)) return;
            if (!DA.GetData(4, ref voidLength)) return;
            if (!DA.GetDataList(5, borderWidths)) return;
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

            // Step 1️⃣ Base inner region from nominal borders
            double innerXMin = xMin + left;
            double innerXMax = xMax - right;
            double innerYMin = yMin + bottom;
            double innerYMax = yMax - top;

            // Step 2️⃣ Build a temporary full grid to find snapping lines
            var tempX = BuildAlternatingIntervals(xMin, xMax, ribWidth, voidLength);
            var tempY = BuildAlternatingIntervals(yMin, yMax, ribWidth, voidLength);

            // Snap the inner limits to the nearest grid line that keeps borders fully enclosing ribs/voids
            innerXMin = FindNextGridBoundary(tempX, innerXMin, true);   // snap inward
            innerXMax = FindNextGridBoundary(tempX, innerXMax, false);  // snap inward
            innerYMin = FindNextGridBoundary(tempY, innerYMin, true);
            innerYMax = FindNextGridBoundary(tempY, innerYMax, false);

            // 2️⃣ Build grid inside snapped borders
            var xIntervals = BuildAlternatingIntervals(innerXMin, innerXMax, ribWidth, voidLength);
            var yIntervals = BuildAlternatingIntervals(innerYMin, innerYMax, ribWidth, voidLength);

            // Borders (expanded to snapped limits)
            var bordersOut = new List<Rectangle3d>
        {
            new Rectangle3d(basePlane, new Interval(xMin, innerXMin), new Interval(yMin, yMax)), // Left
            new Rectangle3d(basePlane, new Interval(innerXMax, xMax), new Interval(yMin, yMax)), // Right
            new Rectangle3d(basePlane, new Interval(innerXMin, innerXMax), new Interval(yMin, innerYMin)), // Bottom
            new Rectangle3d(basePlane, new Interval(innerXMin, innerXMax), new Interval(innerYMax, yMax))  // Top
        };

            // 3️⃣ Build ribs + voids
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

            // 4️⃣ Pillar solids (same as before)
            var solidsOut = new List<Rectangle3d>();
            foreach (var pl in planes)
            {
                basePlane.RemapToPlaneSpace(pl.Origin, out Point3d pt);
                double cx = pt.X;
                double cy = pt.Y;

                double r0 = cx - offset;
                double r1 = cx + offset;
                double s0 = cy - offset;
                double s1 = cy + offset;

                if (r1 < xMin || r0 > xMax || s1 < yMin || s0 > yMax)
                    continue;

                double newX0 = r0, newX1 = r1, newY0 = s0, newY1 = s1;

                foreach (var xi in xIntervals)
                    if (RangesOverlap(r0, r1, xi.Start, xi.End))
                    { newX0 = Math.Min(newX0, xi.Start); newX1 = Math.Max(newX1, xi.End); }

                foreach (var yi in yIntervals)
                    if (RangesOverlap(s0, s1, yi.Start, yi.End))
                    { newY0 = Math.Min(newY0, yi.Start); newY1 = Math.Max(newY1, yi.End); }

                newX0 = Math.Max(newX0, xMin);
                newX1 = Math.Min(newX1, xMax);
                newY0 = Math.Max(newY0, yMin);
                newY1 = Math.Min(newY1, yMax);

                if (newX1 > newX0 && newY1 > newY0)
                    solidsOut.Add(new Rectangle3d(basePlane, new Interval(newX0, newX1), new Interval(newY0, newY1)));
            }

            // 5️⃣ Filter voids outside solids
            var filteredVoids = new List<Rectangle3d>();
            foreach (var v in voidsOut)
            {
                bool overlaps = false;
                foreach (var s in solidsOut)
                {
                    if (RectanglesOverlap(v, s)) { overlaps = true; break; }
                }
                if (!overlaps) filteredVoids.Add(v);
            }

            // ✅ Outputs
            DA.SetDataList(0, solidsOut);
            DA.SetDataList(1, bordersOut);
            DA.SetDataList(2, ribsOut);
            DA.SetDataList(3, filteredVoids);
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

        private double FindNextGridBoundary(List<Interval1D> intervals, double value, bool fromMin)
        {
            if (intervals.Count == 0) return value;
            if (fromMin)
            {
                foreach (var i in intervals)
                    if (i.End > value)
                        return i.End; // snap inward (next boundary inside)
            }
            else
            {
                for (int j = intervals.Count - 1; j >= 0; j--)
                    if (intervals[j].Start < value)
                        return intervals[j].Start;
            }
            return value;
        }

        private bool RangesOverlap(double a0, double a1, double b0, double b1)
        {
            return (a1 > b0) && (b1 > a0);
        }

        private bool RectanglesOverlap(Rectangle3d a, Rectangle3d b)
        {
            Plane pl = a.Plane;
            pl.RemapToPlaneSpace(a.Corner(0), out Point3d a0);
            pl.RemapToPlaneSpace(a.Corner(2), out Point3d a1);
            pl.RemapToPlaneSpace(b.Corner(0), out Point3d b0);
            pl.RemapToPlaneSpace(b.Corner(2), out Point3d b1);

            double ax0 = Math.Min(a0.X, a1.X);
            double ax1 = Math.Max(a0.X, a1.X);
            double ay0 = Math.Min(a0.Y, a1.Y);
            double ay1 = Math.Max(a0.Y, a1.Y);
            double bx0 = Math.Min(b0.X, b1.X);
            double bx1 = Math.Max(b0.X, b1.X);
            double by0 = Math.Min(b0.Y, b1.Y);
            double by1 = Math.Max(b0.Y, b1.Y);

            return (ax1 > bx0 && bx1 > ax0 && ay1 > by0 && by1 > ay0);
        }

        private class Interval1D
        {
            public double Start, End;
            public bool IsRib;
            public Interval1D(double s, double e, bool r) { Start = s; End = e; IsRib = r; }
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