using System;
using NAudio.Wave;

namespace StudioLog.Core
{
    /// <summary>
    /// LTC Generator using libltc native library (x42/libltc).
    /// This is the production encoder — uses the industry-standard libltc library
    /// proven by Ardour DAW and dozens of professional tools.
    /// </summary>
    public class LTCEncoderLibLTC : ISampleProvider, IDisposable
    {
        private IntPtr _encoder = IntPtr.Zero;
        private byte[] _ltcBuffer = Array.Empty<byte>();        // 8-bit unsigned audio from libltc
        private float[] _floatBuffer = Array.Empty<float>();     // Converted to float for NAudio
        private int _bufferSize;
        private double _sampleRate;
        private double _frameRate;
        
        private int _readPosition = 0;
        private int _availableSamples = 0;
        private bool _enabled = false;
        private bool _firstReadCall = true;
        
        // NDI callback
        public Action<float[], int>? NDIAudioCallback { get; set; }
        
        public WaveFormat WaveFormat { get; }
        
        public LTCEncoderLibLTC()
        {
            _sampleRate = 48000;
            _frameRate = 30.0;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
            
            InitializeEncoder();
        }
        
        private void InitializeEncoder()
        {
            try
            {
                Console.WriteLine("[LibLTC] Attempting to initialize encoder...");
                
                // Determine TV standard based on frame rate
                var standard = (_frameRate == 25.0) 
                    ? LibLTC.LTC_TV_STANDARD.LTC_TV_625_50 
                    : LibLTC.LTC_TV_STANDARD.LTC_TV_525_60;
                
                Console.WriteLine($"[LibLTC] Creating encoder: {_sampleRate}Hz, {_frameRate}fps, standard={standard}");
                
                // Create encoder (flags = 0 for default behavior with parity bit)
                _encoder = LibLTC.ltc_encoder_create(_sampleRate, _frameRate, standard, 0);
                
                if (_encoder == IntPtr.Zero)
                {
                    throw new Exception("libltc encoder creation failed - returned null pointer");
                }
                
                Console.WriteLine("[LibLTC] Encoder created successfully");
                
                // Set volume to -2dBFS (same as our custom implementation: ±0.8)
                LibLTC.ltc_encoder_set_volume(_encoder, -2.0);
                Console.WriteLine("[LibLTC] Volume set to -2.0 dBFS");
                
                // Allocate buffers
                _bufferSize = LibLTC.ltc_encoder_get_buffersize(_encoder);
                _ltcBuffer = new byte[_bufferSize];
                _floatBuffer = new float[_bufferSize];
                
                Console.WriteLine($"[LibLTC] Encoder initialized successfully");
                Console.WriteLine($"[LibLTC] Sample rate: {_sampleRate}Hz");
                Console.WriteLine($"[LibLTC] Frame rate: {_frameRate}fps");
                Console.WriteLine($"[LibLTC] Buffer size: {_bufferSize} samples");
            }
            catch (DllNotFoundException ex)
            {
                Console.WriteLine($"[LibLTC] ERROR: DLL not found!");
                Console.WriteLine($"[LibLTC] Make sure libltc.dll is in the same folder as StudioLog.exe");
                Console.WriteLine($"[LibLTC] Exception: {ex.Message}");
                throw new Exception($"libltc DLL not found. Please place libltc.dll in application directory. Error: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LibLTC] ERROR: Failed to initialize: {ex.Message}");
                Console.WriteLine($"[LibLTC] Stack trace: {ex.StackTrace}");
                throw new Exception($"Failed to initialize libltc encoder: {ex.Message}", ex);
            }
        }
        
