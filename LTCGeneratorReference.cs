using System;
using NAudio.Wave;

namespace StudioLog.Core
{
    /// <summary>
    /// DEPRECATED: Reference LTC Generator - Not used in production.
    /// The active encoder is LTCGenerator.cs (used by LTCAudioManager).
    /// 
    /// WARNING: This class has incorrect BCD bit mapping (see Fix #18) —
    /// frame tens are placed at bit 8 (user bits) instead of bit 4.
    /// Do not activate without fixing EncodeFrame().
    /// 
    /// Kept for reference only. Consider removing from the build.
    /// </summary>
    [Obsolete("Not used in production. See LTCGenerator.cs for the active encoder.")]
    public class LTCGeneratorReference : ISampleProvider, IDisposable
    {
        private readonly int _sampleRate = 48000;
        private double _frameRate = 30.0;
        
        // LTC frame (80 bits)
        private byte[] _frame = new byte[10];  // 80 bits = 10 bytes
        
        // Encoder state
        private bool _enabled = false;
        private int _hours = 0;
        private int _minutes = 0;
        private int _seconds = 0;
        private int _frames = 0;
        
        // Audio buffer for one complete LTC frame
        private float[] _audioBuffer;
        private int _audioBufferSize;
        private int _audioReadPos = 0;
        
        public Action<float[], int>? NDIAudioCallback { get; set; }
        public WaveFormat WaveFormat { get; }
        
        public LTCGeneratorReference()
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 1);
            
            // Calculate buffer size for one frame
            // At 30fps, one frame = 1/30 second = 48000/30 = 1600 samples
            // Each LTC frame has 80 bits, each bit needs samples
            _audioBufferSize = (int)(_sampleRate / _frameRate);
            _audioBuffer = new float[_audioBufferSize];
            
