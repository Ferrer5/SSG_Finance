using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyMvcApp.Models
{
    [Table("report_items")]
    public class ReportItem
    {
        [Key]
        [Column("item_id")]
        public int ItemId { get; set; }

        [Required]
        [Column("report_id")]
        public int ReportId { get; set; }

        [Required]
        [Column("item_type")]
        public string ItemType { get; set; } = string.Empty;

        [Required]
        [Column("item_ref_id")]
        public int ItemRefId { get; set; }

        [Column("description", TypeName = "text")]
        public string? Description { get; set; }

        [Required]
        [Column("amount")]
        public decimal Amount { get; set; }

        [ForeignKey("ReportId")]
        public Report Report { get; set; } = null!;
    }
}
