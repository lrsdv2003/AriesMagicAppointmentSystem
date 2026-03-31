namespace AriesMagicAppointmentSystem.Models
{
    public class Service
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int DurationInHours { get; set; }
        public bool IsArchived { get; set; }

        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }
}