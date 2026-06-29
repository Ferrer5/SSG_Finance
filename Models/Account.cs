using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyMvcApp.Models
{
    [Table("accounts")]
    public class Account
    {
        [Key]
        [Column("account_id")]
        public int AccountId { get; set; }

        [Required]
        [StringLength(50)]
        [Column("school_id")]
        public string SchoolId { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [StringLength(150)]
        [EmailAddress]
        [Column("email")]
        public string? Email { get; set; }

        [Required]
        [Column("roles")]
        public UserRole Role { get; set; }

        [Required]
        [Column("request_status")]
        public RequestStatus RequestStatus { get; set; } = RequestStatus.Pending;

        [Required]
        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(255)]
        [Column("password_reset_token")]
        public string? PasswordResetToken { get; set; }

        [Column("password_reset_token_expires")]
        public DateTime? PasswordResetTokenExpires { get; set; }

        // Navigation properties
        public User? User { get; set; }
        public ICollection<OrgFeePayment> ReceivedPayments { get; set; } = new List<OrgFeePayment>();
        public ICollection<OtherFund> ReceivedFunds { get; set; } = new List<OtherFund>();
        public ICollection<Expense> RecordedExpenses { get; set; } = new List<Expense>();
        public ICollection<Receipt> IssuedReceipts { get; set; } = new List<Receipt>();
    }

    public enum UserRole
    {
        Student,
        Treasurer,
        Admin,
        Professor,
        Advisor
    }

    public enum RequestStatus
    {
        Pending,
        Approved,
        Rejected
    }
}
