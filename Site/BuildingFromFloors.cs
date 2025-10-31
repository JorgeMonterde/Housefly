using Eto.Forms;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Housefly.Site
{
    public class BuildingFromFloors : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BuildingFromFloors class.
        /// </summary>
        public BuildingFromFloors()
          : base("BuildingFromFloors", "BuildingsFromFloors",
              "Creates buildings as Breps or Meshes from base closed curves and the number of floors",
              "Housefly", "Site")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Base curves", "C", "Base closed curves of the buildings", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Number of floors", "N", "Number of floors of the buildings", GH_ParamAccess.list, 3); // default value
            pManager.AddNumberParameter("Floor heights", "H", "Height of the floors", GH_ParamAccess.list, 3); // default value
            pManager.AddBooleanParameter("Brep or mesh", "T", "Type of the output, either Brep ('false') or Mesh ('true')", GH_ParamAccess.item, true); // create meshes as default
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Buildings", "B", "Resulting building geometries, either as Breps or Meshes", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            List<Curve> baseCurves = new List<Curve>();
            List<int> numberOfFloors = new List<int>();
            List<double> floorHeights = new List<double>();
            bool isResultAsMesh = true;

            if (!DA.GetDataList(0, baseCurves)) return; // TODO: add warning runtime message 
            DA.GetDataList(1, numberOfFloors); 
            DA.GetDataList(2, floorHeights);
            DA.GetData(3, ref isResultAsMesh);

            int count = Math.Min(numberOfFloors.Count, Math.Min(baseCurves.Count, floorHeights.Count)); // set number of iterations
            double tol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            List<GeometryBase> outputBreps = new List<GeometryBase>();


            for (int i = 0; i < count; i++)
            {
                int floors = numberOfFloors[i];
                Curve baseCurve = baseCurves[i];
                double heightPerFloor = floorHeights[i];

                if (baseCurve == null || !baseCurve.IsClosed || !baseCurve.IsPlanar())
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Curve in index {i} is null, not closed or not planar");
                    continue;
                }

                double totalHeight = floors * heightPerFloor;
                Vector3d heightVector = Vector3d.Multiply(Vector3d.ZAxis, totalHeight);

                if (isResultAsMesh)
                {
                    //Mesh
                    //sionMesh = CreateBuildingsAsMesh(baseCurve, heightVector);
                    Mesh extrusionMesh = CreateMeshExtrudingClosedCurve(baseCurve, heightVector); // PROBLEM HERE
                    if (extrusionMesh == null /*|| !extrusionMesh.IsClosed*/) // PROBLEM HERE: this "if" is triggered
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Mesh from curve in index {i} is null or not closed");
                        continue;
                    }
                    outputBreps.Add(extrusionMesh);
                }
                else
                {
                    Brep extrusionBrep = Extrusion.CreateExtrusion(baseCurve, heightVector).ToBrep();
                    if (!extrusionBrep.IsSolid)
                    {
                        extrusionBrep = extrusionBrep.CapPlanarHoles(tol);
                    }
                    outputBreps.Add(extrusionBrep);
                }

                
            }




            DA.SetDataList(0, outputBreps);




        }

        public static Mesh CreateBuildingsAsMesh(Curve baseCurve, Vector3d extrusionVector)
        {
            // Discretize the curve into polygon points
            Polyline polyline;
            if (!baseCurve.TryGetPolyline(out polyline))
            {
                // GH_ActiveObject.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Curve must be a polyline (polygon).");
                return null;
            }

            // Remove duplicate end point if exists
            if (polyline.Count > 1 && polyline[0].DistanceToSquared(polyline[polyline.Count - 1]) < RhinoDoc.ActiveDoc.ModelAbsoluteTolerance) // notice tolerance is important when checking for duplicated objects
                polyline.RemoveAt(polyline.Count - 1);

            int count = polyline.Count;
            if (count < 3)
            {
                // GH_ActiveObject.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Not enough vertices to form a polygon.");
                return null;
            }

            Mesh prism = new Mesh();

            // Add base vertices
            List<int> baseIndices = new List<int>();
            foreach (Point3d pt in polyline)
                baseIndices.Add(prism.Vertices.Add(pt));

            // Add top vertices (offset by extrusion vector)
            List<int> topIndices = new List<int>();
            foreach (Point3d pt in polyline)
                topIndices.Add(prism.Vertices.Add(pt + extrusionVector));

            // Add side faces
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;

                int a = baseIndices[i];
                int b = baseIndices[next];
                int c = topIndices[next];
                int d = topIndices[i];

                prism.Faces.AddFace(a, b, c, d);
            }

            // Add bottom face (reversed to keep normal pointing down)
            prism.Faces.AddFace(baseIndices[2], baseIndices[1], baseIndices[0]);
            for (int i = 3; i < baseIndices.Count; i++)
                prism.Faces.AddFace(baseIndices[i], baseIndices[i - 1], baseIndices[0]);

            // Add top face
            for (int i = 2; i < topIndices.Count; i++)
                prism.Faces.AddFace(topIndices[0], topIndices[i - 1], topIndices[i]);

            // Finish mesh
            prism.Normals.ComputeNormals();
            prism.Compact();
            prism.UnifyNormals();

            return prism;

        }

        private Mesh CreateMeshExtrudingClosedCurve(Curve baseCurve, Vector3d extrusionVector)
        {
            // Discretize the curve into polygon points
            Polyline polyline;
            if (!baseCurve.TryGetPolyline(out polyline))
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Curve must be a polyline (polygon).");
                return null;
            }

            // Remove duplicate end point if exists
            if (polyline.Count > 1 && polyline[0].DistanceToSquared(polyline[polyline.Count - 1]) < RhinoDoc.ActiveDoc.ModelAbsoluteTolerance) // notice tolerance is important when checking for duplicated objects
                polyline.RemoveAt(polyline.Count - 1);

            if (!polyline.IsClosed)
            {
                polyline.Add(polyline[0]); // close cleanly
            }

            if (!polyline.IsValid)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Polyline is invalid.");
                return null;
            }

            int count = polyline.Count;
            if (count < 3)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Not enough vertices to form a polygon.");
                return null;
            }

            // Step 1: Triangulate the base polygon safely (handles concave)
            Mesh baseTriMesh = Mesh.CreateFromClosedPolyline(polyline);

            if (baseTriMesh == null)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to triangulate base polyline: 'CreateFromClosedPolyline' result is null.");
                return null;
            }

            if (baseTriMesh.Faces.Count == 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to triangulate base polyline: Mesh faces equal zero.");
                return null;
            }

            // Step 2: Build full mesh
            Mesh solidMesh = new Mesh();

            // Add bottom vertices
            List<int> baseVerticesIndexes = new List<int>();
            foreach (Point3d pt in polyline) baseVerticesIndexes.Add(solidMesh.Vertices.Add(pt));

            // Add top vertices (offset by extrusion vector)
            List<int> topVerticesIndexes = new List<int>();
            foreach (Point3d pt in polyline) topVerticesIndexes.Add(solidMesh.Vertices.Add(pt + extrusionVector));

            // Step 3: Add side quads
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count; // THIS LINES MAY CAUSING STRANGE MESH BOTTOM AND TOP PRISM FACES -> WRONG MESH TOPOGRAPHY

                int a = baseVerticesIndexes[i];
                int b = baseVerticesIndexes[next];
                int c = topVerticesIndexes[next];
                int d = topVerticesIndexes[i];

                solidMesh.Faces.AddFace(a, b, c, d);
            }

            // Step 4: Add bottom faces (from triangulated base mesh)
            foreach (MeshFace face in baseTriMesh.Faces)
            {
                int a = face.A;
                int b = face.B;
                int c = face.C;

                solidMesh.Faces.AddFace(baseVerticesIndexes[a], baseVerticesIndexes[b], baseVerticesIndexes[c]);
            }

            // Step 5: Add top faces (same triangles, but reversed order)
            foreach (MeshFace face in baseTriMesh.Faces)
            {
                int a = face.A;
                int b = face.B;
                int c = face.C;

                solidMesh.Faces.AddFace(topVerticesIndexes[c], topVerticesIndexes[b], topVerticesIndexes[a]);
            }

            // Finalize
            solidMesh.Normals.ComputeNormals();
            solidMesh.Compact();
            solidMesh.UnifyNormals();

            if (!solidMesh.IsValid || !solidMesh.IsClosed)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Mesh is invalid or not closed.");
            }

            return solidMesh;

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
            get { return new Guid("39EB9E18-7F61-45B0-A166-7CF11EA654B7"); }
        }
    }
}