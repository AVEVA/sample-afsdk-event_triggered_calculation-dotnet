namespace EventTriggeredCalc
{
    public class CalculationContext
    {
        /// <summary>
        /// The input tag used in the calculation
        /// </summary>
        public string InputTagName { get; set; }

        /// <summary>
        /// The output tag that the calculation output is written to
        /// </summary>
        public string OutputTagName { get; set; }
    }
}
