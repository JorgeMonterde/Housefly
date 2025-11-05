using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

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

        private static readonly Random random = new Random();

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Base Curves", "C", "Closed curves representing building plan parts. Default: random rectangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Axis", "A", "Polyline serving as building axis or reference line. Default: line along X axis.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Floor Heights", "H", "Tree of lists with floor heights in meters. Default: random floors per building.", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Interior Flags", "I", "Tree matching 'Floor Heights' indicating interior (true) or exterior (false) floors. Default: random booleans.", GH_ParamAccess.tree);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output", "O", "Validated and processed data (placeholder for geometry output).", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Floor planes", "FP", "Floor planes for each base curve", GH_ParamAccess.tree);
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

            if(!DA.GetDataList("Base Curves", baseCurves)) return;
            if(!DA.GetData("Axis", ref axis)) return;
            if(!DA.GetDataTree("Floor Heights", out heightTree)) return;
            if(!DA.GetDataTree("Interior Flags", out interiorTree)) return;

            // VALIDATION

            bool isValid = true;

            if (heightTree.PathCount != interiorTree.PathCount)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Height and interior trees have different branch counts: {heightTree.PathCount} vs {interiorTree.PathCount}.");
                isValid = false;
            }

            if (baseCurves.Count != heightTree.PathCount)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Number of base curves ({baseCurves.Count}) does not match number of branches ({heightTree.PathCount}). Each footprint should correspond to one branch of floor data.");
                isValid = false;
            }

            int branchCount = Math.Min(heightTree.PathCount, interiorTree.PathCount);
            for (int i = 0; i < branchCount; i++)
            {
                int hCount = heightTree.Branches[i].Count;
                int iCount = interiorTree.Branches[i].Count;

                if (hCount != iCount)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Branch {heightTree.Paths[i]} mismatch: {hCount} heights vs {iCount} interior flags.");
                    isValid = false;
                }
            }

            if (!isValid)
            {
                DA.SetDataList(0, new List<object>());
                return;
            }


            // LOGIC
            // create planes for each base curve
            GH_Structure<GH_Plane> floorsTree = new GH_Structure<GH_Plane>();

            for (int i = 0; i < baseCurves.Count; i++)
            {
                GH_Path path = heightTree.Paths[i];

                Curve curve = baseCurves[i];
                Polyline polyline;

                if(!curve.TryGetPolyline(out polyline))
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Base curve on index {i} could not get polyline.");
                    return;
                }
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

                // move planes to heights
                List<double> heights = (List<double>) heightTree.get_Branch(path);
                List<bool> interiors = (List<bool>) interiorTree.get_Branch(path);

                for (int j = 0; j < heights.Count; j++)
                {
                    // "acumulate" heights to move planes and base curves 
                    Vector3d moveDown = Vector3d.ZAxis * heights[j];
                    Plane floorPlane = firstPlane.Clone();
                    floorPlane.Origin = centerPoint + moveDown;
                    floorsTree.Append(new GH_Plane(floorPlane), path);

                    bool isFloorInterior = interiors[j];
                    if (!isFloorInterior) continue;
                    // create brep for interior space



                    // TODO: check adjacent cells and determine kind of partition
                    
                }
            }






            // OUTPUTS

            List<object> output = new List<object>();
            output.AddRange(baseCurves);
            output.Add(axis);
            DA.SetDataList("Output", output);
            DA.SetDataList("Floor planes", floorsTree);
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