            EncodeFrame();
        }
        
        public void SetFrameRate(double frameRate)
        {
            _frameRate = frameRate;
            _audioBufferSize = (int)(_sampleRate / _frameRate);
            _audioBuffer = new float[_audioBufferSize];
        }
        
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (enabled)
            {
                _audioReadPos = 0;
                Console.WriteLine("[LTC-Ref] Encoder enabled");
                Console.WriteLine($"[LTC-Ref] Sample rate: {_sampleRate}, Frame rate: {_frameRate}");
                Console.WriteLine($"[LTC-Ref] Audio buffer size: {_audioBufferSize} samples");
                Console.WriteLine($"[LTC-Ref] Starting timecode: {_hours:D2}:{_minutes:D2}:{_seconds:D2}:{_frames:D2}");
            }
            else
            {
                Console.WriteLine("[LTC-Ref] Encoder disabled");
            }
        }
        
        public void SetTime(int hours, int minutes, int seconds, int frames)
        {
            _hours = hours;
            _minutes = minutes;
            _seconds = seconds;
            _frames = frames;
        }
        
        public void SetTimecode(int hours, int minutes, int seconds, int frames)
        {
            SetTime(hours, minutes, seconds, frames);
        }
        
        private void EncodeFrame()
        {
            // Clear frame
            Array.Clear(_frame, 0, 10);
            
            // Encode timecode in BCD, LSB first per SMPTE 12M standard bit positions.
            // NOTE: This file uses the correct SMPTE 12M bit assignments (units at 0/16/32/48,
            // tens at 8/24/40/56). The active LTCGenerator.cs uses a simplified layout
            // (tens immediately after units) which has been validated with hardware decoders.
            // SetBCDBits writes 4 bits but the max BCD values (0-2 for frame/hour tens,
            // 0-5 for sec/min tens) never set the upper bits, so flag bits are not affected.
            SetBCDBits(0, _frames % 10);      // Frame units (bits 0-3)
            SetBCDBits(8, _frames / 10);      // Frame tens (bits 8-9, 2 bits used)
            
            SetBCDBits(16, _seconds % 10);    // Second units (bits 16-19)
            SetBCDBits(24, _seconds / 10);    // Second tens (bits 24-26, 3 bits used)
            
            SetBCDBits(32, _minutes % 10);    // Minute units (bits 32-35)
            SetBCDBits(40, _minutes / 10);    // Minute tens (bits 40-42, 3 bits used)
            
            SetBCDBits(48, _hours % 10);      // Hour units (bits 48-51)
            SetBCDBits(56, _hours / 10);      // Hour tens (bits 56-57, 2 bits used)
            
            // User bits (all zeros)
            // Bits 4-7, 12-15, 20-23, 28-31, 36-39, 44-47, 52-55, 60-63
            
            // Drop frame flag (bit 10)
            SetBit(10, false);
            
            // Color frame flag (bit 11)
            SetBit(11, false);
            
            // Polarity correction bit (bit 27 for 30fps, bit 59 for 25fps)
            int parityBit = (_frameRate == 25.0) ? 59 : 27;
            
            // BGF bits (43, 58, 59 for 30fps)
            SetBit(43, false);  // BGF0
            SetBit(58, false);  // BGF1
            
            // Sync word (bits 64-79): 0x3FFD = 0011111111111101
            // LSB first: 1011111111111100
            SetBit(64, true);   SetBit(65, false);
            SetBit(66, true);   SetBit(67, true);
            SetBit(68, true);   SetBit(69, true);
            SetBit(70, true);   SetBit(71, true);
            SetBit(72, true);   SetBit(73, true);
            SetBit(74, true);   SetBit(75, true);
            SetBit(76, true);   SetBit(77, true);
            SetBit(78, false);  SetBit(79, false);
            
            // Calculate and set parity bit
            int zeroCount = 0;
            for (int i = 0; i < 80; i++)
            {
                if (i != parityBit && !GetBit(i))
                    zeroCount++;
            }
            SetBit(parityBit, (zeroCount % 2) == 0);
            
            // Generate audio waveform using bi-phase mark encoding
            GenerateAudioWaveform();
            
            // Diagnostic output
            if (_frames == 0)
            {
                Console.WriteLine($"[LTC-Ref] {_hours:D2}:{_minutes:D2}:{_seconds:D2}:{_frames:D2}");
                Console.WriteLine($"[LTC-Ref] Frame: {BitConverter.ToString(_frame)}");
            }
        }
        
        private void GenerateAudioWaveform()
        {
            int samplesPerBit = _audioBufferSize / 80;
            float amplitude = 0.8f;
            
            bool state = false;  // Start low
            int sampleIndex = 0;
            
            for (int bit = 0; bit < 80; bit++)
            {
                bool bitValue = GetBit(bit);
                
                // Bi-phase mark encoding:
                // Bit 0: transition at start only
                // Bit 1: transitions at start AND middle
                
                int samplesThisBit = samplesPerBit;
                if (bit == 79)
                    samplesThisBit = _audioBufferSize - sampleIndex;
                
                int halfBit = samplesThisBit / 2;
                
                // Transition at start of bit
                state = !state;
                
                // First half
                for (int i = 0; i < halfBit && sampleIndex < _audioBufferSize; i++)
                {
                    _audioBuffer[sampleIndex++] = state ? amplitude : -amplitude;
                }
                
                // If bit is 1, transition at middle
                if (bitValue)
                {
                    state = !state;
                }
                
                // Second half
                for (int i = 0; i < (samplesThisBit - halfBit) && sampleIndex < _audioBufferSize; i++)
                {
                    _audioBuffer[sampleIndex++] = state ? amplitude : -amplitude;
                }
            }
        }
        
        private void SetBCDBits(int startBit, int value)
        {
            SetBit(startBit + 0, (value & 1) != 0);
            SetBit(startBit + 1, (value & 2) != 0);
            SetBit(startBit + 2, (value & 4) != 0);
            SetBit(startBit + 3, (value & 8) != 0);
        }
        
        private void SetBit(int bitIndex, bool value)
        {
            int byteIndex = bitIndex / 8;
            int bitOffset = bitIndex % 8;
            
            if (value)
                _frame[byteIndex] |= (byte)(1 << bitOffset);
            else
                _frame[byteIndex] &= (byte)~(1 << bitOffset);
        }
        
        private bool GetBit(int bitIndex)
        {
            int byteIndex = bitIndex / 8;
            int bitOffset = bitIndex % 8;
            return (_frame[byteIndex] & (1 << bitOffset)) != 0;
        }
        
        private bool _firstReadCall = true;
        
        public int Read(float[] buffer, int offset, int count)
        {
            if (!_enabled)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
            
            int samplesWritten = 0;
            
            // Diagnostic: Log first time we generate audio
            if (_firstReadCall)
            {
                Console.WriteLine($"[LTC-Ref] Read() called: buffer size={count}, audio buffer={_audioBufferSize}");
                _firstReadCall = false;
            }
            
            while (samplesWritten < count)
            {
                // If we've read entire audio buffer, generate next frame
                if (_audioReadPos >= _audioBufferSize)
                {
                    IncrementTimecode();
                    EncodeFrame();
                    _audioReadPos = 0;
                }
                
                // Copy from audio buffer
                int toCopy = Math.Min(count - samplesWritten, _audioBufferSize - _audioReadPos);
                Array.Copy(_audioBuffer, _audioReadPos, buffer, offset + samplesWritten, toCopy);
                
                _audioReadPos += toCopy;
                samplesWritten += toCopy;
            }
            
            // Send to NDI
            NDIAudioCallback?.Invoke(buffer, count);
            
            return count;
        }
        
        private void IncrementTimecode()
        {
            _frames++;
            
            int maxFrames = (int)Math.Round(_frameRate);
            if (_frames >= maxFrames)
            {
                _frames = 0;
                _seconds++;
                
                if (_seconds >= 60)
                {
                    _seconds = 0;
                    _minutes++;
                    
                    if (_minutes >= 60)
                    {
                        _minutes = 0;
                        _hours++;
                        
                        if (_hours >= 24)
                        {
                            _hours = 0;
                        }
                    }
                }
            }
        }
        
        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