        public void SetFrameRate(double frameRate)
        {
            if (Math.Abs(_frameRate - frameRate) < 0.01)
                return; // No change
            
            _frameRate = frameRate;
            
            // Recreate encoder with new frame rate
            if (_encoder != IntPtr.Zero)
            {
                LibLTC.ltc_encoder_free(_encoder);
                _encoder = IntPtr.Zero;
            }
            
            InitializeEncoder();
            Console.WriteLine($"[LibLTC] Frame rate changed to: {_frameRate}fps");
        }
        
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            
            if (enabled)
            {
                Console.WriteLine("[LibLTC] LTC generation enabled");
                _availableSamples = 0;
                _readPosition = 0;
                _firstReadCall = true;
                _firstFrameLogged = false;
                _totalSamplesOutput = 0;
                _testTonePhase = 0.0;
            }
            else
            {
                Console.WriteLine("[LibLTC] LTC generation disabled");
            }
        }
        
        public void SetTime(int hours, int minutes, int seconds, int frames)
        {
            if (_encoder == IntPtr.Zero)
                return;
            
            var smpte = LibLTC.SMPTETimecode.Create(hours, minutes, seconds, frames);
            LibLTC.ltc_encoder_set_timecode(_encoder, ref smpte);
        }
        
        public void SetTimecode(int hours, int minutes, int seconds, int frames)
        {
            SetTime(hours, minutes, seconds, frames);
        }
        
        public void SetAmplitude(float amplitude)
        {
            if (_encoder == IntPtr.Zero) return;
            // Convert linear amplitude (0.5-1.0) to dBFS
            // amplitude 1.0 = 0 dBFS, 0.5 = -6 dBFS
            double dbFS = 20.0 * Math.Log10(Math.Max(amplitude, 0.01));
            LibLTC.ltc_encoder_set_volume(_encoder, dbFS);
        }
        
        private bool _testToneMode = false;
        private double _testTonePhase = 0.0;
        private float _testToneAmplitude = 0.72f;
        
        public void SetTestToneMode(bool enabled)
        {
            _testToneMode = enabled;
            if (enabled) _testTonePhase = 0.0;
        }
        
        private long _totalSamplesOutput = 0;
        
        public int Read(float[] buffer, int offset, int count)
        {
            if (!_enabled || _encoder == IntPtr.Zero)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
            
            // Test tone mode - 1kHz sine wave
            if (_testToneMode)
            {
                const double testFreq = 1000.0;
                double phaseIncrement = 2.0 * Math.PI * testFreq / _sampleRate;
                for (int i = 0; i < count; i++)
                {
                    buffer[offset + i] = (float)(Math.Sin(_testTonePhase) * _testToneAmplitude);
                    _testTonePhase += phaseIncrement;
                    if (_testTonePhase > 2.0 * Math.PI)
                        _testTonePhase -= 2.0 * Math.PI;
                }
                NDIAudioCallback?.Invoke(buffer, count);
                return count;
            }
            
            // DIAGNOSTIC: 3-second test tone to verify this class's audio path works
            if (_totalSamplesOutput < 48000 * 3)
            {
                const double testFreq = 1000.0;
                double phaseIncrement = 2.0 * Math.PI * testFreq / _sampleRate;
                for (int i = 0; i < count; i++)
                {
                    buffer[offset + i] = (float)(Math.Sin(_testTonePhase) * 0.5);
                    _testTonePhase += phaseIncrement;
                    if (_testTonePhase > 2.0 * Math.PI)
                        _testTonePhase -= 2.0 * Math.PI;
                }
                _totalSamplesOutput += count;
                
                if (_firstReadCall)
                {
                    Console.WriteLine("[LibLTC] DIAGNOSTIC: 3-second test tone from LTCEncoderLibLTC.Read()...");
                    _firstReadCall = false;
                }
                
                NDIAudioCallback?.Invoke(buffer, count);
                return count;
            }
            
            // After test tone: LTC from libltc
            int samplesWritten = 0;
            
            while (samplesWritten < count)
            {
                if (_availableSamples == 0)
                {
                    GenerateFrame();
                }
                
                int toCopy = Math.Min(_availableSamples, count - samplesWritten);
                Array.Copy(_floatBuffer, _readPosition, buffer, offset + samplesWritten, toCopy);
                
                _readPosition += toCopy;
                _availableSamples -= toCopy;
                samplesWritten += toCopy;
            }
            
            _totalSamplesOutput += count;
            
            NDIAudioCallback?.Invoke(buffer, count);
            return count;
        }
        
