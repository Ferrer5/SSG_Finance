using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyMvcApp.Models
{
    [Table("reports")]
    public class Report
    {
        [Key]
        [Column("report_id")]
        public int ReportId { get; set; }

        [Required]
        [Column("report_type")]
        public string ReportType { get; set; } = string.Empty;

        [Required]
        [Column("title")]
        public string Title { get; set; } = string.Empty;

        [Column("school_year_id")]
        public int? SchoolYearId { get; set; }

        [Column("semester")]
        public string? Semester { get; set; }

        [Column("date_from")]
        public DateTime? DateFrom { get; set; }

        [Column("date_to")]
        public DateTime? DateTo { get; set; }

        [Required]
        [Column("beginning_balance")]
        public decimal BeginningBalance { get; set; } = 0;

        [Required]
        [Column("total_revenue")]
        public decimal TotalRevenue { get; set; } = 0;

        [Required]
        [Column("total_expenses")]
        public decimal TotalExpenses { get; set; } = 0;

        [Required]
        [Column("running_balance")]
        public decimal RunningBalance { get; set; } = 0;

        [Required]
        [Column("status")]
        public string Status { get; set; } = "Draft";

        [Required]
        [Column("created_by")]
        public int CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("SchoolYearId")]
        public SchoolYear? SchoolYear { get; set; }

        [ForeignKey("CreatedBy")]
        public Account Creator { get; set; } = null!;

        public virtual ICollection<ReportItem> Items { get; set; } = new List<ReportItem>();
    }
}
