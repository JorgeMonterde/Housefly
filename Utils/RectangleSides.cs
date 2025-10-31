using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Housefly.Utils
{
    public class RectangleSides : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the RectangleSides class.
        /// </summary>
        public RectangleSides()
          : base("RectangleSides", "Rectangle",
              "Returns rectangle base and height from the specified area and factor that determines the relation between sides.",
              "Housefly", "Utils")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Area", "A", "Area for the rectangle", GH_ParamAccess.item);
            pManager.AddNumberParameter("Factor", "F", "Factor that determines the relation between sides", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Area", "A", "Area for the rectangle", GH_ParamAccess.item);
            pManager.AddNumberParameter("Factor", "F", "Factor that determines the relation between sides", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double area = 0;
            double factor = 0;

            if (!DA.GetData(0, ref area)) return;
            if (!DA.GetData(1, ref factor)) return;

            if (area <= 0 || factor <= 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Area and factor must be positive");
                return;
            }

            double b = Math.Sqrt(area / factor);
            double h = Math.Sqrt(area * factor);

            DA.SetData(0, b);
            DA.SetData(1, h);
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
            get { return new Guid("3C15C318-8A7F-4F61-95AE-F3842FEA6A0D"); }
        }
    }
}