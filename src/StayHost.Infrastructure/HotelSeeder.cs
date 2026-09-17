using Microsoft.EntityFrameworkCore;
using StayHost.Domain;

namespace StayHost.Infrastructure;

/// <summary>
/// docs/01 MR-08 — two properties that sell rooms of a kind rather than the
/// whole place, so the counted-availability path has something behind it.
/// </summary>
public static class HotelSeeder
{
    private record Room(string Name, string Summary, int Inventory, int MaxGuests, int Beds,
                        double Sqm, decimal Price, string[] Features, string Image);

    private record Seed(string Title, string City, string District, double Lat, double Lng,
                        string Description, Room[] Rooms);

    public static async Task SeedAsync(StayHostDbContext db, CancellationToken ct = default)
    {
        if (await db.Listings.AnyAsync(l => l.Type == PlaceType.Hotel, ct)) return;

        var hosts = await db.Hosts.OrderBy(h => h.Id).ToListAsync(ct);
        if (hosts.Count == 0) return;

        var amenities = await db.Amenities
            .Where(a => a.Key == "wifi" || a.Key == "pool" || a.Key == "kitchen")
            .ToListAsync(ct);

        for (var i = 0; i < Seeds.Length; i++)
        {
            var s = Seeds[i];

            // The listing's own price is the cheapest room, which is what the
            // search card shows: "phòng từ …".
            var cheapest = s.Rooms.Min(r => r.Price);

            var listing = new Listing
            {
                HostId = hosts[i % hosts.Count].Id,
                // docs/03 §8 — a listing carries its host's title, never its own.
                IsSuperhost = hosts[i % hosts.Count].IsSuperhost,
                Slug = Slugify(s.Title, i + 1),
                Title = s.Title,
                City = s.City,
                Type = PlaceType.Hotel,
                RoomType = RoomType.PrivateRoom,
                Bedrooms = 1,
                Beds = s.Rooms.Max(r => r.Beds),
                Bathrooms = 1,
                MaxGuests = s.Rooms.Max(r => r.MaxGuests),
                PricePerNight = cheapest,
                CleaningFee = 0,
                Description = s.Description,
                Latitude = s.Lat,
                Longitude = s.Lng,
                InstantBook = true,
                IsPublished = true,
                IsComplete = true,
                HotelStars = StarsFor(cheapest),
                Rating = 4.75 + i * 0.05,
                ReviewCount = 120 + i * 40,
                CancellationTier = CancellationTier.Moderate,
                MinNights = 1,
                Images = s.Rooms.Select((r, j) => new ListingImage { Url = r.Image, SortOrder = j }).ToList(),
                RoomTypes = s.Rooms.Select((r, j) => new RoomTypeOption
                {
                    Name = r.Name,
                    Summary = r.Summary,
                    Inventory = r.Inventory,
                    MaxGuests = r.MaxGuests,
                    Beds = r.Beds,
                    SizeSqm = r.Sqm,
                    PricePerNight = r.Price,
                    ImageUrl = r.Image,
                    Features = string.Join('\n', r.Features),
                    SortOrder = j,
                    // Booking.com-style rate plans, so the demo hotels sell them.
                    NonRefundableDiscountPercent = 10,
                    BreakfastPricePerGuest = BreakfastFor(r.Price)
                }).ToList(),
                Amenities = amenities.Select(a => new ListingAmenity { AmenityId = a.Id }).ToList()
            };

            listing.RefreshSearchText();
            db.Listings.Add(listing);
        }

        await db.SaveChangesAsync(ct);
    }

    private static readonly Seed[] Seeds =
    [
        new("Khách sạn Bến Nghé", "TP. Hồ Chí Minh", "Quận 1", 10.7745, 106.7010,
            "Khách sạn 42 phòng cách chợ Bến Thành 400 mét. Lễ tân 24 giờ, bữa sáng phở và bánh mì " +
            "từ 6 giờ, hồ bơi nhỏ trên tầng 9 mở tới 22 giờ.",
            [
                new Room("Phòng Standard", "18 m², một giường đôi, cửa sổ nhìn ra hẻm", 18, 2, 1, 18,
                    1_150_000m, ["Giường đôi", "Bàn làm việc", "Điều hoà"],
                    "https://images.pexels.com/photos/271624/pexels-photo-271624.jpeg"),
                new Room("Phòng Deluxe hướng phố", "26 m², ban công nhỏ nhìn ra Lê Lợi", 14, 3, 2, 26,
                    1_780_000m, ["Ban công", "Hai giường đơn hoặc một giường lớn", "Bồn tắm"],
                    "https://images.pexels.com/photos/1918291/pexels-photo-1918291.jpeg"),
                new Room("Suite gia đình", "45 m², hai phòng ngủ thông nhau", 4, 5, 3, 45,
                    3_400_000m, ["Hai phòng ngủ", "Bếp nhỏ", "Bồn tắm", "Sofa giường"],
                    "https://images.pexels.com/photos/271618/pexels-photo-271618.jpeg")
            ]),

        new("Marble Bay Hotel Đà Nẵng", "Đà Nẵng", "Ngũ Hành Sơn", 16.0100, 108.2600,
            "Sát bãi biển Mỹ Khê, 60 phòng, hồ bơi ngoài trời và nhà hàng hải sản mở tới nửa đêm. " +
            "Xe đưa đón sân bay theo yêu cầu.",
            [
                new Room("Phòng Superior", "24 m², nhìn ra thành phố", 22, 2, 1, 24,
                    980_000m, ["Giường đôi", "Điều hoà", "Két sắt"],
                    "https://images.pexels.com/photos/271639/pexels-photo-271639.jpeg"),
                new Room("Phòng hướng biển", "30 m², ban công nhìn thẳng ra biển", 16, 3, 2, 30,
                    1_650_000m, ["Ban công hướng biển", "Bồn tắm", "Máy pha cà phê"],
                    "https://images.pexels.com/photos/261102/pexels-photo-261102.jpeg"),
                new Room("Penthouse tầng 12", "70 m², bể sục riêng trên ban công", 2, 4, 2, 70,
                    5_900_000m, ["Bể sục riêng", "Phòng khách riêng", "Bữa sáng tại phòng"],
                    "https://images.pexels.com/photos/2506988/pexels-photo-2506988.jpeg")
            ])
    ];

    /// <summary>A plausible class for a demo hotel, from its cheapest room.</summary>
    internal static int StarsFor(decimal cheapest) =>
        cheapest >= 2_000_000m ? 5 : cheapest >= 1_000_000m ? 4 : 3;

    /// <summary>About an eighth of the room, in round tens of thousands, never below 80.000 ₫.</summary>
    internal static decimal BreakfastFor(decimal roomPrice) =>
        Math.Max(80_000m, Math.Round(roomPrice / 8m / 10_000m) * 10_000m);

    private static string Slugify(string title, int n)
    {
        var normalised = SearchText.Normalize(title);
        var slug = new string(normalised.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return $"{slug.Trim('-')}-ks{n}";
    }
}
