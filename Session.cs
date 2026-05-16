using System;

namespace StudioLog.Models
{
    public class Session
    {
        public int Id { get; set; }
        public string SessionName { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ClosedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
