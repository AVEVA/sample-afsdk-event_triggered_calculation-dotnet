using System.Collections.Generic;

namespace EventTriggeredCalc
{
    public class ContextDefinition
    {
        /// <summary>
        /// The input tag used in the calculation
        /// </summary>
        public List<UnresolvedInput> Inputs { get; }

        /// <summary>
        /// The output tag that the calculation output is written to
        /// </summary>
        public string OutputAttributeName { get; set; }
    }
}
