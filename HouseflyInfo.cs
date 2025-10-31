using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace Housefly
{
    public class HouseflyInfo : GH_AssemblyInfo
    {
        public override string Name => "Housefly";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("71395a0f-b402-456f-b171-c77728a8fd9f");

        //Return a string identifying you or your company.
        public override string AuthorName => "Jorge MO";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}