namespace AriesMagicAppointmentSystem.Models
{
    public class EmailVerification
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public string CodeHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime LastSentAt { get; set; }
        public int AttemptCount { get; set; }
        public bool IsUsed { get; set; }
        public DateTime? VerifiedAt { get; set; }
    }
}
