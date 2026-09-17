namespace StayHost.Domain;

/// <summary>
/// docs/01 TC-04 and docs/02 G7 — "báo cáo thuế theo năm" for a host.
///
/// docs/03 §1 step 8 makes tax the guest's line and the platform's to remit, so
/// this is not a bill. It is the year written down: what guests paid on this
/// host's places, how much of that was tax, what the platform withheld, and what
/// reached the host — the four numbers an accountant asks for first.
///
/// A stay is counted in the year it **ended**. Some of the money for a December
/// checkout arrives in January, so the alternative reading is by payout date;
/// both are defensible and they disagree every year. Ending is chosen because it
/// is the year the service was actually delivered, and the payout date rides
/// along on every row so anyone working on a cash basis can re-cut it without
/// asking for a different report.
/// </summary>
public static class TaxReports
{
    /// <summary>One completed stay, reduced to what a tax report needs.</summary>
    public sealed record Stay(
        string Reference,
        string ListingTitle,
        DateOnly CheckIn,
        DateOnly CheckOut,
        DateOnly? PaidOutOn,
        decimal GuestPaid,
        decimal RoomSubtotal,
        decimal GuestServiceFee,
        decimal HostServiceFee,
        decimal HostPayout,
        IReadOnlyList<PriceLine> TaxLines)
    {
        public decimal Tax => TaxLines.Sum(l => l.Amount);
    }

    public sealed record MonthRow(
        int Month, int Stays, decimal GuestPaid, decimal Tax,
        decimal HostServiceFee, decimal HostPayout);

    /// <summary>
    /// One tax as it was actually charged. The names come from the rules that
    /// applied at the time of each booking, so a rule renamed or retired later
    /// still appears under the name the guest was shown.
    /// </summary>
    public sealed record TaxTotal(string Name, decimal Amount, int Stays);

    public sealed record Report(
        int Year,
        IReadOnlyList<MonthRow> Months,
        IReadOnlyList<TaxTotal> Taxes,
        int Stays,
        decimal GuestPaid,
        decimal RoomSubtotal,
        decimal GuestServiceFee,
        decimal Tax,
        decimal HostServiceFee,
        decimal HostPayout);

    /// <summary>Which years have anything in them, newest first, for a year picker.</summary>
    public static IReadOnlyList<int> YearsCovered(IEnumerable<Stay> stays) =>
        stays.Select(s => s.CheckOut.Year).Distinct().OrderByDescending(y => y).ToList();

    public static Report Build(IEnumerable<Stay> stays, int year)
    {
        var inYear = stays.Where(s => s.CheckOut.Year == year).ToList();

        // All twelve months, including the empty ones. A tax return is read as a
        // year, and a table that silently skips March makes the reader count.
        var months = Enumerable.Range(1, 12).Select(month =>
        {
            var rows = inYear.Where(s => s.CheckOut.Month == month).ToList();
            return new MonthRow(
                month, rows.Count,
                rows.Sum(s => s.GuestPaid), rows.Sum(s => s.Tax),
                rows.Sum(s => s.HostServiceFee), rows.Sum(s => s.HostPayout));
        }).ToList();

        var taxes = inYear
            .SelectMany(s => s.TaxLines.Select(l => (s.Reference, l.Label, l.Amount)))
            .GroupBy(x => x.Label)
            .Select(g => new TaxTotal(g.Key, g.Sum(x => x.Amount), g.Select(x => x.Reference).Distinct().Count()))
            .OrderByDescending(t => t.Amount)
            .ThenBy(t => t.Name)
            .ToList();

        return new Report(
            year, months, taxes,
            inYear.Count,
            inYear.Sum(s => s.GuestPaid),
            inYear.Sum(s => s.RoomSubtotal),
            inYear.Sum(s => s.GuestServiceFee),
            inYear.Sum(s => s.Tax),
            inYear.Sum(s => s.HostServiceFee),
            inYear.Sum(s => s.HostPayout));
    }

    public static string MonthLabel(int month) => month switch
    {
        1 => "Tháng 1", 2 => "Tháng 2", 3 => "Tháng 3", 4 => "Tháng 4",
        5 => "Tháng 5", 6 => "Tháng 6", 7 => "Tháng 7", 8 => "Tháng 8",
        9 => "Tháng 9", 10 => "Tháng 10", 11 => "Tháng 11", _ => "Tháng 12"
    };

    /// <summary>
    /// Said on the report itself, not only in the documentation: a host reading a
    /// tax total needs to know the platform already remitted it.
    /// </summary>
    public const string RemittanceNote =
        "Thuế trong bảng này do khách trả và Staylio đã nộp thay cho cơ quan thuế. " +
        "Đơn được tính vào năm có ngày trả phòng.";
}
