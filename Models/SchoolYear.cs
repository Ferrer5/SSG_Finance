using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyMvcApp.Models
{
    [Table("school_years")]
    public class SchoolYear
    {
        [Key]
        public int SchoolYearId { get; set; }

        [Required]
        [Column("year_start")]
        public int YearStart { get; set; }

        [Required]
        [Column("year_end")]
        public int YearEnd { get; set; }

        [Required]
        [Column("year_status")]
        public YearStatus YearStatus { get; set; } = YearStatus.Current;

        [Column("first_sem_start")]
        public DateTime? FirstSemStart { get; set; }

        [Column("first_sem_end")]
        public DateTime? FirstSemEnd { get; set; }

        [Column("second_sem_start")]
        public DateTime? SecondSemStart { get; set; }

        [Column("second_sem_end")]
        public DateTime? SecondSemEnd { get; set; }

        // Navigation properties
        public virtual ICollection<FullAmount> FullAmounts { get; set; } = new List<FullAmount>();
    }

    public enum YearStatus
    {
        Current,
        Ended
    }
}
