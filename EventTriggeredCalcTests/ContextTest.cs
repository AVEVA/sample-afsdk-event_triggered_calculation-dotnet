using System.Collections.Generic;
using EventTriggeredCalc;
using OSIsoft.AF.Asset;

namespace EventTriggeredCalcTest
{
    public class ContextTest
    {
        /// <summary>
        /// The input tag used in the calculation
        /// </summary>
        public List<Input> Inputs { get; }

        /// <summary>
        /// The output tag that the calculation output is written to
        /// </summary>
        public string OutputAttributeName { get; set; }

        /// <summary>
        /// The output tag that the calculation output is written to
        /// </summary>
        public AFAttribute OutputAttribute { get; set; }
    }
}
