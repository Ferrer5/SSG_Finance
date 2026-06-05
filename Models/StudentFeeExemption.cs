using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyMvcApp.Models
{
    [Table("student_fee_exemptions")]
    public class StudentFeeExemption
    {
        [Key]
        [Column("exemption_id")]
        public int ExemptionId { get; set; }

        [Required]
        [Column("user_id")]
        public int UserId { get; set; }

        [Required]
        [Column("school_year_id")]
        public int SchoolYearId { get; set; }

        [Required]
        [Column("semester")]
        public Semester Semester { get; set; }

        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        [ForeignKey("SchoolYearId")]
        public SchoolYear SchoolYear { get; set; } = null!;
    }
}
