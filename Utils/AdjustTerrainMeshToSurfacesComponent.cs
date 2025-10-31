using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Diagnostics;


namespace Housefly.Utils
{
    public class AdjustTerrainMeshToSurfacesComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AdjustTerrainMeshToSurfacesComponent class.
        /// </summary>
        public AdjustTerrainMeshToSurfacesComponent()
          : base("AdjustTerrainMeshToSurfacesComponent", "AdjustTerrainMesh",
              "Modifies a mesh (as a terrain) with near surfaces (as building, roads...)",
              "Housefly", "Utils")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Mesh to modify", GH_ParamAccess.item);
            pManager.AddSurfaceParameter("Surfaces", "S", "Planar surfaces near the mesh", GH_ParamAccess.list);
            pManager.AddNumberParameter("Distance", "D", "Distance as threshold to modify the mesh", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Allow lowering", "L", "If true, vertices above surfaces will be lowered (cuts).", GH_ParamAccess.item, false);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Modified Mesh", "M", "Terrain mesh modified by nearby surfaces", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // TODO: "refinement" mode to add more vertices that approximates to the surface boundary to solve straight intersections with terrain


            Mesh terrain = null;
            List<Brep> surfaces = new List<Brep>();
            double threshold = 1.0;
            bool allowLowering = false;

            if (!DA.GetData(0, ref terrain)) return;
            if (!DA.GetDataList(1, surfaces)) return;
            if (!DA.GetData(2, ref threshold)) return;
            if (!DA.GetData(3, ref allowLowering)) return;
            if (terrain == null || surfaces.Count == 0) return;

            Mesh modified = terrain.DuplicateMesh();

            for (int i = 0; i < modified.Vertices.Count; i++)
            {
                Point3f vertex = modified.Vertices[i];

                Point3d[] intersectionPts = Intersection.RayShoot(new Ray3d(vertex, Vector3d.ZAxis), surfaces, 1);

                if (intersectionPts.Length == 0)
                {
                    intersectionPts = Intersection.RayShoot(new Ray3d(vertex, Vector3d.ZAxis * -1), surfaces, 1);
                };
                if (intersectionPts.Length == 0)
                {
                    continue;
                };

                Point3d intersectionPt = intersectionPts[0];
                double verticalDiff = intersectionPt.Z - vertex.Z;
                double absDist = Math.Abs(verticalDiff);
                if (absDist <= threshold)
                {
                    if (verticalDiff > 0) // Lifting (surface above terrain)
                    {
                        modified.Vertices.SetVertex(i, intersectionPt);
                    }
                    else if (allowLowering && verticalDiff < 0) // Lowering (surface below terrain)
                    {
                        modified.Vertices.SetVertex(i, intersectionPt);
                    }
                }
            }

            // Clean and finalize mesh
            modified.Normals.ComputeNormals();
            modified.UnifyNormals();
            modified.Weld(Math.PI / 6);
            modified.Compact();

            DA.SetData(0, modified);
        }

        // create Delaunay mesh
        private Mesh createDelaunayMeshForXYPlane(List<Point3d> points)
        {
            // code from http://james-ramsden.com/create-2d-delaunay-triangulation-mesh-with-c-in-grasshopper/

            // convert point3d to node2
            // grasshopper requires that nodes are saved within a Node2List for Delaunay
            var nodes = new Grasshopper.Kernel.Geometry.Node2List();
            for (int i = 0; i < points.Count; i++)
            {
                // notice how we only read in the X and Y coordinates
                // this is why points should be mapped onto the XY plane
                nodes.Append(new Grasshopper.Kernel.Geometry.Node2(points[i].X, points[i].Y));
            }

            // solve Delaunay
            var delaunayMesh = new Mesh();
            var faces = new List<Grasshopper.Kernel.Geometry.Delaunay.Face>();

            faces = Grasshopper.Kernel.Geometry.Delaunay.Solver.Solve_Faces(nodes, 0);

            // output
            delaunayMesh = Grasshopper.Kernel.Geometry.Delaunay.Solver.Solve_Mesh(nodes, 0, ref faces);
            for (int i = 0; i < points.Count; i++)
            {
                delaunayMesh.Vertices.SetVertex(i, points[i]);
            }

            return delaunayMesh;
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
            get { return new Guid("7CD61F22-B725-42E4-9727-EA09E0896527"); }
        }
    }
}