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

            GH_Structure<GH_Line> lineTree = new GH_Structure<GH_Line>();
            GH_Structure<GH_Number> facadeHeightTree = new GH_Structure<GH_Number>();

            // 1. mix heights of A and B and convert them to "accumulated heights"

            // 2. sort all accumulated heights

            // 3. remove duplicated heights (keep both paths?) 

            // 4. iterate resulting sorted height list

            // 5. on each height (either on A or B), find the height on the other tree (either A or B) that is the biggest opn its tree but smaller than the iterated value. 

            // 6. compare both heights (of A and B) and assign the translator vector according to interior condition:
            // (int && int) || (ext && ext) = null 
            // int && ext = -> 
            // ext && int = <- 

            // 7. store vector on new tree

            // for (int i = 0; i < basePolylines.Count; i++)
            // {
            //     GH_Path path = heightTree.Paths[i];
            //     Polyline polyline = basePolylines[i];

            // }

            DA.SetDataTree(0, lineTree);
            DA.SetDataTree(1, facadeHeightTree);
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
