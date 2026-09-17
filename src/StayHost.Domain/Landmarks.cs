namespace StayHost.Domain;

/// <summary>
/// docs/01 TĐ-13 — "khoảng cách tới các điểm chính".
///
/// A guest reading a map wants to know how far the beach is, not to work it out
/// from two pins. The list is small, static and Vietnamese-specific, so it lives
/// in code rather than in a table nobody would ever edit: adding a city here is
/// a one-line change, and there is no admin screen to build for it.
/// </summary>
public static class Landmarks
{
    public readonly record struct Landmark(string City, string Name, double Latitude, double Longitude);

    /// <summary>How far out a landmark is still worth mentioning.</summary>
    public const double RelevantKm = 25;

    /// <summary>How many to show; more than this is a list, not an orientation.</summary>
    public const int Shown = 4;

    private static readonly Landmark[] All =
    [
        // Đà Nẵng
        new("Đà Nẵng", "Bãi biển Mỹ Khê", 16.0605, 108.2470),
        new("Đà Nẵng", "Cầu Rồng", 16.0614, 108.2270),
        new("Đà Nẵng", "Bà Nà Hills", 15.9950, 107.9960),
        new("Đà Nẵng", "Sân bay Đà Nẵng", 16.0439, 108.1994),
        new("Đà Nẵng", "Ngũ Hành Sơn", 16.0035, 108.2630),

        // Hội An
        new("Hội An", "Chùa Cầu", 15.8770, 108.3260),
        new("Hội An", "Bãi biển An Bàng", 15.9080, 108.3450),
        new("Hội An", "Phố cổ Hội An", 15.8801, 108.3380),
        new("Hội An", "Rừng dừa Bảy Mẫu", 15.9060, 108.3730),

        // Đà Lạt
        new("Đà Lạt", "Hồ Xuân Hương", 11.9430, 108.4400),
        new("Đà Lạt", "Chợ Đà Lạt", 11.9425, 108.4370),
        new("Đà Lạt", "Hồ Tuyền Lâm", 11.9020, 108.4230),
        new("Đà Lạt", "Ga Đà Lạt", 11.9450, 108.4550),

        // Nha Trang
        new("Nha Trang", "Bãi biển Trần Phú", 12.2400, 109.1960),
        new("Nha Trang", "Tháp Bà Ponagar", 12.2650, 109.1950),
        new("Nha Trang", "VinWonders Nha Trang", 12.2110, 109.2440),
        new("Nha Trang", "Chợ Đầm", 12.2500, 109.1920),

        // TP. Hồ Chí Minh
        new("TP. Hồ Chí Minh", "Chợ Bến Thành", 10.7720, 106.6980),
        new("TP. Hồ Chí Minh", "Nhà thờ Đức Bà", 10.7797, 106.6990),
        new("TP. Hồ Chí Minh", "Phố đi bộ Nguyễn Huệ", 10.7740, 106.7040),
        new("TP. Hồ Chí Minh", "Sân bay Tân Sơn Nhất", 10.8180, 106.6520),

        // Hà Nội
        new("Hà Nội", "Hồ Hoàn Kiếm", 21.0287, 105.8524),
        new("Hà Nội", "Phố cổ Hà Nội", 21.0340, 105.8500),
        new("Hà Nội", "Lăng Chủ tịch Hồ Chí Minh", 21.0369, 105.8345),
        new("Hà Nội", "Sân bay Nội Bài", 21.2210, 105.8070),

        // Huế
        new("Huế", "Đại Nội Huế", 16.4700, 107.5780),
        new("Huế", "Chùa Thiên Mụ", 16.4540, 107.5450),
        new("Huế", "Cầu Trường Tiền", 16.4690, 107.5920),

        // Phú Quốc
        new("Phú Quốc", "Bãi Sao", 10.0430, 104.0270),
        new("Phú Quốc", "Thị trấn Dương Đông", 10.2170, 103.9600),
        new("Phú Quốc", "Cáp treo Hòn Thơm", 10.0350, 104.0180),

        // Sa Pa
        new("Sa Pa", "Nhà thờ đá Sa Pa", 22.3360, 103.8440),
        new("Sa Pa", "Bản Cát Cát", 22.3260, 103.8290),
        new("Sa Pa", "Đỉnh Fansipan", 22.3030, 103.7750),

        // Quy Nhơn
        new("Quy Nhơn", "Bãi biển Quy Nhơn", 13.7690, 109.2340),
        new("Quy Nhơn", "Eo Gió", 13.8420, 109.2870),
        new("Quy Nhơn", "Tháp Đôi", 13.7810, 109.2160),

        // Phan Thiết
        new("Phan Thiết", "Bãi biển Mũi Né", 10.9330, 108.2870),
        new("Phan Thiết", "Đồi cát Bàu Trắng", 11.1000, 108.4200),

        // Vũng Tàu
        new("Vũng Tàu", "Bãi Sau", 10.3350, 107.0930),
        new("Vũng Tàu", "Tượng Chúa Kitô Vua", 10.3300, 107.0850)
    ];

