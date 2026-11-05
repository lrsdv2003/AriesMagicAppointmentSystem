namespace AriesMagicAppointmentSystem.Models
{
    public class EmailSettings
    {
        public string Provider { get; set; } = "Smtp";
        public string ApiKey { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool EnableSsl { get; set; }
    }
}