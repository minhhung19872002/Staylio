using StayHost.Domain;

namespace StayHost.Domain.Tests;

/// <summary>docs/01 TĐ-13 — the distances a guest reads to orient themselves.</summary>
public class LandmarksTests
{
    /// <summary>Sunset Villa, Đà Nẵng — the seeded listing nearest My Khe beach.</summary>
    private const double DaNangLat = 16.0544;
    private const double DaNangLng = 108.2022;

    [Fact]
    public void The_nearest_places_come_first()
    {
        var near = Landmarks.Near("Đà Nẵng", DaNangLat, DaNangLng);

        Assert.NotEmpty(near);
        Assert.Equal(near.OrderBy(l => l.DistanceKm).Select(l => l.Name), near.Select(l => l.Name));
    }

    [Fact]
    public void Only_the_city_the_listing_is_in()
    {
        var near = Landmarks.Near("Đà Lạt", 11.9404, 108.4583);
        Assert.All(near, l => Assert.DoesNotContain("Mỹ Khê", l.Name));
        Assert.Contains(near, l => l.Name.Contains("Xuân Hương"));
    }

    [Fact]
    public void A_city_nobody_listed_gets_nothing_rather_than_something_wrong()
    {
        Assert.Empty(Landmarks.Near("Cà Mau", 9.1769, 105.1524));
        Assert.Empty(Landmarks.Near("", 0, 0));
        Assert.Empty(Landmarks.Near(null!, 0, 0));
    }

    [Fact]
    public void Nothing_too_far_away_is_worth_mentioning()
    {
        // A listing on the far edge of the province is still "Đà Nẵng", but the
        // beach 60 km away is not an orientation, it is a drive.
        var far = Landmarks.Near("Đà Nẵng", 16.6, 108.9);
        Assert.All(far, l => Assert.True(l.DistanceKm <= Landmarks.RelevantKm));
    }

    [Fact]
    public void The_list_stays_short_enough_to_read()
    {
        Assert.True(Landmarks.Near("Đà Nẵng", DaNangLat, DaNangLng).Count <= Landmarks.Shown);
        Assert.Equal(2, Landmarks.Near("Đà Nẵng", DaNangLat, DaNangLng, take: 2).Count);
    }

    [Fact]
    public void Walking_distance_is_written_in_metres()
    {
        Assert.Equal("600 m", Landmarks.DistanceLabel(0.6));
        Assert.Equal("950 m", Landmarks.DistanceLabel(0.94));
        Assert.Equal("50 m", Landmarks.DistanceLabel(0.04));
    }

    [Fact]
    public void Anything_further_is_written_in_kilometres_the_way_people_say_it()
    {
        Assert.Equal("1,2 km", Landmarks.DistanceLabel(1.23));
        Assert.Equal("12 km", Landmarks.DistanceLabel(12.04));
        Assert.Equal("1 km", Landmarks.DistanceLabel(1.0));
    }

    [Fact]
    public void Every_seeded_city_of_any_size_can_orient_a_guest()
    {
        foreach (var city in new[] { "Đà Nẵng", "Hội An", "Đà Lạt", "Nha Trang", "Hà Nội", "TP. Hồ Chí Minh" })
            Assert.Contains(city, Landmarks.Cities);
    }

    [Fact]
    public void A_landmark_is_measured_from_the_listing_not_from_the_city()
    {
        // The same landmark, seen from two different listings in one city, has
        // to be two different distances. Neither point sits on a landmark, or
        // both would simply read zero.
        const string beach = "Bãi biển Mỹ Khê";

        var seaside = Landmarks.Near("Đà Nẵng", 16.0570, 108.2440).Single(l => l.Name == beach);
        var westOfTheRiver = Landmarks.Near("Đà Nẵng", 16.0500, 108.2100).Single(l => l.Name == beach);

        Assert.True(seaside.DistanceKm < westOfTheRiver.DistanceKm,
            $"{seaside.DistanceKm} should be nearer than {westOfTheRiver.DistanceKm}");
    }

    [Fact]
    public void Standing_on_a_landmark_reads_as_zero_rather_than_as_an_error()
    {
        var onIt = Landmarks.Near("Đà Nẵng", 16.0605, 108.2470, take: 1).Single();
        Assert.Equal(0, onIt.DistanceKm, 3);
        Assert.Equal("0 m", Landmarks.DistanceLabel(onIt.DistanceKm));
    }

    [Fact]
    public void Distance_to_the_centre_matches_the_city_however_it_was_typed()
    {
        var km = Landmarks.FromCentreKm("Thành phố Hồ Chí Minh", 10.7720, 106.6980);
        Assert.NotNull(km);
        Assert.True(km < 0.01);
        Assert.InRange(Landmarks.FromCentreKm("Hà Nội", 21.0287, 105.8624)!.Value, 0.9, 1.2);
    }

    [Fact]
    public void A_city_without_a_known_centre_has_no_distance() =>
        Assert.Null(Landmarks.FromCentreKm("Làng Không Tên", 10, 106));
}
