using Xunit;

namespace EventTriggeredCalcTests
{
    public class UnitTests
    {
        [Fact]
        public void EventTriggeredCalcTest()
        {
            Assert.True(EventTriggeredCalc.Program.MainLoop(true));
        }
    }
}