        private bool _firstFrameLogged = false;
        
        private void GenerateFrame()
        {
            // Encode one LTC frame
            int result = LibLTC.ltc_encoder_encode_frame(_encoder);
            
            if (result != 0)
            {
                Console.WriteLine($"[LibLTC] Warning: encode_frame returned {result}");
            }
            
            // Copy encoded samples from libltc's internal buffer
            int size = LibLTC.ltc_encoder_copy_buffer(_encoder, _ltcBuffer, _bufferSize);
            
            if (!_firstFrameLogged)
            {
                Console.WriteLine($"[LibLTC] GenerateFrame: encode_frame={result}, copy_buffer returned {size} bytes (bufferSize={_bufferSize})");
                if (size > 0)
                {
                    // Show first 20 raw byte values from libltc
                    var rawSamples = new System.Text.StringBuilder();
                    for (int j = 0; j < Math.Min(20, size); j++)
                    {
                        rawSamples.Append($"{_ltcBuffer[j]},");
                    }
                    Console.WriteLine($"[LibLTC] Raw bytes [0..19]: {rawSamples}");
                    
                    // Show min/max of raw bytes
                    byte minVal = 255, maxVal = 0;
                    for (int j = 0; j < size; j++)
                    {
                        if (_ltcBuffer[j] < minVal) minVal = _ltcBuffer[j];
                        if (_ltcBuffer[j] > maxVal) maxVal = _ltcBuffer[j];
                    }
                    Console.WriteLine($"[LibLTC] Raw byte range: min={minVal}, max={maxVal} (expect ~38..218 for -2dBFS)");
                }
            }
            
            if (size <= 0)
            {
                Console.WriteLine("[LibLTC] Warning: No samples copied from encoder");
                _availableSamples = 0;
                return;
            }
            
            // Convert 8-bit unsigned to float
            // libltc outputs: 0-255 (unsigned 8-bit), center at 128
            for (int i = 0; i < size; i++)
            {
                _floatBuffer[i] = ((float)_ltcBuffer[i] - 128.0f) / 128.0f;
            }
            
            if (!_firstFrameLogged)
            {
                // Show first few converted float values
                float fMin = float.MaxValue, fMax = float.MinValue;
                for (int j = 0; j < size; j++)
                {
                    if (_floatBuffer[j] < fMin) fMin = _floatBuffer[j];
                    if (_floatBuffer[j] > fMax) fMax = _floatBuffer[j];
                }
                Console.WriteLine($"[LibLTC] Float range: min={fMin:F4}, max={fMax:F4}");
                Console.WriteLine($"[LibLTC] First 10 floats: {_floatBuffer[0]:F4},{_floatBuffer[1]:F4},{_floatBuffer[2]:F4},{_floatBuffer[3]:F4},{_floatBuffer[4]:F4},{_floatBuffer[5]:F4},{_floatBuffer[6]:F4},{_floatBuffer[7]:F4},{_floatBuffer[8]:F4},{_floatBuffer[9]:F4}");
                _firstFrameLogged = true;
            }
            
            _availableSamples = size;
            _readPosition = 0;
            
            // Increment timecode for next frame
            LibLTC.ltc_encoder_inc_timecode(_encoder);
        }
        
        public void Dispose()
        {
            if (_encoder != IntPtr.Zero)
            {
                LibLTC.ltc_encoder_free(_encoder);
                _encoder = IntPtr.Zero;
                Console.WriteLine("[LibLTC] Encoder disposed");
            }
        }
    }
}
