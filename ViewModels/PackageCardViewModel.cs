namespace AriesMagicAppointmentSystem.ViewModels;

public class PackageCardViewModel
{
    public string Name { get; set; } = "";
    public string Eyebrow { get; set; } = "";
    public string Number { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public List<string> Inclusions { get; set; } = new();
    public bool Featured { get; set; }
    public int? ServiceId { get; set; }
    public int? Duration { get; set; }
    public bool Manage { get; set; }
    public string ActionController { get; set; } = "Services";
    public string ActionName { get; set; } = "Details";
    public string ActionLabel { get; set; } = "View Details";
}
