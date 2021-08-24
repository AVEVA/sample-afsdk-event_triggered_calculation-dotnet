using OSIsoft.AF.Asset;

namespace EventTriggeredCalc
{
    public class Input
    {
        /// <summary>
        /// The resolved AF Attribute to read data from
        /// </summary>
        public AFAttribute Attribute { get; set; }

        /// <summary>
        /// Whether this AF Attribute's update should trigger a new calculation
        /// </summary>
        public bool IsTrigger { get; set; }
    }
}
