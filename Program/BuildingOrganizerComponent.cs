using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Housefly.Program
{
    public class BuildingOrganizerComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BuildingOrganizerComponent class.
        /// </summary>
        public BuildingOrganizerComponent()
          : base("BuildingOrganizerComponent", "BuildingOrganizer",
              "Crosses base curves with floors, heights and interior-exterior condition to create base inputs for a building.",
              "Housefly", "Program")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Base polylines", "P", "Closed curves representing building plan parts.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Axis", "A", "Curve serving as building axis or reference line.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Floor Heights", "H", "Tree of lists with floor heights (possitive or negative).", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Interior Flags", "I", "Tree matching 'Floor Heights' indicating interior (true) or exterior (false) floors.", GH_ParamAccess.tree);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPlaneParameter("Floor planes", "FP", "Floor planes for each base curve", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Polylines on heights", "PH", "Polylines on each height", GH_ParamAccess.tree);
            pManager.AddLineParameter("Intersected facades", "PF", "Facades intersected by axis", GH_ParamAccess.tree);
            pManager.AddLineParameter("Lateral facades A", "FA", "Facades non intersected by axis and on its side A", GH_ParamAccess.tree);
            pManager.AddLineParameter("Lateral facades B", "FB", "Facades non intersected by axis and on its side B", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            List<Curve> baseCurves = new List<Curve>();
            Curve axis = null;
            GH_Structure<GH_Number> heightTree = null;
            GH_Structure<GH_Boolean> interiorTree = null;

            if(!DA.GetDataList("Base polylines", baseCurves)) return;
            if(!DA.GetData("Axis", ref axis)) return;
            if(!DA.GetDataTree("Floor Heights", out heightTree)) return;
            if(!DA.GetDataTree("Interior Flags", out interiorTree)) return;

            List<Polyline> basePolylines = new List<Polyline>();

            // VALIDATION

            bool isValid = true;

            if (heightTree.PathCount != interiorTree.PathCount)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Height and interior trees have different branch counts: {heightTree.PathCount} vs {interiorTree.PathCount}.");
                isValid = false;
            }

            if (baseCurves.Count != heightTree.PathCount)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Number of base curves ({baseCurves.Count}) does not match number of branches ({heightTree.PathCount}). Each footprint should correspond to one branch of floor data.");
                isValid = false;
            }

            int branchCount = Math.Min(heightTree.PathCount, interiorTree.PathCount);
            for (int i = 0; i < branchCount; i++)
            {
                int hCount = heightTree.Branches[i].Count;
                int iCount = interiorTree.Branches[i].Count;

                if (hCount != iCount)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Branch {heightTree.Paths[i]} mismatch: {hCount} heights vs {iCount} interior flags.");
                    isValid = false;
                }
            }

            for (int i = 0; i < baseCurves.Count; i++)
            {
                Curve currentCurve = baseCurves[i];
                Polyline currentPolyline = null;
                if (!currentCurve.IsPolyline())
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Base curve of index {i} is not a polyline.");
                    isValid = false;
                    break;
                }
                if (!currentCurve.TryGetPolyline(out currentPolyline))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Could not take polyline from base curve of index {i}.");
                    isValid = false;
                    break;
                }

                basePolylines.Add(currentPolyline);
            }

            if (!isValid) return;
            


            // LOGIC
            // create planes for each base curve
            GH_Structure<GH_Plane> floorsTree = new GH_Structure<GH_Plane>();
            GH_Structure<GH_Curve> curvesTree = new GH_Structure<GH_Curve>();
            GH_Structure<GH_Line> intersectedFacadesTree = new GH_Structure<GH_Line>();
            GH_Structure<GH_Line> nonIntersectedFacadesTreeA = new GH_Structure<GH_Line>();
            GH_Structure<GH_Line> nonIntersectedFacadesTreeB = new GH_Structure<GH_Line>();

            for (int i = 0; i < basePolylines.Count; i++)
            {
                GH_Path path = heightTree.Paths[i];
                Polyline polyline = basePolylines[i];

                if (polyline.Count < 3)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Polyline for base curve on index {i} has less than 3 segments.");
                    return;
                }
                if (!polyline.IsClosed)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Base curve on index {i} is not closed.");
                    return;
                }

                Point3d centerPoint = polyline.CenterPoint();
                Plane firstPlane = Plane.WorldXY;
                firstPlane.Origin = centerPoint;
                floorsTree.Append(new GH_Plane(firstPlane), path);

                // classify polyline segments to determine facades
                List<Line> intersectedSegments = new List<Line>();
                List<Line> nonIntersectedSegmentsA = new List<Line>();
                List<Line> nonIntersectedSegmentsB = new List<Line>();
                if (!ClassifyPolylineSegmentsByIntersection(axis, polyline, out intersectedSegments, out nonIntersectedSegmentsA, out nonIntersectedSegmentsB))
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Base curve on index {i} could not be classified.");
                }

                // move planes to heights
                List<GH_Number> heights = (List<GH_Number>) heightTree.get_Branch(path);
                List<GH_Boolean> interiors = (List<GH_Boolean>) interiorTree.get_Branch(path);

                double accumulatedHeight = 0;

                for (int j = 0; j < heights.Count; j++)
                {
                    
                    //GH_Path heightsPath = new GH_Path(new int[] { i, j });
                    GH_Path heightsPath = path.AppendElement(j);

                    // "acumulate" heights to move planes and base curves 
                    accumulatedHeight += heights[j].Value;
                    Vector3d moveDown = Vector3d.ZAxis * accumulatedHeight;
                    Transform translation = Transform.Translation(moveDown);
                    
                    Plane floorPlane = firstPlane.Clone();
                    floorPlane.Origin = centerPoint + moveDown;
                    floorsTree.Append(new GH_Plane(floorPlane), heightsPath);
                    
                    Polyline polylineOnFloor = polyline.Duplicate();
                    polylineOnFloor.Transform(translation);
                    curvesTree.Append(new GH_Curve(polylineOnFloor.ToPolylineCurve()), heightsPath);

                    List<GH_Line> lateralOnFloor = new List<GH_Line>();
                    List<GH_Line> longitudinalOnFloorA = new List<GH_Line>();
                    List<GH_Line> longitudinalOnFloorB = new List<GH_Line>();
                    for (int k = 0; k < intersectedSegments.Count; k++)
                    {
                        Line line = intersectedSegments[k];
                        line.Transform(translation);
                        lateralOnFloor.Add(new GH_Line(line));
                    }

                    for (int k = 0; k < nonIntersectedSegmentsA.Count; k++)
                    {
                        Line line = nonIntersectedSegmentsA[k];
                        line.Transform(translation);
                        longitudinalOnFloorA.Add(new GH_Line(line));
                    }
                    for (int k = 0; k < nonIntersectedSegmentsB.Count; k++)
                    {
                        Line line = nonIntersectedSegmentsB[k];
                        line.Transform(translation);
                        longitudinalOnFloorB.Add(new GH_Line(line));
                    }

                    intersectedFacadesTree.AppendRange(lateralOnFloor, heightsPath);
                    nonIntersectedFacadesTreeA.AppendRange(longitudinalOnFloorA, heightsPath);
                    nonIntersectedFacadesTreeB.AppendRange(longitudinalOnFloorB, heightsPath);

                    bool isFloorInterior = interiors[j].Value;
                    if (!isFloorInterior) continue;

                    

                    // TODO: check adjacent cells and determine kind of partition
                    
                }
            }


            DA.SetDataTree(0, floorsTree);
            DA.SetDataTree(1, curvesTree);
            DA.SetDataTree(2, intersectedFacadesTree);
            DA.SetDataTree(3, nonIntersectedFacadesTreeA);
            DA.SetDataTree(4, nonIntersectedFacadesTreeB);
        }

        private bool ClassifyPolylineSegmentsByIntersection(
            Curve curve,
            Polyline polyline,
            out List<Line> intersectedLines,
            out List<Line> nonIntersectedLinesA,
            out List<Line> nonIntersectedLinesB)
        {
            intersectedLines = new List<Line>();
            nonIntersectedLinesA = new List<Line>();
            nonIntersectedLinesB = new List<Line>();
        
            if (curve == null || polyline == null || polyline.Count < 2)
                return false;
        
            double tol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            double angTol = RhinoDoc.ActiveDoc.ModelAngleToleranceRadians;
        
            // validate planarity of A
            if (!curve.IsPlanar())
                return false;
        
            Plane planeA;
            if (!curve.TryGetPlane(out planeA))
                return false;
        
            // validate that polyline B lies in the same plane
            for (int i = 0; i < polyline.Count; i++)
            {
                if (Math.Abs(planeA.DistanceTo(polyline[i])) > tol)
                    return false;
            }
        
            int count = polyline.Count;
            bool closed = polyline.IsClosed;
            int segmentCount = closed ? count : count - 1;

            bool changeSide = false;
        
            // process each polyline segment
            for (int i = 0; i < segmentCount; i++)
            {
                Point3d p0 = polyline[i];
                Point3d p1 = polyline[(i + 1) % count];
        
                Line line = new Line(p0, p1);
                LineCurve segment = new LineCurve(line);
        
                // intersection test with Curve A
                var x = Rhino.Geometry.Intersect.Intersection.CurveCurve(
                    curve, segment, tol, angTol);
        
                bool hit = false;
                for (int j = 0; j < x.Count; j++)
                {
                    if (x[j].IsPoint)
                    {
                        hit = true;
                        break;
                    }
                }
        
                if (hit)
                {
                    intersectedLines.Add(line);
                    changeSide = !changeSide;
                }
                else if (changeSide)
                {
                    nonIntersectedLinesA.Add(line);
                }
                else
                {
                    nonIntersectedLinesB.Add(line);
                }
            }
        
            return true;
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
            get { return new Guid("CB474233-B4B4-40DA-8644-514BB22D8826"); }
        }
    }
}
