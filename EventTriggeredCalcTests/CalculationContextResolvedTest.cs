using OSIsoft.AF.PI;

namespace EventTriggeredCalcTests
{
    public class CalculationContextResolvedTest
    {
        /// <summary>
        /// The input tag used in the calculation
        /// </summary>
        public PIPoint InputTag { get; set; }

        /// <summary>
        /// The input and output tags are resolved at separate times in the test, this stores the value for later
        /// </summary>
        public string OutputTagName { get; set; }

        /// <summary>
        /// The output tag that the calculation output is written to
        /// </summary>
        public PIPoint OutputTag { get; set; }
    }
}
