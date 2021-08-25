using System.Collections.Generic;

namespace EventTriggeredCalc
{
    public class AppSettings
    {
        /// <summary>
        /// The list of input attributes whose updates will trigger a new calculation
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Need to read from settings file deserialization")]
        public IList<string> Inputs { get; set; }


        /// <summary>
        /// The list of input attributes whose updates will trigger a new calculation
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Need to read from settings file deserialization")]
        public IList<string> TriggerAttributes { get; set; }

        /// <summary>
        /// The number of seconds of time series data to keep in the cache
        /// </summary>
        public int CacheTimeSpanSeconds { get; set; }


        /// <summary>
        /// The list of elements to run the calculation against
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Need to read from settings file deserialization")]
        public IList<string> Contexts { get; set; }

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
