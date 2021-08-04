using System.Collections.Generic;

namespace EventTriggeredCalc
{
    public class AppSettings
    {
        /// <summary>
        /// The unresolved list of input and output tag names for the calculation to run against
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Allow in configuration, reading from file")]
        public IList<CalculationContext> CalculationContexts { get; set; }

        /// <summary>
        /// The name of the PI Data Archive to use
        /// An empty string will resolve to the Default PI Data Archive
        /// </summary>
        public string PIDataArchiveName { get; set; }

        /// <summary>
        /// The interval that the timer triggers the checking of snapshot updates, in ms
        /// </summary>
        public int UpdateCheckIntervalMS { get; set; }

        /// <summary>
        /// The max number of snapshot updates to accept on each check
        /// This number should be tuned based on the number of tags, update frequency, and check frequency
        /// </summary>
        public int MaxEventsPerPeriod { get; set; }
    }
}
