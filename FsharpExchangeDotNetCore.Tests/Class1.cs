
using NUnit.Framework;

namespace FSharpExchangeDotNetCoreTests
{
    [TestFixture]
    public class Tests
    {
        [Test]
        public void Check_to_see_if_MD_or_VS4Mac_can_show_dot_net_core_based_NUnit_tests()
        {
            // there is a MonoDevelop/VS4Mac bug that makes the unit test panel
            // not show up if the library linking to NUnit is .NET Core-based,
            // see https://twitter.com/knocte/status/945905606606516224
        }
    }
}
