using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models
{
    public class Availability
    {
        [Key]
        public int AvailabilityId { get; set; }

        [Required]
        public int ParkingSpotId { get; set; }

        public DayType DayType { get; set; }      // Weekday, Weekend, Everyday

        public TimeOnly StartTime { get; set; }

        public TimeOnly EndTime { get; set; }

        public DateOnly? EffectiveFrom { get; set; }

        public DateOnly? EffectiveUntil { get; set; }

        [ForeignKey(nameof(ParkingSpotId))]
        public ParkingSpot ParkingSpot { get; set; } = null!;
    }
}
