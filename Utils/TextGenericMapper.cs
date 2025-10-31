using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Housefly.Utils
{
    public class TextGenericMapper : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the TextGenericMapper class.
        /// </summary>
        public TextGenericMapper()
          : base("TextGenericMapper", "TextMapper",
              "Maps generic data according to a list of text entries and a data tree with lists of appearences with those texts",
              "Housefly", "Utils")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("List of texts", "T", "List of unique texts to use on mapping", GH_ParamAccess.list);
            pManager.AddGenericParameter("Data", "D", "Data to map", GH_ParamAccess.list);
            pManager.AddTextParameter("Map", "M", "Data tree to use for mapping the list of texts with the data", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Non redundant", "R", "Boolean flag to avoid duplicated appearances due to mapping", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Mapped data", "D", "Resulting data tree with mapped data", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> texts = new List<string>();
            List<IGH_Goo> data = new List<IGH_Goo>();
            GH_Structure<GH_String> map = new GH_Structure<GH_String>();
            bool isNonRedundant = false;

            if (!DA.GetDataList(0, texts)) return;
            if (!DA.GetDataList(1, data)) return;
            if (!DA.GetDataTree(2, out map)) return;
            DA.GetData(3, ref isNonRedundant);

            GH_Structure<IGH_Goo> output = new GH_Structure<IGH_Goo>();
            var seenPairs = new HashSet<string>(); // to track unique adjacency pairs
            IList<GH_Path> mapPaths = map.Paths;

            if (texts.Count != data.Count)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error: 'texts' and 'data' inputs must have the same length.");
                return;
            }

            if (texts.Count != mapPaths.Count)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error: 'texts' input length and 'map' amount of branches must be the same.");
                return;
            }

            for (int i = 0; i < texts.Count; i++)
            {
                GH_Path path = mapPaths[i];

                IList<GH_String> branch = map.get_Branch(path) as IList<GH_String>;
                if (branch == null) continue;

                foreach (GH_String mappingText in branch)
                {
                    int mappingTextIndex = texts.IndexOf(mappingText.Value);
                    if (mappingTextIndex == -1)
                    {
                        this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Warning: mapping value '{mappingText.Value}' not found in text list.");
                        continue;
                    }

                    // apply "non redundant" mode
                    if (isNonRedundant)
                    {
                        // create a unique key for the pair (unordered) and only process if pair hasn't been seen before
                        string key = (i < mappingTextIndex) ? $"{i}-{mappingTextIndex}" : $"{mappingTextIndex}-{i}";
                        if (seenPairs.Contains(key))
                        {
                            output.Append(null, path);
                            continue;
                        }
                        seenPairs.Add(key);
                    }

                    object mappedValue = data[mappingTextIndex];
                    output.Append(GH_Convert.ToGoo(mappedValue), path);
                }
            }

            DA.SetDataTree(0, output);
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
            get { return new Guid("A70F0522-6E51-4EDB-A259-451D360B294D"); }
        }
    }
}