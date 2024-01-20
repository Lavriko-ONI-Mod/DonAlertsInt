namespace DonAlertsInt;

public class DonationExportDto
{
    public string? Sender { get; set; }
    public float AmountSource { get; set; }
    public string Currency { get; set; }
    public float AmountInMyCurrency { get; set; }
    public string Type { get; set; }
    public DateTime? CreatedAt { get; set; }
}