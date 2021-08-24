using OSIsoft.AF.Asset;

namespace EventTriggeredCalc
{
    public class UnresolvedInput
    {
        /// <summary>
        /// The resolved AF Attribute to read data from
        /// </summary>
        public string AttributeName { get; set; }

        /// <summary>
        /// Whether this AF Attribute's update should trigger a new calculation
        /// </summary>
        public bool IsTrigger { get; set; }
    }
}
