namespace StayHost.Domain.Tests;

public class TravellerTypesTests
{
    [Theory]
    [InlineData(2, 1, 0, true, TravellerTypes.Business)]
    [InlineData(2, 0, 1, false, TravellerTypes.Family)]
    [InlineData(1, 0, 0, false, TravellerTypes.Solo)]
    [InlineData(2, 0, 0, false, TravellerTypes.Couple)]
    [InlineData(4, 0, 0, false, TravellerTypes.Group)]
    public void The_party_decides_the_kind_of_trip(int adults, int children, int infants, bool business, string expected) =>
        Assert.Equal(expected, TravellerTypes.Of(adults, children, infants, business));

    [Fact]
    public void Every_type_has_a_label() =>
        Assert.All(new[] { TravellerTypes.Business, TravellerTypes.Family, TravellerTypes.Couple,
                           TravellerTypes.Solo, TravellerTypes.Group },
            k => Assert.True(TravellerTypes.Labels.ContainsKey(k)));
}
