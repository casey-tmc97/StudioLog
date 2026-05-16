using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace StudioLog.Core
{
    /// <summary>
    /// SMPTE LTC Generator using libltc for frame encoding.
    /// libltc produces the correct 80-bit SMPTE 12M frame (bit positions, parity, flags).
    /// This class reads those bits and generates audio via sample-by-sample bi-phase mark encoding,
    /// which is proven to produce audio through NAudio's WaveOutEvent pipeline.
    /// </summary>
    public class LTCGenerator : ISampleProvider
    {
        private readonly int _sampleRate = 48000;
        private double _frameRate = 30.0;
        private float _outputAmplitude = 0.72f;
        
        // libltc encoder handle
        private IntPtr _encoder = IntPtr.Zero;
        private bool _libltcAvailable = false;
        
        // Local copy of the 80-bit frame from libltc
        // sizeof(LTCFrame) in C is 12 bytes (not 10) because the sync_word:16 bitfield
        // sits in a 32-bit unsigned int with 16 bits of padding.
        // We allocate 12 bytes for libltc interop; our GetBit() only reads bits 0-79 (bytes 0-9).
        private byte[] _ltcFrame = new byte[12];
        
        private bool _enabled = false;
        private bool _testToneMode = false;
        private double _testTonePhase = 0.0;
        
        // Bi-phase encoding state (sample-by-sample — proven to output audio)
        private int _bitIndex = 0;
        private double _sampleCount = 0.0;
        private double _samplesPerBit = 20.0;
        private bool _outputState = false;
        
        // Thread-safe timecode transfer
        private volatile int _packedTimecode = 0;
        
        public Action<float[], int>? NDIAudioCallback { get; set; }
        public WaveFormat WaveFormat { get; }

        public LTCGenerator()
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 1);
            CalculateSamplesPerBit();
            InitializeLibLTC();
        }
        
        private void InitializeLibLTC()
        {
            try
            {
                var standard = (_frameRate == 25.0)
                    ? LibLTC.LTC_TV_STANDARD.LTC_TV_625_50
                    : LibLTC.LTC_TV_STANDARD.LTC_TV_525_60;
                
                _encoder = LibLTC.ltc_encoder_create(_sampleRate, _frameRate, standard, 0);
                
                if (_encoder != IntPtr.Zero)
                {
                    LibLTC.ltc_encoder_set_volume(_encoder, -2.0);
                    _libltcAvailable = true;
                    Console.WriteLine($"[LTC] libltc encoder initialized: {_sampleRate}Hz, {_frameRate}fps");
                }
                else
                {
                    Console.WriteLine("[LTC] libltc encoder creation failed — using fallback encoder");
                    _libltcAvailable = false;
                }
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("[LTC] libltc.dll not found — using fallback encoder");
                _libltcAvailable = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LTC] libltc init error: {ex.Message} — using fallback encoder");
                _libltcAvailable = false;
            }
        }

        public void SetFrameRate(double frameRate)
        {
            _frameRate = frameRate;
            CalculateSamplesPerBit();
            
            // Reinitialize libltc with new frame rate
            if (_encoder != IntPtr.Zero)
            {
                LibLTC.ltc_encoder_free(_encoder);
                _encoder = IntPtr.Zero;
            }
            InitializeLibLTC();
        }
        
        public void SetAmplitude(float amplitude)
        {
            _outputAmplitude = Math.Clamp(amplitude, 0.5f, 1.0f);
        }
        
        public void SetTestToneMode(bool enabled)
        {
            _testToneMode = enabled;
            if (enabled) _testTonePhase = 0.0;
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (enabled)
            {
                _bitIndex = 0;
                _sampleCount = 0.0;
                _outputState = false;
                EncodeFrame();
                Console.WriteLine($"[LTC] Enabled: {_frameRate}fps, libltc={_libltcAvailable}");
            }
        }

        private void CalculateSamplesPerBit()
        {
            double bitRate = _frameRate * 80.0;
            _samplesPerBit = _sampleRate / bitRate;
        }

        public void SetTimecode(int hours, int minutes, int seconds, int frames)
        {
            _packedTimecode = (hours << 24) | (minutes << 16) | (seconds << 8) | frames;
        }

        /// <summary>
        /// Encode timecode into 80-bit LTC frame.
        /// Uses libltc if available (guaranteed correct SMPTE 12M), otherwise falls back to C# encoder.
        /// </summary>
        private void EncodeFrame()
        {
            int packed = _packedTimecode;
            int hours = (packed >> 24) & 0xFF;
            int minutes = (packed >> 16) & 0xFF;
            int seconds = (packed >> 8) & 0xFF;
            int frames = packed & 0xFF;
            
            if (_libltcAvailable && _encoder != IntPtr.Zero)
            {
                // Use libltc to encode the frame — correct SMPTE 12M guaranteed
                var smpte = LibLTC.SMPTETimecode.Create(hours, minutes, seconds, frames);
                LibLTC.ltc_encoder_set_timecode(_encoder, ref smpte);
                
                // Read the encoded frame bits back from libltc
                // ltc_encoder_get_frame copies the 10-byte LTCFrame struct
                LibLTC.ltc_encoder_get_frame(_encoder, _ltcFrame);
            }
            else
            {
                // Fallback: manual SMPTE 12M encoding
                EncodeFrameFallback(hours, minutes, seconds, frames);
            }
        }
        
        /// <summary>
        /// Fallback encoder if libltc.dll is not available.
        /// Correct SMPTE 12M interleaved bit layout.
        /// </summary>
        private void EncodeFrameFallback(int hours, int minutes, int seconds, int frames)
        {
            Array.Clear(_ltcFrame, 0, _ltcFrame.Length);

            SetBCD(0, frames % 10, 4);
            SetBCD(8, frames / 10, 2);
            SetBit(10, false); // DF
            SetBit(11, false); // CF
            SetBCD(16, seconds % 10, 4);
            SetBCD(24, seconds / 10, 3);
            SetBCD(32, minutes % 10, 4);
            SetBCD(40, minutes / 10, 3);
            SetBit(43, false); // BGF0
            SetBCD(48, hours % 10, 4);
            SetBCD(56, hours / 10, 2);
            SetBit(58, false); // BGF1
            SetBit(59, false); // BGF2

            // Sync word 0x3FFD LSB-first
            SetBit(64, true);   SetBit(65, false);
            SetBit(66, true);   SetBit(67, true);
            SetBit(68, true);   SetBit(69, true);
            SetBit(70, true);   SetBit(71, true);
            SetBit(72, true);   SetBit(73, true);
            SetBit(74, true);   SetBit(75, true);
            SetBit(76, true);   SetBit(77, true);
            SetBit(78, false);  SetBit(79, false);
            
            // Polarity correction
            int parityBitIndex = (_frameRate == 25.0) ? 59 : 27;
            int zeroCount = 0;
            for (int i = 0; i < 80; i++)
            {
                if (i != parityBitIndex && !GetBit(i))
                    zeroCount++;
            }
            SetBit(parityBitIndex, (zeroCount % 2) != 0);
        }

        private void SetBCD(int startBit, int value, int numBits)
        {
            for (int i = 0; i < numBits; i++)
                SetBit(startBit + i, ((value >> i) & 1) == 1);
        }

        private void SetBit(int bitIndex, bool value)
        {
            int byteIndex = bitIndex / 8;
            int bitInByte = bitIndex % 8;
            if (value)
                _ltcFrame[byteIndex] |= (byte)(1 << bitInByte);
            else
                _ltcFrame[byteIndex] &= (byte)~(1 << bitInByte);
        }

        private bool GetBit(int bitIndex)
        {
            int byteIndex = bitIndex / 8;
            int bitInByte = bitIndex % 8;
            return (_ltcFrame[byteIndex] & (1 << bitInByte)) != 0;
        }

        /// <summary>
        /// Generate LTC audio sample-by-sample using bi-phase mark encoding.
        /// This method is proven to produce audio through NAudio's WaveOutEvent.
        /// The frame data comes from libltc (or fallback encoder).
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            if (!_enabled)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            if (_testToneMode)
            {
                const double testFreq = 1000.0;
                double phaseIncrement = 2.0 * Math.PI * testFreq / _sampleRate;
                for (int i = 0; i < count; i++)
                {
                    buffer[offset + i] = (float)(Math.Sin(_testTonePhase) * _outputAmplitude);
                    _testTonePhase += phaseIncrement;
                    if (_testTonePhase > 2.0 * Math.PI)
                        _testTonePhase -= 2.0 * Math.PI;
                }
                NDIAudioCallback?.Invoke(buffer, count);
                return count;
            }

            for (int i = 0; i < count; i++)
            {
                bool bitValue = GetBit(_bitIndex);
                double halfBit = _samplesPerBit / 2.0;
                
                bool firstHalfLevel = !_outputState;
                bool currentOutput;
                
                if (_sampleCount < halfBit)
                {
                    currentOutput = firstHalfLevel;
                }
                else
                {
                    currentOutput = bitValue ? !firstHalfLevel : firstHalfLevel;
                }
                
                buffer[offset + i] = currentOutput ? _outputAmplitude : -_outputAmplitude;
                
                _sampleCount++;
                
                if (_sampleCount >= _samplesPerBit)
                {
                    _outputState = currentOutput;
                    _sampleCount -= _samplesPerBit;
                    _bitIndex++;
                    
                    if (_bitIndex >= 80)
                    {
                        _bitIndex = 0;
                        EncodeFrame();
                    }
                }
            }
            
            NDIAudioCallback?.Invoke(buffer, count);
            return count;
        }
    }
}
