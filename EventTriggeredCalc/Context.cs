using System.Collections.Generic;
using OSIsoft.AF.Asset;

namespace EventTriggeredCalc
{
    public class Context
    {
        /// <summary>
        /// The input tag used in the calculation
        /// </summary>
        public List<Input> Inputs { get; }

        /// <summary>
        /// The output tag that the calculation output is written to
        /// </summary>
        public AFAttribute OutputAttribute { get; set; }
        
        /// <summary>
        /// The element being used as the context
        /// </summary>
        public AFElement Element { get; set; }
    }
}
