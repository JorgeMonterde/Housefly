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
            pManager.AddLineParameter("Facade lines", "L", "Base lines representing possible facades on plan.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Floor heights", "H", "Floor heights for spaces.", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Interior flags", "I", "Tree matching 'Floor heights' indicating interior (true) or exterior (false) condition for each space.", GH_ParamAccess.tree);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Facade setback lines", "L", "Resulting facade setback lines for each input facade line", GH_ParamAccess.tree);
            pManager.AddVectorParameter("Setback vectors", "V", "Resulting setback unitized vectors for each 'Facade setback line'", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Facade setback heights ", "H", "Resulting facade setback heights for each 'Facade setback line'", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // get all inputs

            List<Line> lines = new List<Line>();
            GH_Structure<GH_Number> heightsTree = null;
            GH_Structure<GH_Boolean> interiorsTree = null;

            if(!DA.GetDataList("Facade lines", lines)) return;
            if(!DA.GetDataTree("Floor heights", out heightsTree)) return;
            if(!DA.GetDataTree("Interior flags", out interiorsTree)) return;

            // validate

            if (heightsTree.PathCount != interiorsTree.PathCount)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Heights and interiors trees have different branch counts: {heightsTree.PathCount} vs {interiorsTree.PathCount}.");
                return;
            }

            if (lines.Count - heightsTree.PathCount != 1) // info for all spaces between each two lines must be provided
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Lines count minus heights branches count must be equal 1: {lines.Count} - {heightsTree.PathCount} != 1.");
                return;
            }

            // iterate lines

            GH_Structure<GH_Line> facadeSetbackLinesTree = new GH_Structure<GH_Line>();
            GH_Structure<GH_Vector> setbackVectorsTree = new GH_Structure<GH_Vector>();
            GH_Structure<GH_Number> facadeSetbackHeightsTree = new GH_Structure<GH_Number>();

            for (int i = 0; i < lines.Count; i++)
            {
                int hCount = (i == 0)
                    ? heightsTree.Branches[0].Count
                    : (i == lines.Count - 1)
                    ? heightsTree.Branches[i - 1].Count
                    : heightsTree.Branches[i].Count;

                int iCount = (i == 0)
                    ? interiorsTree.Branches[0].Count
                    : (i == lines.Count - 1)
                    ? interiorsTree.Branches[i - 1].Count
                    : interiorsTree.Branches[i].Count;


                if (hCount != iCount)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Branch {heightsTree.Paths[i]} on trees mismatch: {hCount} heights vs {iCount} interior flags.");
                    continue; // skip iteration
                }

                List<double> heightsA = new List<double>();
                List<double> heightsB = new List<double>();
                List<bool> interiorsA = new List<bool>();
                List<bool> interiorsB = new List<bool>();

                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Loop {i}.");


                if (i == 0)
                {
                    var heightsFirstBranch = heightsTree.get_Branch(heightsTree.Paths[i]);
                    var interiorsFirstBranch = interiorsTree.get_Branch(interiorsTree.Paths[i]);

                    foreach (var item in heightsFirstBranch) heightsB.Add((item as GH_Number).Value);
                    foreach (var item in interiorsFirstBranch) interiorsB.Add((item as GH_Boolean).Value);

                    List<double> cumulatedHeights = this.CreateCumulativeList(heightsB);
                    cumulatedHeights.Sort((a, b) => Math.Abs(a).CompareTo(Math.Abs(b)));
                    double auxHeight = cumulatedHeights[cumulatedHeights.Count - 1]; // get highest absolute value

                    // just one big floor with external spaces to represent outer space
                    heightsA = new List<double>() { auxHeight };
                    interiorsA = new List<bool>() { false };
                }
                else if (i == lines.Count - 1)
                {
                    var heightsLastBranch = heightsTree.get_Branch(heightsTree.Paths[i - 1]);
                    var interiorsLastBranch = interiorsTree.get_Branch(interiorsTree.Paths[i - 1]);

                    foreach (var item in heightsLastBranch) heightsA.Add((item as GH_Number).Value);
                    foreach (var item in interiorsLastBranch) interiorsA.Add((item as GH_Boolean).Value);

                    List<double> cumulatedHeights = this.CreateCumulativeList(heightsA);
                    cumulatedHeights.Sort((a, b) => Math.Abs(a).CompareTo(Math.Abs(b)));
                    double auxHeight = cumulatedHeights[cumulatedHeights.Count - 1]; // get highest absolute value

                    // just one big floor with external spaces to represent outer space
                    heightsB = new List<double>() { auxHeight };
                    interiorsB = new List<bool>() { false };
                }
                else
                {
                    GH_Path pathA = heightsTree.Paths[i - 1];
                    GH_Path pathB = heightsTree.Paths[i];

                    foreach (var item in heightsTree.get_Branch(pathA)) heightsA.Add((item as GH_Number).Value);
                    foreach (var item in interiorsTree.get_Branch(pathA)) interiorsA.Add((item as GH_Boolean).Value);

                    foreach (var item in heightsTree.get_Branch(pathB)) heightsB.Add((item as GH_Number).Value);
                    foreach (var item in interiorsTree.get_Branch(pathB)) interiorsB.Add((item as GH_Boolean).Value);

                }

                Line line = lines[i];
                var ( facadePortionLines, setbackVectors, facadePortionHeights ) = this.CreateSetbacks(line, heightsA, heightsB, interiorsA, interiorsB);

                GH_Path newPath = new GH_Path(i);
                facadeSetbackLinesTree.AppendRange(facadePortionLines, newPath);
                setbackVectorsTree.AppendRange(setbackVectors, newPath);
                facadeSetbackHeightsTree.AppendRange(facadePortionHeights, newPath);
            }


            DA.SetDataTree(0, facadeSetbackLinesTree);
            DA.SetDataTree(1, setbackVectorsTree);
            DA.SetDataTree(2, facadeSetbackHeightsTree);
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

        private (List<GH_Line> facadePortionLines, List<GH_Vector> setbackVectors, List<GH_Number> facadePortionHeights) CreateSetbacks(
            Line baseFacadeLine,
            List<double> heightsA,
            List<double> heightsB,
            List<bool> interiorsA,
            List<bool> interiorsB)
        {

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "CreateSetbacks running...");


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

            if (!isValid) return (new List<GH_Line>(), new List<GH_Vector>(), new List<GH_Number>());
            
            // LOGIC

            // 1. convert heights to "cumulated heights"

            List<double> cumulatedHeightsA = this.CreateCumulativeList(heightsA);
            List<double> cumulatedHeightsB = this.CreateCumulativeList(heightsB);

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

            // sort by absolute cumulated heights
            // NOTE: if there are possitive and negative values it will generate unexpected results
            allFloors.Sort((a, b) => Math.Abs(a.AccumulatedHeight).CompareTo(Math.Abs(b.AccumulatedHeight)));
            

            // 4. iterate resulting sorted height list: 
            // on each height (either on A or B), find the height on the other list (either A or B) that is the biggest on its list but smaller than the iterated value.

            Vector3d perpDir = Vector3d.CrossProduct(baseFacadeLine.Direction, Vector3d.ZAxis);
            perpDir.Unitize();
            Vector3d opositePerpDir = -perpDir;

            List<GH_Line> facadePortionLines = new List<GH_Line>();
            List<GH_Vector> setbackVectors = new List<GH_Vector>();
            List<GH_Number> facadePortionHeights = new List<GH_Number>();

            for (int i = 0; i < allFloors.Count; i++)
            {
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
                            currentFloor.SetbackVector = currentFloor.ListIdentifier == "A" ? perpDir : -perpDir;
                        }
                        else if (currentFloor.IsInterior && !candidate.IsInterior) // return vector 2
                        {
                            // TODO: check correct orientation; what about the orientation of each line?
                            // they will change the orientation, so we need a way to get absolute orientation
                            // POSSIBLE SOLUTION: flip all lines with a guide
                            currentFloor.SetbackVector = currentFloor.ListIdentifier == "A" ? opositePerpDir : -opositePerpDir;

                        }
                        else // (currentFloor.IsInterior && candidate.IsInterior) && (!currentFloor.IsInterior && !candidate.IsInterior)
                        {
                            // both interior or both exterior: return to interior or exterior partitions (or not returned anything at all)
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
                        this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Current is interior.");
                        currentFloor.SetbackVector = currentFloor.ListIdentifier == "A" ? opositePerpDir : -opositePerpDir;
                    }
                    else
                    {
                        this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Current is exterior: no reason for facade");
                        // currentFloor.SetbackVector = Vector3d.Zero;
                        continue; // skip floor as it does not need facade
                    }
                }

                
                // 7. create lines and vectors for facade setbacks

                double previousCumulatedHeight = i == 0 ? 0 : allFloors[i - 1].AccumulatedHeight;

                Line bottomLine = baseFacadeLine;
                bottomLine.To = bottomLine.To + (Vector3d.ZAxis * previousCumulatedHeight);
                bottomLine.From = bottomLine.From + (Vector3d.ZAxis * previousCumulatedHeight);

                double facadePortionHeight = currentFloor.AccumulatedHeight - previousCumulatedHeight;

                facadePortionLines.Add(new GH_Line(bottomLine));
                setbackVectors.Add(new GH_Vector(currentFloor.SetbackVector));
                facadePortionHeights.Add(new GH_Number(facadePortionHeight));
            }


            return ( facadePortionLines, setbackVectors, facadePortionHeights);
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
