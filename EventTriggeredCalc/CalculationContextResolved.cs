using OSIsoft.AF.PI;

namespace EventTriggeredCalc
{
    public class CalculationContextResolved
    {
        /// <summary>
        /// The input tag used in the calculation
        /// </summary>
        public PIPoint InputTag { get; set; }

        /// <summary>
        /// The output tag that the calculation output is written to
        /// </summary>
        public PIPoint OutputTag { get; set; }
    }
}
