namespace StayHost.Domain.Tests;

public class ChildPolicyTests
{
    [Fact]
    public void A_place_takes_children_unless_the_host_says_otherwise()
    {
        var open = new Listing();
        Assert.True(open.ChildrenAllowed);
        Assert.False(ChildPolicy.Refuses(open, new PartySize(2, 1, 1, 0)));
    }

    [Fact]
    public void An_adults_only_place_refuses_children_and_infants_but_not_adults()
    {
        var adults = new Listing { ChildrenAllowed = false };
        Assert.True(ChildPolicy.Refuses(adults, new PartySize(2, 1, 0, 0)));
        Assert.True(ChildPolicy.Refuses(adults, new PartySize(2, 0, 1, 0)));
        Assert.False(ChildPolicy.Refuses(adults, new PartySize(3)));
        Assert.Equal(["Không nhận trẻ em."], ChildPolicy.Lines(adults));
    }

    [Fact]
    public void Cots_and_extra_beds_are_said_either_way()
    {
        var lines = ChildPolicy.Lines(new Listing { CribAvailable = true });
        Assert.Contains(lines, l => l.StartsWith("Có cũi"));
        Assert.Contains(lines, l => l.StartsWith("Không có giường phụ"));
    }
}
