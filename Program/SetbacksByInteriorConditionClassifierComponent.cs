﻿using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Housefly.Program
{
    public class SetbacksByInteriorConditionClassifierComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SetbacksByInteriorConditionClassifierComponent class.
        /// </summary>
        public SetbacksByInteriorConditionClassifierComponent()
          : base("SetbacksByInteriorConditionClassifierComponent", "SetbacksByInteriorConditionClassifier",
              "Moves a base line representing a facade to fit the interior or exterior conditions of the building spaces and according to its heights.",
              "Housefly", "Program")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("Facade line", "FL", "Base line representing a facade on plan.", GH_ParamAccess.item);

            pManager.AddNumberParameter("Floor heights A", "HA", "Floor heights for spaces on A.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Floor heights B", "HB", "Floor heights for spaces on B.", GH_ParamAccess.tree);

            pManager.AddBooleanParameter("Interior flags A", "IA", "Tree matching 'Floor heights A' indicating interior (true) or exterior (false) condition for each space.", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Interior flags B", "IB", "Tree matching 'Floor heights B' indicating interior (true) or exterior (false) condition for each space.", GH_ParamAccess.tree);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Moved facade lines", "FL", "Resulting facade lines", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Facade heights", "FH", "Resulting facade segment heights", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Line baseFacadeLine = Line.Unset;
            GH_Structure<GH_Number> heightTreeA = null;
            GH_Structure<GH_Number> heightTreeB = null;
            GH_Structure<GH_Boolean> interiorTreeA = null;
            GH_Structure<GH_Boolean> interiorTreeB = null;

            if(!DA.GetData("Facade line", ref baseFacadeLine)) return;
            if(!DA.GetDataTree("Floor heights A", out heightTreeA)) return;
            if(!DA.GetDataTree("Floor heights B", out heightTreeB)) return;
            if(!DA.GetDataTree("Interior flags A", out interiorTreeA)) return;
            if(!DA.GetDataTree("Interior flags B", out interiorTreeB)) return;

            // VALIDATION

            bool isValid = true;

            if(!baseFacadeLine.IsValid)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Base facade line is not valid.");
                isValid = false;
            }
            if(baseFacadeLine.Direction.IsParallelTo(Vector3d.ZAxis))
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Base facade line cannot be parallel to Z axis.");
                isValid = false;
            }

            if (heightTreeA.PathCount != interiorTreeA.PathCount)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Height and interior A trees have different branch counts: {heightTreeA.PathCount} vs {interiorTreeA.PathCount}.");
                isValid = false;
            }
            if (heightTreeB.PathCount != interiorTreeB.PathCount)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Height and interior B trees have different branch counts: {heightTreeB.PathCount} vs {interiorTreeB.PathCount}.");
                isValid = false;
            }

            int branchCountA = Math.Min(heightTreeA.PathCount, interiorTreeA.PathCount);
            for (int i = 0; i < branchCountA; i++)
            {
                int hCountA = heightTreeA.Branches[i].Count;
                int iCountA = interiorTreeA.Branches[i].Count;

                if (hCountA != iCountA)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Branch {heightTreeA.Paths[i]} on A tree mismatch: {hCountA} heights vs {iCountA} interior flags.");
                    isValid = false;
                }
            }

            int branchCountB = Math.Min(heightTreeB.PathCount, interiorTreeB.PathCount);
            for (int i = 0; i < branchCountB; i++)
            {
                int hCountB = heightTreeB.Branches[i].Count;
                int iCountB = interiorTreeB.Branches[i].Count;

                if (hCountB != iCountB)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Branch {heightTreeB.Paths[i]} on B tree mismatch: {hCountB} heights vs {iCountB} interior flags.");
                    isValid = false;
                }
            }

            if (!isValid) return;
            


            // LOGIC

            

            // 1. convert heights to "accumulated heights"
            // NOTE: we are assuming that branches are ordered
            // TODO: check for branching order ?
            GH_Structure<GH_Number> accumulatedHeightTreeA = this.CreateAccumulatedValueTree(heightTreeA);
            GH_Structure<GH_Number> accumulatedHeightTreeB = this.CreateAccumulatedValueTree(heightTreeB);


            // 2. mix and sort all accumulated heights

            List<Floor> allFloors = new List<Floor>();

            // gather A data
            foreach (var p in accumulatedHeightTreeA.Paths)
            {
                var heights = accumulatedHeightTreeA.get_Branch(p);
                var interiors = interiorTreeA.get_Branch(p);

                for (int i = 0; i < heights.Count; i++)
                {
                    double h = heights[i].Value;
                    bool isInterior = interiors[i].Value;
                    allFloors.Add(new Floor(h, p, isInterior));
                }
            }

            // gather B data
            foreach (var p in accumulatedHeightTreeB.Paths)
            {
                var heights = accumulatedHeightTreeB.get_Branch(p);
                var interiors = interiorTreeB.get_Branch(p);

                for (int i = 0; i < heights.Count; i++)
                {
                    double h = heights[i].Value;
                    bool isInterior = interiors[i].Value;
                    allFloors.Add(new Floor(h, p, isInterior));
                }
            }

            // sort by the numeric value
            allFloors.Sort((a, b) => a.Value.CompareTo(b.Value));


            // 4. iterate resulting sorted height list: 
            // on each height (either on A or B), find the height on the other tree (either A or B) that is the biggest on its tree but smaller than the iterated value. 

            Vector3d perpDir = Vector3d.CrossProduct(baseFacadeLine.Direction, Vector3d.ZAxis);
            Vector3d opositePerpDir = -perpDir;

            for (int i = 0; i < allFloors.Count; i++)
            {
                Floor currentFloor = allFloors[i];
                Floor neighbourFloor? = null; 

                for (int j = i + 1; j < allFloors.Count; j++)
                {
                    Floor candidate = allFloors[j];

                    // find equal or next higher floor from the other tree
                    // TODO: ensure path is correct to be compared
                    if(candidate.Value >= currentFloor.Value && candidate.Path != currentFloor.Path)
                    {
                        neighbourFloor = candidate;
                        break;
                    }
                }

                if(!neighbourFloor)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not get neighbour tuple for index {i}.");
                    continue;
                }

                // 5. compare both interior conditions (of A and B) and assign the translator vector according to interior condition:
                // (int && int) || (ext && ext) = null 
                // int && ext = -> 
                // ext && int = <- 

                Vector3d offsetVector = Vector3d.Unset;

                if (currentFloor.IsInterior && neighbourFloor.IsInterior)
                {
                    // both interior: return to interior partitions (or not returned anything at all)
                    offsetVector = null;
                }
                else if (!currentFloor.IsInterior && !neighbourFloor.IsInterior)
                {
                    // both exterior: return to exterior partitions (or not returned anything at all)
                    offsetVector = null;
                }
                else if (!currentFloor.IsInterior && neighbourFloor.IsInterior)
                {
                    // interior - exterior: return vector 1
                    // TODO: check correct orientation; what about the orientation of each line?
                    // they will change the orientation, so we need a way to get absolute orientation
                    // POSSIBLE SOLUTION: flip all lines with a guide
                    offsetVector = perpDir;
                }
                else // (currentFloor.IsInterior && neighbourFloor.IsInterior)
                {
                    // exterior - interior: return vector 2
                    // TODO: check correct orientation; what about the orientation of each line?
                    // they will change the orientation, so we need a way to get absolute orientation
                    // POSSIBLE SOLUTION: flip all lines with a guide
                    offsetVector = opositePerpDir;
                }

                currentFloor.Vector = offsetVector;

            }




            // 6. create new tree with vectors and heights ? 



            DA.SetDataTree(0, lineTree);
            DA.SetDataTree(1, facadeHeightTree);
        }

        public struct Floor
        {
            public double Height;
            public GH_Path Path;
            public bool IsInterior;
            public Vector3d Vector;

            public Floor(double height, GH_Path path, bool isInterior, Vector3d vector = Vector3d.Unset)
            {
                Value = value;
                Path = path;
                IsInterior = isInterior;
                Vector = vector;
            }
        }


        private GH_Structure<GH_Number> CreateAccumulatedValueTree(GH_Structure<GH_Number> tree)
        {
            GH_Structure<GH_Number> accumulatedTree = new GH_Structure<GH_Number>();
            double accumulatedHeight = 0;

            for (int i = 0; i < tree.Branches.Count; i++)
            {
                GH_Path path = tree.Paths[i];
                List<double> numbers = tree.Branch[path]; // need to be GH_Number ?
                
                if(numbers.Count != 1)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"List on branch with path {path} has {numbers.Count} elements and it must have one.");
                } 

                accumulatedHeight += numbers[0];
                accumulatedTree.Append(new GH_Number(accumulatedHeight), path);
            }
            
            return accumulatedTree;
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
            get { return new Guid("f82ea080-0ba2-4ef8-b874-4884b4a34744"); }
        }
    }
}
