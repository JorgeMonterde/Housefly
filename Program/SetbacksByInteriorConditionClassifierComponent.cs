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

            if (heightTreeA.PathCount != interiorTreeA.PathCount)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Height and interior A trees have different branch counts: {heightTreeA.PathCount} vs {interiorTreeA.PathCount}.");
                isValid = false;
            }
            if (heightTreeB.PathCount != interiorTreeB.PathCount)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Height and interior B trees have different branch counts: {heightTreeB.PathCount} vs {interiorTreeB.PathCount}.");
                isValid = false;
            }

            int branchCountA = Math.Min(heightTreeA.PathCount, interiorTreeA.PathCount);
            for (int i = 0; i < branchCountA; i++)
            {
                int hCountA = heightTreeA.Branches[i].Count;
                int iCountA = interiorTreeA.Branches[i].Count;

                if (hCountA != iCountA)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Branch {heightTreeA.Paths[i]} on A tree mismatch: {hCountA} heights vs {iCountA} interior flags.");
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
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Branch {heightTreeB.Paths[i]} on B tree mismatch: {hCountB} heights vs {iCountB} interior flags.");
                    isValid = false;
                }
            }

            if (!isValid) return;
            


            // LOGIC

            

            // 1. convert heights to "accumulated heights"

            GH_Structure<GH_Number> accumulatedHeightTreeA = this.CreateAccumulatedValueTree(heightTreeA);
            GH_Structure<GH_Number> accumulatedHeightTreeB = this.CreateAccumulatedValueTree(heightTreeB);

            // 2. mix and sort all accumulated heights

            List<(double value, GH_Path path)> allAccumulatedHeights = new List<(double value, GH_Path path)>(); // list of tuples

            // gather all numbers + paths from Tree A
            foreach (var kv in accumulatedHeightTreeA.Paths.SelectMany(p => accumulatedHeightTreeA.get_Branch(p).Select(n => (n, p))))
            {
                GH_Number num = kv.n;
                GH_Path path = kv.p;
                if (num != null)
                    allAccumulatedHeights.Add((num.Value, path));
            }

            // gather all numbers + paths from Tree B
            foreach (var kv in accumulatedHeightTreeB.Paths.SelectMany(p => accumulatedHeightTreeB.get_Branch(p).Select(n => (n, p))))
            {
                GH_Number num = kv.n;
                GH_Path path = kv.p;
                if (num != null)
                    allAccumulatedHeights.Add((num.Value, path));
            }

            // sort by the numeric value
            allAccumulatedHeights.Sort((a, b) => a.value.CompareTo(b.value));

            // unpack into parallel lists
            // List<double> sortedValues = allAccumulatedHeights.Select(tuple => tuple.value).ToList();
            // List<GH_Path> sortedPaths  = allAccumulatedHeights.Select(tuple => tuple.path).ToList();


            // 3. remove duplicated heights and keep all paths 

            List<(double value, List<GH_Path> paths)> uniqueAccumulatedHeights = new List<(double value, List<GH_Path> paths)>(); // list of tuples; tuples of numbers and lists of GH_Path
            
            for (int i = 1; i < allAccumulatedHeights.Count; i++)
            {
                (double value, GH_Path path) prevTuple = allAccumulatedHeights[i - 1];
                (double value, GH_Path path) currentTuple = allAccumulatedHeights[i];
                
                if(currentTuple.value == prevTuple.value && uniqueAccumulatedHeights.Count != 0)
                {
                    // add new path to previous unique tuple
                    uniqueAccumulatedHeights[i - 1].paths.Add(currentTuple.path);
                    continue;
                }

                (double value, List<GH_Path> paths) uniqueTuple = (currentTuple.value, new List<GH_Path>(){ currentTuple.path })
                uniqueAccumulatedHeights.Add(uniqueTuple);
            }

            // 4.iterate resulting sorted height list: on each height (either on A or B), find the height on the other tree (either A or B) that is the biggest on its tree but smaller than the iterated value. 

            for (int i = 1; i < allAccumulatedHeights.Count; i++)
            {
                (double value, GH_Path path) prevTuple = allAccumulatedHeights[i - 1];
                (double value, GH_Path path) currentTuple = allAccumulatedHeights[i];
                
                if(currentTuple.value == prevTuple.value && uniqueAccumulatedHeights.Count != 0)
                {
                    // add new path to previous unique tuple
                    uniqueAccumulatedHeights[i - 1].paths.Add(currentTuple.path);
                    continue;
                }

                (double value, List<GH_Path> paths) uniqueTuple = (currentTuple.value, new List<GH_Path>(){ currentTuple.path })
                uniqueAccumulatedHeights.Add(uniqueTuple);
            }



            // 5. compare both heights (of A and B) and assign the translator vector according to interior condition:
            // (int && int) || (ext && ext) = null 
            // int && ext = -> 
            // ext && int = <- 

            // 6. store vector on new tree



            DA.SetDataTree(0, lineTree);
            DA.SetDataTree(1, facadeHeightTree);
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
