
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Housefly.Utils
{
    public class AsyncLayerRefComponent : GH_Component
    {

        private bool _shouldExpire = false;
        private List<GeometryBase> _geometries = new List<GeometryBase>();


        /// <summary>
        /// Initializes a new instance of the AsyncLayerRefComponent class.
        /// </summary>
        public AsyncLayerRefComponent()
          : base("Asynchronous Layer Reference", "AsyncLayerRef",
              "Asynchronous version of the Layer Reference component from LunchBox",
              "Housefly", "Utils")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Toggle", "T", "Toggle for referencing objects by layer", GH_ParamAccess.item, false);
            pManager.AddTextParameter("LayerName", "L", "Name of the layer to be referenced", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometries", "G", "Geometries on layers", GH_ParamAccess.list);
            // pManager.AddTextParameter("Name", "N", "Name of the geometries", GH_ParamAccess.list);
            // pManager.AddTextParameter("User strings", "U", "User strings assigned to the object", GH_ParamAccess.list);
            // pManager.AddTextParameter("GUID", "ID", "The geometries ids", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            if (_shouldExpire)
            {
                // second time "SolveInstance" was invoked (after expiring the solution)
                DA.SetDataList(0, _geometries);
                _shouldExpire = false;
                this.Message = "Done!";
                return;
            }

            bool toggle = false;
            string layerName = "";
            if (!DA.GetData(0, ref toggle)) return;
            if (!DA.GetData(1, ref layerName)) return;


            this.Message = "Referencing layer...";
            this.AsyncGetLayerObjects(layerName);
        }

        private void AsyncGetLayerObjects(string layerName)
        {
            Task.Run(() =>
            {
                // expensive operation here

                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null) return;

                int layerIndex = doc.Layers.FindByFullPath(layerName, -1);
                if (layerIndex == -1)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Layer \"{layerName}\" not found.");
                    return;
                }

                List<GeometryBase> geometries = new List<GeometryBase>();
                foreach (RhinoObject obj in doc.Objects)
                {
                    if (obj.Attributes.LayerIndex == layerIndex)
                    {
                        GeometryBase geometryCopy = obj.Geometry?.Duplicate();
                        if (geometryCopy != null) geometries.Add(geometryCopy);
                    }
                }

                // data to pass downstream

                this._geometries = geometries;
                _shouldExpire = true;

                // Expire the component solution to downstream updated data
                // ExpireSolution(true); // this would not work for this as we are calculating the new data on another thread, and we need to find the component's thread.
                // We can find the correct thread with the following lines

                RhinoApp.InvokeOnUiThread(
                    (Action)delegate { ExpireSolution(true); } // create the delegate function and cast it int an action
                );

            });
        }

        // custom ExpireDownStreamObjects function to trigger it whenever we want
        protected override void ExpireDownStreamObjects()
        {
            if (_shouldExpire)
            {
                base.ExpireDownStreamObjects();
            }
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
            get { return new Guid("4d9facfa-9237-4341-b6f3-747c40b861dd"); }
        }
    }
}
