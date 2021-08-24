using System.Collections.Generic;

namespace EventTriggeredCalc
{
    public class AppSettings
    {
        /// <summary>
        /// The unresolved list of input and output tag names for the calculation to run against
        /// </summary>
        public ContextDefinition ContextDefinition { get; }

        public List<string> Contexts { get; }

        /// <summary>
        /// The name of the AF Server to use
        /// An empty string will resolve to the Default AF Server
        /// </summary>
        public string AFServerName { get; set; }

        /// <summary>
        /// The name of the AF Database to use
        /// An empty string will resolve to the Default AF Database
        /// </summary>
        public string AFDatabaseName { get; set; }

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
