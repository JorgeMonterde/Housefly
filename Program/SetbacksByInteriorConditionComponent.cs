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
    public class SetbacksByInteriorConditionComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SetbacksByInteriorConditionComponent class.
        /// </summary>
        public SetbacksByInteriorConditionComponent()
          : base("SetbacksByInteriorConditionComponent", "SetbacksByInteriorCondition",
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
            pManager.AddNumberParameter("Floor heights A", "HA", "Floor heights for spaces on A.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Floor heights B", "HB", "Floor heights for spaces on B.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Interior flags A", "IA", "List matching 'Floor heights A' indicating interior (true) or exterior (false) condition for each space.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Interior flags B", "IB", "List matching 'Floor heights B' indicating interior (true) or exterior (false) condition for each space.", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Bottom facade lines A", "LA", "Resulting bottom lines of facade portion on A", GH_ParamAccess.list);
            pManager.AddLineParameter("Bottom facade lines B", "LB", "Resulting bottom lines of facade portion on B", GH_ParamAccess.list);
            pManager.AddNumberParameter("Facade heights A", "FHA", "Resulting facade portion heights on A", GH_ParamAccess.list);
            pManager.AddNumberParameter("Facade heights B", "FHB", "Resulting facade portion heights on B", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Line baseFacadeLine = Line.Unset;
            List<double> heightsA = new List<double> ();
            List<double> heightsB = new List<double> ();
            List<bool> interiorsA = new List<bool> ();
            List<bool> interiorsB = new List<bool> ();

            if(!DA.GetData("Facade line", ref baseFacadeLine)) return;
            if(!DA.GetDataList("Floor heights A", heightsA)) return;
            if(!DA.GetDataList("Floor heights B", heightsB)) return;
            if(!DA.GetDataList("Interior flags A", interiorsA)) return;
            if(!DA.GetDataList("Interior flags B", interiorsB)) return;

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

            if (heightsA.Count!= interiorsA.Count)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Height and interior A lists have different lengths: {heightsA.Count} vs {interiorsA.Count}.");
                isValid = false;
            }
            if (heightsB.Count!= interiorsB.Count)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Height and interior B lists have different lengths: {heightsB.Count} vs {interiorsB.Count}.");
                isValid = false;
            }

            if (!isValid) return;
            


            // LOGIC

            

            // 1. convert heights to "cumulated heights"
            // NOTE: we are assuming lists are ordered
            // TODO: check for list order ?
            List<double> cumulatedHeightsA = this.CreateCumulativeList(heightsA);
            List<double> cumulatedHeightsB = this.CreateCumulativeList(heightsB);

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"test 1.A: {cumulatedHeightsA.Count} - {cumulatedHeightsB.Count}");



            // 2. mix and sort all cumulated heights

            List<Floor> allFloors = new List<Floor>();

            // gather A data
            for (int i = 0; i < heightsA.Count; i++)
            {
                allFloors.Add(new Floor(heightsA[i], cumulatedHeightsA[i], "A", interiorsA[i]));
            }

            // gather B data
            for (int i = 0; i < heightsB.Count; i++)
            {
                allFloors.Add(new Floor(heightsB[i], cumulatedHeightsB[i], "B", interiorsB[i]));
            }

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"test 2: {allFloors.Count}");


            // sort by absolute cumulated heights
            // NOTE: if there are possitive and negative values it will generate unexpected results
            allFloors.Sort((a, b) => Math.Abs(a.AccumulatedHeight).CompareTo(Math.Abs(b.AccumulatedHeight)));
            

            // 4. iterate resulting sorted height list: 
            // on each height (either on A or B), find the height on the other list (either A or B) that is the biggest on its list but smaller than the iterated value.

            // A vectors
            Vector3d perpDir = Vector3d.CrossProduct(baseFacadeLine.Direction, Vector3d.ZAxis);
            perpDir.Unitize();
            Vector3d opositePerpDir = -perpDir;

            // B vectors


            List<Line> facadePortionBottomLinesA = new List<Line>();
            List<Line> facadePortionBottomLinesB = new List<Line>();
            List<double> facadePortionHeightsA = new List<double>();
            List<double> facadePortionHeightsB = new List<double>();

            for (int i = 0; i < allFloors.Count; i++)
            {
                // TODO: Fix loop for last element

                Floor currentFloor = allFloors[i];
                bool neighbourFound = false;

                for (int j = 0; j < allFloors.Count; j++)
                {
                    Floor candidate = allFloors[j];

                    // find equal or next higher floor from the other list
                    if (Math.Abs(candidate.AccumulatedHeight) >= Math.Abs(currentFloor.AccumulatedHeight) && candidate.ListIdentifier != currentFloor.ListIdentifier)
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
                            currentFloor.SetbackVector = currentFloor.ListIdentifier == "A" ? perpDir : -perpDir;
                        }
                        else if (currentFloor.IsInterior && !candidate.IsInterior) // return vector 2
                        {
                            // TODO: check correct orientation; what about the orientation of each line?
                            // they will change the orientation, so we need a way to get absolute orientation
                            // POSSIBLE SOLUTION: flip all lines with a guide
                            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "different interior and exterior 2");
                            currentFloor.SetbackVector = currentFloor.ListIdentifier == "A" ? opositePerpDir : -opositePerpDir;

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

                    if(currentFloor.IsInterior)
                    {
                        this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Current is interior.");
                        currentFloor.SetbackVector = currentFloor.ListIdentifier == "A" ? opositePerpDir : -opositePerpDir;
                    }
                    else
                    {
                        this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Current is exterior: no reason for facade");
                        // currentFloor.SetbackVector = Vector3d.Zero;
                        continue; // skip floor as it does not need facade
                    }
                }

                
                // 7. create lines and vectors for facade setbacks

                double previousCumulatedHeight = i == 0 ? 0 : allFloors[i - 1].AccumulatedHeight;

                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"test 5 : iteration {i} - {currentFloor.ToString()}.");

                Line bottomLine = baseFacadeLine;
                bottomLine.To = bottomLine.To + currentFloor.SetbackVector + (Vector3d.ZAxis * previousCumulatedHeight);
                bottomLine.From = bottomLine.From + currentFloor.SetbackVector + (Vector3d.ZAxis * previousCumulatedHeight);

                double facadePortionHeight = currentFloor.AccumulatedHeight - previousCumulatedHeight;

                if (currentFloor.ListIdentifier == "A")
                {
                    facadePortionBottomLinesA.Add(bottomLine);
                    facadePortionHeightsA.Add(facadePortionHeight);
                }
                else
                {
                    facadePortionBottomLinesB.Add(bottomLine);
                    facadePortionHeightsB.Add(facadePortionHeight);
                }
            }

            DA.SetDataList(0, facadePortionBottomLinesA);
            DA.SetDataList(1, facadePortionBottomLinesB);
            DA.SetDataList(2, facadePortionHeightsA);
            DA.SetDataList(3, facadePortionHeightsB);
        }

        public struct Floor
        {
            public double Height;
            public double AccumulatedHeight;
            public string ListIdentifier;
            public bool IsInterior;
            public Vector3d SetbackVector;

            public Floor(double height, double accumulatedHeight, string listIdentifier, bool isInterior, Vector3d setbackVector = default)
            {
                this.Height = height;
                this.AccumulatedHeight = accumulatedHeight;
                this.ListIdentifier = listIdentifier;
                this.IsInterior = isInterior;
                this.SetbackVector = setbackVector;
            }

            override public string ToString()
            {
                return $"Height: {this.Height} | CumulatedHeight: {this.AccumulatedHeight} | List: {this.ListIdentifier} | IsInterior: {this.IsInterior} | SetbackVector: {this.SetbackVector.ToString()}";
            }
        }


        private List<double> CreateCumulativeList(List<double> list)
        {
            List<double> cumulativeList = new List<double>(list.Count);
            double sum = 0;

            foreach (var n in list)
            {
                sum += n;
                cumulativeList.Add(sum);
            }

            return cumulativeList;
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
