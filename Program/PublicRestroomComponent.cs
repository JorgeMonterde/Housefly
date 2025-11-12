using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Housefly.Program
{
    public class PublicRestroomComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PublicRestroomComponent class.
        /// </summary>
        public PublicRestroomComponent()
          : base("PublicRestroomComponent", "PublicRestroom",
              "Creates base plan lines for a public restroom",
              "Housefly", "Program")
        {
        }



        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Width", "W", "Width of the rectangle", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("Depth", "D", "Depth of the rectangle", GH_ParamAccess.item, 6.0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Entrance", "E", "Entrance door", GH_ParamAccess.item);
            pManager.AddLineParameter("Exterior walls", "EW", "exterior walls", GH_ParamAccess.list);
            pManager.AddLineParameter("Interior partitions", "IP", "interior partitions", GH_ParamAccess.list);
            pManager.AddCurveParameter("Cabin doors", "CD", "Door curves for cabin doors", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Toilet planes", "TP", "Toilet base planes", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // IMPROVEMENTS:
            // - Add sink drawings

            double sinkWidth = 0.60,
                centralSpaceWidth = 1.50,
                entranceDepth = 2.0,
                entranceWidth = 1.5,
                accessibleCabinDepth = 2.0,
                accessibleDoorSize = 1.20,
                toiletCabinDoor = 0.70,
                toiletCabinDoorFrame = 0.10,
                minimumCabinWidth = toiletCabinDoor + toiletCabinDoorFrame * 2,
                minimumCabinDepth = 1.50,
                toiletDistanceFromWall = 0.10,
                minWidth = sinkWidth + centralSpaceWidth + minimumCabinDepth,
                minDepth = entranceWidth + minimumCabinWidth + accessibleCabinDepth;

            double width = 0;
            double depth = 0;

            DA.GetData(0, ref width);
            DA.GetData(1, ref depth);

            if (width < minWidth)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Width is too low; minimum width will be used instead ({width})");
                width = minWidth;
            }
            if (depth < minDepth)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Depth is too low; minimum depth will be used instead ({depth})");
                depth = minDepth;
            }

            Point3d A = new Point3d(0, 0, 0);
            Point3d B = new Point3d(width, 0, 0);
            Point3d C = new Point3d(width, depth, 0);
            Point3d D = new Point3d(0, depth, 0);

            Line AB_line = new Line(A, B);
            Line BC_line = new Line(B, C);
            Line CD_line = new Line(C, D);
            Line DA_line = new Line(D, A);

            Vector3d dirBA = A - B;
            dirBA.Unitize();
            Point3d X = B + dirBA * entranceDepth;

            Vector3d perpAB = BC_line.Direction;
            perpAB.Unitize();
            Point3d E = X + perpAB * entranceWidth;

            Line entrance = new Line(E, X);
            PolylineCurve entrancePolyline = this.CreateDoorPolyline(entrance, accessibleDoorSize, true, true);

            // Y = closest point on BC to E
            double tBC = BC_line.ClosestParameter(E);
            Point3d Y = BC_line.PointAt(tBC);

            // main walls
            Line wallAB = AB_line;
            Line wallYC = new Line(Y, C);
            Line wallCD_ = CD_line;
            Line wallDA_ = DA_line;
            Line wallEY = new Line(E, Y);

            // accessible cabin
            Vector3d dirCB = B - C;
            dirCB.Unitize();
            Vector3d accessibleCabinDirection = dirCB * accessibleCabinDepth;
            Point3d Cprime = C + accessibleCabinDirection;
            Point3d Dprime = D + accessibleCabinDirection;

            Vector3d dirDC = C - D;
            dirDC.Unitize();

            Point3d R = Dprime + dirDC * 0.80; // accessible toilet sink side
            Point3d S = R + dirDC * accessibleDoorSize;

            Line DprimeR = new Line(Dprime, R);
            Line accessibleDoorLine = new Line(R, S);
            Line SCprime = new Line(S, Cprime);

            // non-accessible cabins

            double available = Y.DistanceTo(Cprime);
            int cabinCount = (int)Math.Floor(available / minimumCabinWidth);

            if (cabinCount < 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not create toilet cabins");
                return;
            }

            double rest = available - cabinCount * minimumCabinWidth;
            double extra = rest / cabinCount;
            double finalWidth = minimumCabinWidth + extra;

            Vector3d dirYC = Cprime - Y;
            dirYC.Unitize();

            Vector3d dirCD = D - C;
            dirCD.Unitize();

            Point3d accessibleToiletPt = C + dirCB * (accessibleCabinDepth / 2) - dirDC * toiletDistanceFromWall;
            Plane accessibleToiletPlane = new Plane(accessibleToiletPt, dirYC, dirCD);
            List<Plane> toiletPlanes = new List<Plane>() { accessibleToiletPlane };

            Point3d normalToiletsBasePt = Y + dirYC * (finalWidth / 2) + dirCD * toiletDistanceFromWall;
            Plane normalToiletsBasePlane = new Plane(normalToiletsBasePt, dirYC, dirCD);

            List<Line> cabinWalls = new List<Line>();
            List<Curve> cabinDoors = new List<Curve>();

            // build toilet planes and cabin partitions
            for (int i = 0; i <= cabinCount; i++)
            {
                Vector3d traslationVector = dirYC * (finalWidth * i);

                if (i < cabinCount) // skip last 
                {
                    Plane toiletPlane = normalToiletsBasePlane.Clone();
                    toiletPlane.Origin = normalToiletsBasePlane.Origin + traslationVector;
                    toiletPlanes.Add(toiletPlane);
                }

                Point3d firstPartitionPt = Y + traslationVector;
                Line partition = new Line(firstPartitionPt, firstPartitionPt + dirCD * minimumCabinDepth);
                cabinWalls.Add(partition);
            }

            // cabin doors (between partitions)
            for (int i = 1; i < cabinWalls.Count; i++)
            {
                Point3d prevBottom = cabinWalls[i - 1].To;
                Point3d nextBottom = cabinWalls[i].To;
                Line doorLine = new Line(prevBottom, nextBottom);
                PolylineCurve door = this.CreateDoorPolyline(doorLine, toiletCabinDoor, false, false);
                cabinDoors.Add(door);
            }
            cabinDoors.Add(this.CreateDoorPolyline(accessibleDoorLine, accessibleDoorLine.Length, true, false));

            // remove first and last partitions
            cabinWalls.RemoveAt(0);
            cabinWalls.RemoveAt(cabinWalls.Count - 1);

            // add accessible cabin partitions
            cabinWalls.Add(DprimeR);
            cabinWalls.Add(SCprime);

            DA.SetData(0, entrancePolyline);
            DA.SetDataList(1, new List<Line>() { wallAB, wallYC, wallCD_, wallDA_, wallEY });
            DA.SetDataList(2, cabinWalls);
            DA.SetDataList(3, cabinDoors);
            DA.SetDataList(4, toiletPlanes);
        }

        private PolylineCurve CreateDoorPolyline(Line baseLine, double doorSize, bool changeSide, bool flipLines)
        {
            if (!baseLine.IsValid)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Line is not valid.");
                return null;
            }

            double lineLength = baseLine.Length;
            if (lineLength < doorSize)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Line length is smaller than door size.");
                return null;
            }

            if (flipLines) baseLine.Flip();

            Point3d start = baseLine.From;
            Point3d end = baseLine.To;

            Vector3d dir = end - start;
            dir.Unitize();

            Vector3d perp = Vector3d.CrossProduct(dir, Vector3d.ZAxis);
            perp.Unitize();
            if (changeSide) perp.Reverse();

            double frameLength = (lineLength - doorSize) / 2;

            Point3d p1 = start;
            Point3d p2 = start + dir * frameLength;
            Point3d p3 = p2 + perp * doorSize;
            Point3d p4 = end - dir * frameLength;
            Point3d p5 = end;

            List<Point3d> points = p1.DistanceTo(p2) < RhinoDoc.ActiveDoc.ModelAbsoluteTolerance
                ? new List<Point3d> { start, start + perp * lineLength, end }
                : new List<Point3d> { p1, p2, p3, p4, p5 };

            Polyline polyline = new Polyline(points);

            if (!polyline.IsValid)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Created polyline not valid.");
                return null;
            }

            return polyline.ToPolylineCurve();
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
            get { return new Guid("862A9A14-9AFE-457B-9CB7-79835103854C"); }
        }
    }
}
