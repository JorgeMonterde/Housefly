using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Housefly.Site
{
    public class CatastroFloorsComponent : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public CatastroFloorsComponent()
          : base("Extract floors info from Catastro", "CatastroFloors",
            "Extracts the number of floors and location of the texts from a set of ",
            "Housefly", "Site")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Number of floors", "N", "Number of floors of the buildings in roman numbers", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Number of floors", "N", "Number of floors the building has", GH_ParamAccess.list);
            pManager.AddPointParameter("Locations", "P", "Locations where the number of floors are applied", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<object> catastroTexts = new List<object>();

            if (!DA.GetDataList(0, catastroTexts)) return;

            int[] numberOfFloors = new int[catastroTexts.Count];
            Point3d[] points = new Point3d[catastroTexts.Count];

            for (int i = 0; i < catastroTexts.Count; i++)
            {
                TextEntity textEntity = (TextEntity) catastroTexts[i];
                if (textEntity == null) continue;
                string textContent = textEntity.PlainText;

                numberOfFloors[i] = textContent.Split('+').Select(RomanToInt).Max();
                BoundingBox box = textEntity.GetBoundingBox(true);
                points[i] = box.GetEdges().OrderBy(edge => edge.PointAt(0.5).Y).Select(edge => edge.PointAt(0.5)).ToArray()[0];
            }

            DA.SetDataList(0, numberOfFloors);
            DA.SetDataList(1, points);

        }

        public static int RomanToInt(string roman)
        {
            Dictionary<char, int> romanMap = new Dictionary<char, int>()
            {
                {'I', 1 },
                {'V', 5 },
                {'X', 10 },
                {'L', 50 },
                {'C', 100 },
                {'D', 500 },
                {'M', 1000 }
            };

            if (string.IsNullOrWhiteSpace(roman))
            {
                return 0;
            };

            roman = roman.Trim().ToUpper();
            bool isNegative = roman.StartsWith("-");
            if (isNegative) roman = roman.Substring(1); // Remove the minus

            int result = 0;
            int prevValue = 0;

            foreach (char c in roman)
            {
                if (!romanMap.ContainsKey(c))
                {
                    return 0;
                }

                int value = romanMap[c];

                if (value > prevValue)
                {
                    // e.g., IV = 5 - 1*2 = 4
                    result += value - 2 * prevValue;
                }
                else
                {
                    result += value;
                }

                prevValue = value;
            }

            return isNegative ? -result : result;
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("6ed27f4b-ce65-46cb-839f-2f897ea9b654");
    }
}