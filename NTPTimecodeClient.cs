using System;

namespace StudioLog.Core
{
    /// <summary>
    /// Timecode clock source provider.
    /// 
    /// NOTE: The "NTP" mode currently uses the system's UTC clock (DateTime.UtcNow) with
    /// timezone conversion. It does NOT perform actual NTP network time requests.
    /// For true NTP sync, a future version should query an NTP server (e.g., pool.ntp.org)
    /// and compute an offset from the system clock.
    /// 
    /// Current modes:
    /// - "System Clock": Local time (DateTime.Now)
    /// - "NTP": UTC time with DST-aware timezone conversion (DateTime.UtcNow + TimeZoneInfo)
    /// - "Free Run": Elapsed time from when mode was activated (starts at 00:00:00:00)
    /// </summary>
    public class NTPTimecodeClient
    {
        private double _frameRate = 30.0;
        private string _clockSource = "System Clock";
        private string _timezoneId = "UTC"; // IANA or Windows timezone ID
        private DateTime _freeRunStartTime = DateTime.MinValue;

        public void SetFrameRate(double frameRate)
        {
            _frameRate = frameRate;
        }

        public void SetClockSource(string clockSource, string timezoneId = "UTC")
        {
            Console.WriteLine($"[NTPTimecodeClient] Setting clock source to: {clockSource}, Timezone: {timezoneId}");
            _clockSource = clockSource;
            _timezoneId = timezoneId;
            
            // Initialize free run start time when switching to Free Run
            if (clockSource == "Free Run")
            {
                _freeRunStartTime = DateTime.Now;
                Console.WriteLine($"[Free Run] Start time set to: {_freeRunStartTime:HH:mm:ss.fff}");
            }
        }

        public string GetTimecodeString()
        {
            DateTime now;

            switch (_clockSource)
            {
                case "Free Run":
                    // Free Run: Count from 00:00:00:00
                    var elapsed = DateTime.Now - _freeRunStartTime;
                    now = new DateTime(1, 1, 1).Add(elapsed);
                    break;

                case "NTP":
                    // NTP: Use UTC time with DST-aware timezone conversion
                    try
                    {
                        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(_timezoneId);
                        now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
                    }
                    catch
                    {
                        // Fallback to UTC if timezone not found
                        now = DateTime.UtcNow;
                    }
                    break;

                case "System Clock":
                default:
                    // System Clock: Use local system time
                    now = DateTime.Now;
                    break;
            }

            int frames = (int)((now.Millisecond / 1000.0) * _frameRate);
            return $"{now:HH:mm:ss}:{frames:D2}";
        }
    }
}
