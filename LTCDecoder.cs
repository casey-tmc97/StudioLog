using System;

namespace StudioLog.Core
{
    /// <summary>
    /// Decodes SMPTE LTC timecode from audio samples
    /// Uses bi-phase mark decoding per SMPTE 12M standard
    /// </summary>
    public class LTCDecoder
    {
        private readonly int _sampleRate;
        private bool _lastState = false;
        private int _samplesSinceTransition = 0;
        private int _bitBuffer = 0;
        private int _bitCount = 0;
        private byte[] _frameBuffer = new byte[10];
        private int _frameByteIndex = 0;
        private int _frameBitIndex = 0;
        private bool _syncFound = false;
        
        // Timing thresholds (will be calculated based on sample rate)
        private int _samplesPerBitMin;
        private int _samplesPerBitMax;
        private int _samplesPerHalfBitMin;
        private int _samplesPerHalfBitMax;
        
        // Decoded timecode
        public struct Timecode
        {
            public int Hours;
            public int Minutes;
            public int Seconds;
            public int Frames;
        }
        
        public LTCDecoder(int sampleRate, double frameRate = 30.0)
        {
            _sampleRate = sampleRate;
            
            // Calculate timing thresholds
            // LTC bit rate = frame_rate * 80 bits/frame
            double bitRate = frameRate * 80.0;
            double samplesPerBit = sampleRate / bitRate;
            
            // Allow 20% tolerance
            _samplesPerBitMin = (int)(samplesPerBit * 0.8);
            _samplesPerBitMax = (int)(samplesPerBit * 1.2);
            _samplesPerHalfBitMin = (int)(samplesPerBit * 0.4);
            _samplesPerHalfBitMax = (int)(samplesPerBit * 0.6);
            
            Console.WriteLine($"[LTC Decoder] Initialized: {sampleRate}Hz, {frameRate}fps");
            Console.WriteLine($"[LTC Decoder] Samples/bit: {samplesPerBit:F1}, range: {_samplesPerBitMin}-{_samplesPerBitMax}");
        }
        
        /// <summary>
        /// Process audio samples and return decoded timecode if a complete frame is found
        /// </summary>
        public Timecode? DecodeSamples(float[] samples)
        {
            Timecode? result = null;
            
            for (int i = 0; i < samples.Length; i++)
            {
                bool currentState = samples[i] > 0;
                
                // Detect transition
                if (currentState != _lastState)
                {
                    // Process transition timing
                    if (_samplesSinceTransition >= _samplesPerBitMin && 
                        _samplesSinceTransition <= _samplesPerBitMax)
                    {
                        // Full bit period - this is a '0' bit (single transition)
                        ProcessBit(false);
                    }
                    else if (_samplesSinceTransition >= _samplesPerHalfBitMin && 
                             _samplesSinceTransition <= _samplesPerHalfBitMax)
                    {
                        // Half bit period - this is part of a '1' bit (two transitions)
                        // Wait for second transition
                        _bitBuffer++;
                        if (_bitBuffer >= 2)
                        {
                            ProcessBit(true);
                            _bitBuffer = 0;
                        }
                    }
                    else
                    {
                        // Invalid timing - reset
                        _bitBuffer = 0;
                    }
                    
                    _samplesSinceTransition = 0;
                    _lastState = currentState;
                }
                
                _samplesSinceTransition++;
                
                // Check if we have a complete frame
                if (_syncFound && _frameByteIndex >= 10)
                {
                    result = DecodeFrame();
                    ResetFrame();
                }
            }
            
            return result;
        }
        
        private void ProcessBit(bool bitValue)
        {
            // Add bit to frame buffer
            if (bitValue)
            {
                _frameBuffer[_frameByteIndex] |= (byte)(1 << _frameBitIndex);
            }
            
            _frameBitIndex++;
            if (_frameBitIndex >= 8)
            {
                _frameBitIndex = 0;
                _frameByteIndex++;
            }
            
            _bitCount++;
            
            // Check for sync word (bits 64-79)
            if (_bitCount >= 80)
            {
                // Check sync word: 0x3FFD in LSB-first order
                ushort syncWord = (ushort)(_frameBuffer[8] | (_frameBuffer[9] << 8));
                if (syncWord == 0x3FFD)
                {
                    _syncFound = true;
                }
                else
                {
                    // Invalid sync - shift buffer and continue looking
                    ShiftBuffer();
                }
            }
        }
        
        private void ShiftBuffer()
        {
            // Shift buffer by one bit to continue searching for sync
            for (int i = 0; i < 9; i++)
            {
                _frameBuffer[i] = (byte)((_frameBuffer[i] >> 1) | ((_frameBuffer[i + 1] & 1) << 7));
            }
            _frameBuffer[9] >>= 1;
            _bitCount--;
        }
        
        private Timecode? DecodeFrame()
        {
            try
            {
                // Extract BCD values from frame buffer per SMPTE 12M
                int frameUnits = _frameBuffer[0] & 0x0F;
                int frameTens = (_frameBuffer[1] >> 0) & 0x03;
                
                int secondsUnits = (_frameBuffer[2] >> 0) & 0x0F;
                int secondsTens = (_frameBuffer[3] >> 0) & 0x07;
                
                int minutesUnits = (_frameBuffer[4] >> 0) & 0x0F;
                int minutesTens = (_frameBuffer[5] >> 0) & 0x07;
                
                int hoursUnits = (_frameBuffer[6] >> 0) & 0x0F;
                int hoursTens = (_frameBuffer[7] >> 0) & 0x03;
                
                return new Timecode
                {
                    Hours = hoursTens * 10 + hoursUnits,
                    Minutes = minutesTens * 10 + minutesUnits,
                    Seconds = secondsTens * 10 + secondsUnits,
                    Frames = frameTens * 10 + frameUnits
                };
            }
            catch
            {
                return null;
            }
        }
        
        private void ResetFrame()
        {
            Array.Clear(_frameBuffer, 0, _frameBuffer.Length);
            _frameByteIndex = 0;
            _frameBitIndex = 0;
            _bitCount = 0;
            _syncFound = false;
        }
    }
}
