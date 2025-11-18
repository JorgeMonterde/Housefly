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
            pManager.AddBooleanParameter("Upwards", "U", "Either the floors are upwards or downwards.", GH_ParamAccess.item, true);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Bottom facade lines A", "LA", "Resulting bottom lines of facade portion on A", GH_ParamAccess.tree);
            pManager.AddLineParameter("Bottom facade lines B", "LB", "Resulting bottom lines of facade portion on B", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Facade heights A", "FHA", "Resulting facade portion heights on A", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Facade heights B", "FHB", "Resulting facade portion heights on B", GH_ParamAccess.tree);
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
            bool upwards = true;

            if(!DA.GetData("Facade line", ref baseFacadeLine)) return;
            if(!DA.GetDataTree("Floor heights A", out heightTreeA)) return;
            if(!DA.GetDataTree("Floor heights B", out heightTreeB)) return;
            if(!DA.GetDataTree("Interior flags A", out interiorTreeA)) return;
            if(!DA.GetDataTree("Interior flags B", out interiorTreeB)) return;
            DA.GetData("Upwards", ref upwards);

            // VALIDATION

            bool isValid = true;

            if(!baseFacadeLine.IsValid)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Base facade line is not valid.");
                isValid = false;
            }
            if(baseFacadeLine.Direction.IsParallelTo(Vector3d.ZAxis) != 0)
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

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"test 1.A: {accumulatedHeightTreeA.PathCount} - {accumulatedHeightTreeB.PathCount}");



            // 2. mix and sort all accumulated heights

            List<Floor> allFloors = new List<Floor>();

            // gather A data
            foreach (var p in accumulatedHeightTreeA.Paths)
            {
                var heights = heightTreeA.get_Branch(p);
                var accumulatedHeights = accumulatedHeightTreeA.get_Branch(p);
                var interiors = interiorTreeA.get_Branch(p);

                for (int i = 0; i < accumulatedHeights.Count; i++)
                {
                    double h = (heights[i] as GH_Number).Value;
                    double ah = (accumulatedHeights[i] as GH_Number).Value;
                    bool isInterior = (interiors[i] as GH_Boolean).Value;
                    allFloors.Add(new Floor(h, ah, p, 0, isInterior)); // hardcoded index as new path representing Tree A
                }
            }

            // gather B data
            foreach (var p in accumulatedHeightTreeB.Paths)
            {
                var heights = heightTreeB.get_Branch(p);
                var accumulatedHeights = accumulatedHeightTreeB.get_Branch(p);
                var interiors = interiorTreeB.get_Branch(p);

                for (int i = 0; i < accumulatedHeights.Count; i++)
                {
                    double h = (heights[i] as GH_Number).Value;
                    double ah = (accumulatedHeights[i] as GH_Number).Value;
                    bool isInterior = (interiors[i] as GH_Boolean).Value;
                    allFloors.Add(new Floor(h, ah, p, 1, isInterior)); // hardcoded index as new path representing Tree B
                }
            }

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"test 2: {allFloors.Count}");


            // sort by absolute aacumulated height value
            // NOTE: if there are possitive and negative values it will generate unexpected results
            allFloors.Sort((a, b) => Math.Abs(a.AccumulatedHeight).CompareTo(Math.Abs(b.AccumulatedHeight)));
            



            // 4. iterate resulting sorted height list: 
            // on each height (either on A or B), find the height on the other tree (either A or B) that is the biggest on its tree but smaller than the iterated value. 

            Vector3d perpDir = Vector3d.CrossProduct(baseFacadeLine.Direction, Vector3d.ZAxis);
            perpDir.Unitize();
            Vector3d opositePerpDir = -perpDir;

            GH_Structure<GH_Line> facadePortionBottomLinesA = new GH_Structure<GH_Line>();
            GH_Structure<GH_Line> facadePortionBottomLinesB = new GH_Structure<GH_Line>();
            GH_Structure<GH_Number> facadePortionHeightsA = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> facadePortionHeightsB = new GH_Structure<GH_Number>();

            for (int i = 0; i < allFloors.Count; i++)
            {
                // TODO: Fix loop for last element


                Floor currentFloor = allFloors[i];
                bool neighbourFound = false;

                for (int j = 0; j < allFloors.Count; j++)
                {
                    Floor candidate = allFloors[j];

                    // find equal or next higher floor from the other tree
                    // TODO: ensure path is correct to be compared
                    // do i just simply compare path or should i just extract a certain branch index ?
                    // ({0;1} == {0;2}) would result true or false ?  
                    // way of comparing paths ? GH_Path.Inequality(candidate.Path, currentFloor.Path)

                    if (Math.Abs(candidate.AccumulatedHeight) >= Math.Abs(currentFloor.AccumulatedHeight) && candidate.Path != currentFloor.Path)
                    {

                        // 5. compare both interior conditions (of A and B) and assign the translator vector according to interior condition:
                        // (int && int) || (ext && ext) = null 
                        // int && ext = -> 
                        // ext && int = <- 


                        if (!currentFloor.IsInterior && candidate.IsInterior) // return vector 1
                        {
                            // TODO: check correct orientation; what about the orientation of each line?
                            // they will change the orientation, so we need a way to get absolute orientation
                            // POSSIBLE SOLUTION: flip all lines with a guide
                            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "different interior and exterior 1");
                            currentFloor.SetbackVector = perpDir;
                        }
                        else if (currentFloor.IsInterior && !candidate.IsInterior) // return vector 2
                        {
                            // TODO: check correct orientation; what about the orientation of each line?
                            // they will change the orientation, so we need a way to get absolute orientation
                            // POSSIBLE SOLUTION: flip all lines with a guide
                            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "different interior and exterior 2");
                            currentFloor.SetbackVector = opositePerpDir;
                        }
                        else // (currentFloor.IsInterior && candidate.IsInterior) && (!currentFloor.IsInterior && !candidate.IsInterior)
                        {
                            // both interior or both exterior: return to interior or exterior partitions (or not returned anything at all)
                            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "same interior and interior or exterior and exterior");
                            currentFloor.SetbackVector = Vector3d.Zero;
                        }

                        neighbourFound = true;
                        break;
                    }
                }

                if(!neighbourFound)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not get neighbour Floor for index {i}.");
                    currentFloor.SetbackVector = Vector3d.Zero; // provisional
                    continue;
                }
                
                
                // 7. create lines and vectors for facade setbacks

                double previousAccumulatedHeight = i == 0 ? 0 : allFloors[i - 1].AccumulatedHeight;

                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"test 5 : iteration {i} - {currentFloor.ToString()}.");


                Line bottomLine = baseFacadeLine;
                bottomLine.To = bottomLine.To + currentFloor.SetbackVector + (Vector3d.ZAxis * previousAccumulatedHeight);
                bottomLine.From = bottomLine.From + currentFloor.SetbackVector + (Vector3d.ZAxis * previousAccumulatedHeight);

                //Line topLine = baseFacadeLine;
                //topLine.To = topLine.To + currentFloor.Vector + (Vector3d.ZAxis * currentFloor.AccumulatedHeight);
                //topLine.From =topLine.From + currentFloor.Vector + (Vector3d.ZAxis * currentFloor.AccumulatedHeight);

                GH_Number facadePortionHeight = new GH_Number(currentFloor.AccumulatedHeight - previousAccumulatedHeight);
                GH_Path path = currentFloor.Path;

                if (currentFloor.Index == 0)
                {
                    facadePortionBottomLinesA.Append(new GH_Line(bottomLine), path);
                    facadePortionHeightsA.Append(facadePortionHeight, path);
                }
                else
                {
                    facadePortionBottomLinesB.Append(new GH_Line(bottomLine), path);
                    facadePortionHeightsB.Append(facadePortionHeight, path);
                }
            }


            DA.SetDataTree(0, facadePortionBottomLinesA);
            DA.SetDataTree(1, facadePortionBottomLinesB);
            DA.SetDataTree(2, facadePortionHeightsA);
            DA.SetDataTree(3, facadePortionHeightsB);
        }

        public struct Floor
        {
            public double Height;
            public double AccumulatedHeight;
            public GH_Path Path;
            public int Index;
            public bool IsInterior;
            public Vector3d SetbackVector;

            public Floor(double height, double accumulatedHeight, GH_Path path, int index, bool isInterior, Vector3d setbackVector = default)
            {
                this.Height = height;
                this.AccumulatedHeight = accumulatedHeight;
                this.Path = path;
                this.Index = index;
                this.IsInterior = isInterior;
                this.SetbackVector = setbackVector;
            }

            override public string ToString()
            {
                return $"Height: {this.Height} | AccumulatedHeight: {this.AccumulatedHeight} | Path: {this.Path} | Index: {this.Index} | SetbackVector: {this.SetbackVector.ToString()}";
            }
        }


        private GH_Structure<GH_Number> CreateAccumulatedValueTree(GH_Structure<GH_Number> tree)
        {
            GH_Structure<GH_Number> accumulatedTree = new GH_Structure<GH_Number>();
            double accumulatedHeight = 0;

            for (int i = 0; i < tree.Paths.Count; i++)
            {
                GH_Path path = tree.Paths[i];
                List<GH_Number> numbers = tree.get_Branch(path) as List<GH_Number>;
                
                if(numbers.Count != 1)
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"List on branch with path {path} has {numbers.Count} elements and it must have one.");

                accumulatedHeight += numbers[0].Value;
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
