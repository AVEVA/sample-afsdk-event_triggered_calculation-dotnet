using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggeredCalcTests
{
    [TestClass]
    public class UnitTests
    {
        [TestMethod]
        public void EventTriggeredCalcTest()
        {
            Assert.IsTrue(EventTriggeredCalc.Program.MainLoop(true));
        }
    }
}
