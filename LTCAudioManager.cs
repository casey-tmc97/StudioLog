using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace StudioLog.Core
{
    public class LTCAudioManager : IDisposable
    {
        private IWavePlayer? _waveOut;
        private IWaveIn? _waveIn;
        private LTCGenerator? _ltcGenerator;
        private BufferedWaveProvider? _inputBuffer;
        private bool _isPlaying = false;
        private string _audioOutput = "System Default";
        private string _audioInput = "None";
        private bool _isPassthroughMode = false;
        private string? _selectedNdiSource = null;
        
        // NDI support - use shared singleton instance
        private static NDIManager? _sharedNdiManager;
        private static readonly object _ndiLock = new object();
        private bool _ndiOutputEnabled = false;
        private bool _ndiInputEnabled = false;
        
        // Public properties
        public bool IsNDIAvailable => _sharedNdiManager?.IsNDIAvailable ?? false;
        public bool IsNDIOutputEnabled => _ndiOutputEnabled;
        public bool IsNDIInputEnabled => _ndiInputEnabled;
        public NDIManager? NDI => _sharedNdiManager;
        public bool IsPassthroughMode => _isPassthroughMode;

        public void Initialize(double frameRate, string audioOutput, string audioInput = "None", string? ndiSource = null)
        {
            Stop();

            _audioOutput = audioOutput;
            _audioInput = audioInput;
            _isPassthroughMode = audioInput != "None";
            
            // Store NDI source for later use
            _selectedNdiSource = ndiSource;
            
            // Initialize shared NDI manager (singleton)
            lock (_ndiLock)
            {
                if (_sharedNdiManager == null)
                {
                    _sharedNdiManager = new NDIManager();
                }
            }
            
            if (!_isPassthroughMode)
            {
                // Generator mode - create LTC generator
                _ltcGenerator = new LTCGenerator();
                _ltcGenerator.SetFrameRate(frameRate);
                
                // Hook up NDI audio callback for generator
                if (_ltcGenerator != null && _sharedNdiManager != null)
                {
                    _ltcGenerator.NDIAudioCallback = (buffer, count) =>
                    {
                        if (_ndiOutputEnabled)
                        {
                            _sharedNdiManager.SendAudio(buffer, count);
                        }
                    };
                }
            }
            else
            {
                // Passthrough mode - create input buffer for routing
                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
                _inputBuffer = new BufferedWaveProvider(waveFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(500),
                    DiscardOnBufferOverflow = true
                };
            }
        }

        private void InitializeInput()
        {
            try
            {
                if (_audioInput == "ASIO")
                {
                    // TODO: NAudio doesn't have built-in ASIO input like AsioOut
                    // For now, fallback to System Default input
                    _waveIn = new WaveInEvent
                    {
                        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1),
                        BufferMilliseconds = 50
                    };
                }
                else if (_audioInput == "NDI Receive")
                {
                    // NDI receive - will be handled separately
                    _ndiInputEnabled = true;
                    SetupNDIReceive();
                }
                else if (_audioInput != "None")
                {
                    // System Default input
                    _waveIn = new WaveInEvent
                    {
                        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1),
                        BufferMilliseconds = 50
                    };
                }

                // Hook up audio input to buffer
                if (_waveIn != null && _inputBuffer != null)
                {
                    _waveIn.DataAvailable += (sender, args) =>
                    {
                        // Write input data to buffer for passthrough
                        _inputBuffer.AddSamples(args.Buffer, 0, args.BytesRecorded);
                        
                        // Also send to NDI if output enabled
                        if (_ndiOutputEnabled && _sharedNdiManager != null)
                        {
                            // Convert bytes to float array for NDI
                            int sampleCount = args.BytesRecorded / 4; // 4 bytes per float
                            float[] samples = new float[sampleCount];
                            Buffer.BlockCopy(args.Buffer, 0, samples, 0, args.BytesRecorded);
                            _sharedNdiManager.SendAudio(samples, sampleCount);
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio] Input initialization error: {ex.Message}");
                _waveIn?.Dispose();
                _waveIn = null;
            }
        }

        private void SetupNDIReceive()
        {
            if (_sharedNdiManager == null || _inputBuffer == null) return;

            // Subscribe to NDI audio receive event
            _sharedNdiManager.AudioReceived += OnNDIAudioReceived;
            
            // Determine which source to use
            string? sourceToUse = null;
            
            if (!string.IsNullOrEmpty(_selectedNdiSource))
            {
                // Use the specifically selected source
                sourceToUse = _selectedNdiSource;
            }
            else if (_sharedNdiManager.DiscoveredSources.Count > 0)
            {
                // Use first available source as fallback
                sourceToUse = _sharedNdiManager.DiscoveredSources[0];
            }
            
            if (!string.IsNullOrEmpty(sourceToUse))
            {
                _sharedNdiManager.StartReceiving(sourceToUse);
            }
        }
        
        private void OnNDIAudioReceived(float[] audioData, int sampleRate)
        {
            if (_inputBuffer != null && audioData != null)
            {
                // Convert float array to bytes
                byte[] bytes = new byte[audioData.Length * 4];
                Buffer.BlockCopy(audioData, 0, bytes, 0, bytes.Length);
                _inputBuffer.AddSamples(bytes, 0, bytes.Length);
            }
        }

        public void Start()
        {
            if (_isPlaying) return;

            try
            {
                Console.WriteLine($"[Audio] Start() called - output='{_audioOutput}', input='{_audioInput}', passthrough={_isPassthroughMode}");
                Console.WriteLine($"[Audio] _ltcGenerator is {(_ltcGenerator == null ? "NULL" : "valid")}");
                
                // Initialize input if in passthrough mode
                if (_isPassthroughMode)
                {
                    InitializeInput();
                }
                
                // Initialize output
                if (_audioOutput == "ASIO")
                {
                    try
                    {
                        var asioOut = new AsioOut();
                        _waveOut = asioOut;
                        Console.WriteLine("[Audio] Created ASIO output");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Audio] ASIO init failed, falling back to WaveOut: {ex.Message}");
                        _waveOut = new WaveOutEvent();
                    }
                }
                else if (_audioOutput != "None")
                {
                    _waveOut = new WaveOutEvent
                    {
                        DesiredLatency = 50,
                        NumberOfBuffers = 3
                    };
                    Console.WriteLine("[Audio] Created WaveOutEvent");
                }
                else
                {
                    Console.WriteLine("[Audio] Output is 'None' - no audio device created!");
                }

                // Init output with appropriate source
                if (_waveOut != null)
                {
                    if (_isPassthroughMode && _inputBuffer != null)
                    {
                        _waveOut.Init(_inputBuffer);
                        Console.WriteLine("[Audio] Init with passthrough buffer");
                    }
                    else if (_ltcGenerator != null)
                    {
                        var waveProvider = _ltcGenerator.ToWaveProvider();
                        _waveOut.Init(waveProvider);
                        Console.WriteLine($"[Audio] Init with LTC generator (WaveFormat: {_ltcGenerator.WaveFormat})");
                    }
                    else
                    {
                        Console.WriteLine("[Audio] WARNING: _waveOut created but no source to init with!");
                    }

                    // Start output
                    _waveOut.Play();
                    Console.WriteLine("[Audio] Play() called");
                }
                else
                {
                    Console.WriteLine("[Audio] WARNING: _waveOut is null - nothing to play!");
                }

                // Start input if in passthrough mode
                if (_isPassthroughMode && _waveIn != null)
                {
                    _waveIn.StartRecording();
                }

                // Enable generator if in generator mode
                if (!_isPassthroughMode)
                {
                    _ltcGenerator?.SetEnabled(true);
                    Console.WriteLine("[Audio] Generator enabled");
                }

                _isPlaying = true;
                Console.WriteLine("[Audio] Start() complete - _isPlaying=true");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio] Start error: {ex.Message}");
                Console.WriteLine($"[Audio] Stack trace: {ex.StackTrace}");
                _waveOut?.Dispose();
                _waveOut = null;
                _waveIn?.Dispose();
                _waveIn = null;
            }
        }

        public void Stop()
        {
            if (_waveOut != null && _isPlaying)
            {
                _ltcGenerator?.SetEnabled(false);
                _waveOut.Stop();
                _waveIn?.StopRecording();
                _isPlaying = false;
            }
        }

        public void EnableNDIOutput(bool enable)
        {
            if (_sharedNdiManager == null) return;

            if (enable && !_ndiOutputEnabled)
            {
                if (_sharedNdiManager.EnableSend())
                {
                    _ndiOutputEnabled = true;
                }
            }
            else if (!enable && _ndiOutputEnabled)
            {
                _sharedNdiManager.DisableSend();
                _ndiOutputEnabled = false;
            }
        }

        public void Dispose()
        {
            Stop();
            
            // Cleanup NDI receive if enabled
            if (_ndiInputEnabled && _sharedNdiManager != null)
            {
                _sharedNdiManager.AudioReceived -= OnNDIAudioReceived;
                _sharedNdiManager.StopReceiving();
                _ndiInputEnabled = false;
            }
            
            _waveOut?.Dispose();
            _waveOut = null;
            _waveIn?.Dispose();
            _waveIn = null;
            _ltcGenerator = null;
            _inputBuffer = null;
        }
        
        /// <summary>
        /// Dispose the shared NDI manager singleton. Call once at app shutdown.
        /// </summary>
        public static void DisposeSharedNDI()
        {
            lock (_ndiLock)
            {
                _sharedNdiManager?.Dispose();
                _sharedNdiManager = null;
            }
        }

        // Timecode forwarding methods
        public void SetTime(int hours, int minutes, int seconds, int frames)
        {
            _ltcGenerator?.SetTimecode(hours, minutes, seconds, frames);
        }

        public void SetFrameRate(double frameRate)
        {
            _ltcGenerator?.SetFrameRate(frameRate);
        }

        public void SetAmplitude(float amplitude)
        {
            _ltcGenerator?.SetAmplitude(amplitude);
        }

        public void SetTestTone(bool enabled)
        {
            _ltcGenerator?.SetTestToneMode(enabled);
        }
    }
}
