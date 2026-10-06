using Shouldly;

namespace mos.Tests;

// Example showing the Given / When / Then style from test/test_instructions.md.
// Delete this file once real tests exist.
public class ExampleTests {

	[Fact]
	public void Reverse_TwoItems_SwapsOrder() {

		// Given: a list containing the numbers 1 and 2, in that order
		var items = new List<int> { 1, 2 };

		// When: the list is reversed
		items.Reverse();

		// Then: the list now holds 2 followed by 1
		items.ShouldBe(new[] { 2, 1 });
	}
}
