using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StudioLog.Models
{
    public class TimecodeLogEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public int Id { get; set; }
        public int SessionId { get; set; }

        private string _timeCodeIn = string.Empty;
        public string TimeCodeIn
        {
            get => _timeCodeIn;
            set { _timeCodeIn = value; OnPropertyChanged(); }
        }

        private string _timeCodeOut = string.Empty;
        public string TimeCodeOut
        {
            get => _timeCodeOut;
            set { _timeCodeOut = value; OnPropertyChanged(); }
        }

        private string _duration = string.Empty;
        public string Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); }
        }

        private string _clipName = string.Empty;
        public string ClipName
        {
            get => _clipName;
            set { _clipName = value; OnPropertyChanged(); }
        }

        private string _notes = string.Empty;
        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(); }
        }

        private string _markTimecode = string.Empty;
        public string MarkTimecode
        {
            get => _markTimecode;
            set { _markTimecode = value; OnPropertyChanged(); }
        }

        private int? _parentEntryId;
        public int? ParentEntryId
        {
            get => _parentEntryId;
            set { _parentEntryId = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsMarkSubRow)); }
        }

        public bool IsMarkSubRow => ParentEntryId.HasValue;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
