namespace StayHost.Domain.Tests;

public class VndFormatTests
{
    [Theory]
    [InlineData(980000, "980.000₫")]
    [InlineData(1150000.4, "1.150.000₫")]
    [InlineData(0, "0₫")]
    [InlineData(-25000, "-25.000₫")]
    public void Dots_every_three_digits_whatever_the_machine_culture(decimal amount, string expected)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Assert.Equal(expected, Vnd.Format(amount));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }
}
