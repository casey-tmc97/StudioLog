using System;
using System.Runtime.InteropServices;

namespace StudioLog.Core
{
    /// <summary>
    /// P/Invoke wrapper for libltc - Industry-standard LTC library
    /// https://github.com/x42/libltc (LGPL v3)
    /// libltc.dll is bundled with StudioLog and copied to the output directory.
    /// </summary>
    public static class LibLTC
    {
        // DLL name - matches the libltc.dll you provided
        private const string DLL_NAME = "libltc";
        
        #region Enums and Constants
        
        /// <summary>
        /// TV standard for frame rate
        /// </summary>
        public enum LTC_TV_STANDARD
        {
            LTC_TV_525_60 = 0,  // NTSC: 30fps, 29.97fps, 24fps
            LTC_TV_625_50 = 1   // PAL: 25fps
        }
        
        // Encoder flags
        public const int LTC_USE_DATE = 1;          // Use SMPTE 309M date in user bits
        public const int LTC_TC_CLOCK = 2;          // BGF1: time is wall-clock
        public const int LTC_BGF_DONT_TOUCH = 4;    // Don't modify BGF bits
        public const int LTC_NO_PARITY = 8;         // Don't set parity bit
        
        #endregion
        
        #region Structures
        
        /// <summary>
        /// SMPTE timecode structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SMPTETimecode
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] timezone;     // Timezone in BCD (SMPTE 309M) - 6 bytes: "+HHMM\0"
            public byte years;          // Year (00-99)
            public byte months;         // Month (01-12)
            public byte days;           // Day (01-31)
            public byte hours;          // Hours (00-23)
            public byte mins;           // Minutes (00-59)
            public byte secs;           // Seconds (00-59)
            public byte frame;          // Frame number (0-29)
            
            /// <summary>
            /// Create a simple timecode (no date/timezone)
            /// </summary>
            public static SMPTETimecode Create(int hours, int minutes, int seconds, int frame)
            {
                return new SMPTETimecode
                {
                    hours = (byte)hours,
                    mins = (byte)minutes,
                    secs = (byte)seconds,
                    frame = (byte)frame,
                    timezone = new byte[6],
                    years = 0,
                    months = 0,
                    days = 0
                };
            }
        }
        
        #endregion
        
        #region Encoder Functions
        
        /// <summary>
        /// Create LTC encoder
        /// </summary>
        /// <param name="sample_rate">Audio sample rate (e.g., 48000)</param>
        /// <param name="fps">Frames per second (e.g., 30, 29.97, 25, 24)</param>
        /// <param name="standard">TV standard</param>
        /// <param name="flags">Encoder flags (0 for default)</param>
        /// <returns>Encoder handle or IntPtr.Zero on failure</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ltc_encoder_create(
            double sample_rate,
            double fps,
            LTC_TV_STANDARD standard,
            int flags);
        
        /// <summary>
        /// Free LTC encoder
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ltc_encoder_free(IntPtr encoder);
        
        /// <summary>
        /// Set timecode for next frame
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ltc_encoder_set_timecode(
            IntPtr encoder,
            ref SMPTETimecode smpte);
        
        /// <summary>
        /// Encode one LTC frame into internal buffer
        /// </summary>
        /// <returns>0 on success</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ltc_encoder_encode_frame(IntPtr encoder);
        
        /// <summary>
        /// Get size of internal encoder buffer
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ltc_encoder_get_buffersize(IntPtr encoder);
        
        /// <summary>
        /// Copy encoded audio to buffer and flush internal buffer
        /// </summary>
        /// <param name="buf">Destination buffer (8-bit unsigned audio)</param>
        /// <param name="size">Size of destination buffer</param>
        /// <returns>Number of bytes copied</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ltc_encoder_copy_buffer(
            IntPtr encoder,
            byte[] buf,
            int size);
        
        /// <summary>
        /// Increment timecode by one frame
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ltc_encoder_inc_timecode(IntPtr encoder);
        
        /// <summary>
        /// Set output volume
        /// </summary>
        /// <param name="dbFS">Volume in dBFS (e.g., -3.0 for default, 0.0 for maximum)</param>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ltc_encoder_set_volume(
            IntPtr encoder,
            double dbFS);
        
        /// <summary>
        /// Get pointer to internal buffer (advanced use)
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ltc_encoder_get_bufferptr(
            IntPtr encoder,
            out int size,
            out int parity);
        
        /// <summary>
        /// Copy the internal LTCFrame (10 bytes / 80 bits) to the provided buffer.
        /// This gives access to the raw frame bit data after encoding.
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ltc_encoder_get_frame(
            IntPtr encoder,
            [Out] byte[] frame);
        
        /// <summary>
        /// Set the internal LTCFrame from the provided buffer.
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ltc_encoder_set_frame(
            IntPtr encoder,
            byte[] frame);
        
        #endregion
        
        #region Utility Functions
        
        /// <summary>
        /// Get libltc version string
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ltc_version();
        
        /// <summary>
        /// Get libltc version as string
        /// </summary>
        public static string GetVersion()
        {
            try
            {
                IntPtr ptr = ltc_version();
                return Marshal.PtrToStringAnsi(ptr) ?? "unknown";
            }
            catch
            {
                return "unavailable";
            }
        }
        
        #endregion
    }
}