    /// <summary>
    /// The nearest few landmarks of a listing's city, closest first. Anything
    /// past <see cref="RelevantKm"/> is left out: "42 km to the beach" tells a
    /// guest nothing they wanted to know.
    /// </summary>
    public static IReadOnlyList<(string Name, double DistanceKm)> Near(
        string city, double latitude, double longitude, int take = Shown)
    {
        var wanted = (city ?? "").Trim();

        return All
            .Where(l => string.Equals(l.City, wanted, StringComparison.OrdinalIgnoreCase))
            .Select(l => (l.Name, DistanceKm: Ranking.DistanceKm(latitude, longitude, l.Latitude, l.Longitude)))
            .Where(l => l.DistanceKm <= RelevantKm)
            .OrderBy(l => l.DistanceKm)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// "600 m" below a kilometre, "1,2 km" above it. A guest reads distance to
    /// decide whether to walk, and "0.6 km" is not how anybody says that.
    /// </summary>
    public static string DistanceLabel(double km) =>
        km < 1
            ? $"{Math.Round(km * 1000 / 50) * 50:0} m"
            : $"{km:0.#} km".Replace('.', ',');

    /// <summary>
    /// Where "the centre" is, for the "cách trung tâm x km" line and the
    /// distance filter and sort. The point a local would call the middle of
    /// town, not the geometric centroid of its boundary.
    /// </summary>
    private static readonly (string City, double Latitude, double Longitude)[] Centres =
    [
        ("Đà Nẵng", 16.0614, 108.2270),        // Cầu Rồng
        ("Hội An", 15.8801, 108.3380),         // Phố cổ
        ("Đà Lạt", 11.9425, 108.4370),         // Chợ Đà Lạt
        ("Nha Trang", 12.2450, 109.1940),
        ("TP. Hồ Chí Minh", 10.7720, 106.6980), // Chợ Bến Thành
        ("Hà Nội", 21.0287, 105.8524),         // Hồ Hoàn Kiếm
        ("Huế", 16.4690, 107.5920),
        ("Phú Quốc", 10.2170, 103.9600),       // Dương Đông
        ("Sa Pa", 22.3360, 103.8440),
        ("Quy Nhơn", 13.7760, 109.2230),
        ("Phan Thiết", 10.9280, 108.1020),
        ("Vũng Tàu", 10.3460, 107.0840),
        ("Hạ Long", 20.9510, 107.0800),
        ("Cần Thơ", 10.0340, 105.7880),
        ("Côn Đảo", 8.6830, 106.6090),
        ("Hải Phòng", 20.8560, 106.6830),
        ("Ninh Bình", 20.2540, 105.9750),
        ("Hà Giang", 22.8230, 104.9840),
        ("Mũi Né", 10.9330, 108.2870),
        ("Tam Đảo", 21.4560, 105.6450)
    ];

    /// <summary>Kilometres from the listing to its own city's centre, or null when that centre is not known.</summary>
    public static double? FromCentreKm(string? city, double latitude, double longitude)
    {
        var key = StayHost.Domain.Cities.Key(city);
        if (key.Length == 0) return null;
        foreach (var c in Centres)
        {
            if (StayHost.Domain.Cities.Key(c.City) == key)
                return Ranking.DistanceKm(latitude, longitude, c.Latitude, c.Longitude);
        }
        return null;
    }

    /// <summary>Every city this list can say anything about.</summary>
    public static IReadOnlyList<string> Cities =>
        All.Select(l => l.City).Distinct().OrderBy(c => c).ToList();
}
