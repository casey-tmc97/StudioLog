using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace StudioLog.Core
{
    /// <summary>
    /// NDI Manager for sending and receiving audio (LTC timecode)
    /// Requires NDI Runtime to be installed on the system
    /// </summary>
    public class NDIManager : IDisposable
    {
        private IntPtr _sendInstance = IntPtr.Zero;
        private IntPtr _recvInstance = IntPtr.Zero;
        private IntPtr _findInstance = IntPtr.Zero;
        private bool _isInitialized = false;
        private bool _isSending = false;
        private bool _isReceiving = false;
        private CancellationTokenSource? _receiveCts;
        private Task? _receiveTask;
        private CancellationTokenSource? _discoveryCts;
        private Task? _discoveryTask;
        
        private const int SAMPLE_RATE = 48000;
        private const int NUM_CHANNELS = 1;
        private const string NDI_SOURCE_NAME = "StudioLog LTC";
        
        // Callback for received audio data (LTC to decode)
        public event Action<float[], int>? AudioReceived;
        
        // List of discovered NDI sources
        public List<string> DiscoveredSources { get; private set; } = new List<string>();
        public event Action? SourcesUpdated;
        
        // Current receive source
        private string? _currentReceiveSource;
        
        // Dynamic library handle
        private static IntPtr _ndiLibHandle = IntPtr.Zero;
        private static string? _loadedLibraryVersion = null;
        private static int _instanceCount = 0;
        private static readonly object _instanceLock = new object();
        
        #region NDI Native Interop - Dynamic Loading
        
        // Windows DLL loading functions
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);
        
        // NDI function delegates
        private delegate bool NDIlib_initialize_delegate();
        private delegate void NDIlib_destroy_delegate();
        private delegate IntPtr NDIlib_send_create_delegate(ref NDIlib_send_create_t createSettings);
        private delegate void NDIlib_send_destroy_delegate(IntPtr instance);
        private delegate void NDIlib_send_send_audio_v2_delegate(IntPtr instance, ref NDIlib_audio_frame_v2_t audioFrame);
        private delegate IntPtr NDIlib_find_create_v2_delegate(ref NDIlib_find_create_t createSettings);
        private delegate void NDIlib_find_destroy_delegate(IntPtr instance);
        private delegate bool NDIlib_find_wait_for_sources_delegate(IntPtr instance, uint timeout_ms);
        private delegate IntPtr NDIlib_find_get_current_sources_delegate(IntPtr instance, out uint numSources);
        private delegate IntPtr NDIlib_recv_create_v3_delegate(ref NDIlib_recv_create_v3_t createSettings);
        private delegate void NDIlib_recv_destroy_delegate(IntPtr instance);
        private delegate int NDIlib_recv_capture_v2_delegate(IntPtr instance, IntPtr videoFrame, ref NDIlib_audio_frame_v2_t audioFrame, IntPtr metadata, uint timeout_ms);
        private delegate void NDIlib_recv_free_audio_v2_delegate(IntPtr instance, ref NDIlib_audio_frame_v2_t audioFrame);
        
        // Function pointers
        private static NDIlib_initialize_delegate? NDIlib_initialize;
        private static NDIlib_destroy_delegate? NDIlib_destroy;
        private static NDIlib_send_create_delegate? NDIlib_send_create;
        private static NDIlib_send_destroy_delegate? NDIlib_send_destroy;
        private static NDIlib_send_send_audio_v2_delegate? NDIlib_send_send_audio_v2;
        private static NDIlib_find_create_v2_delegate? NDIlib_find_create_v2;
        private static NDIlib_find_destroy_delegate? NDIlib_find_destroy;
        private static NDIlib_find_wait_for_sources_delegate? NDIlib_find_wait_for_sources;
        private static NDIlib_find_get_current_sources_delegate? NDIlib_find_get_current_sources;
        private static NDIlib_recv_create_v3_delegate? NDIlib_recv_create_v3;
        private static NDIlib_recv_destroy_delegate? NDIlib_recv_destroy;
        private static NDIlib_recv_capture_v2_delegate? NDIlib_recv_capture_v2;
        private static NDIlib_recv_free_audio_v2_delegate? NDIlib_recv_free_audio_v2;
        
        // NDI Structures
        [StructLayout(LayoutKind.Sequential)]
        private struct NDIlib_send_create_t
        {
            public IntPtr p_ndi_name;
            public IntPtr p_groups;
            [MarshalAs(UnmanagedType.U1)]
            public bool clock_video;
            [MarshalAs(UnmanagedType.U1)]
            public bool clock_audio;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct NDIlib_audio_frame_v2_t
        {
            public int sample_rate;
            public int no_channels;
            public int no_samples;
            public long timecode;
            public IntPtr p_data;
            public int channel_stride_in_bytes;
            public IntPtr p_metadata;
            public long timestamp;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct NDIlib_find_create_t
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool show_local_sources;
            public IntPtr p_groups;
            public IntPtr p_extra_ips;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct NDIlib_source_t
        {
            public IntPtr p_ndi_name;
            public IntPtr p_url_address;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct NDIlib_recv_create_v3_t
        {
            public NDIlib_source_t source_to_connect_to;
            public int color_format;
            public int bandwidth;
            [MarshalAs(UnmanagedType.U1)]
            public bool allow_video_fields;
            public IntPtr p_ndi_recv_name;
        }
        
        private const int NDIlib_frame_type_none = 0;
        private const int NDIlib_frame_type_video = 1;
        private const int NDIlib_frame_type_audio = 2;
        private const int NDIlib_frame_type_metadata = 3;
        private const int NDIlib_frame_type_error = 4;
        
        private const int NDIlib_recv_bandwidth_audio_only = 10;
        
        #endregion
        
        public bool IsNDIAvailable { get; private set; }
        public bool IsSending => _isSending;
        public bool IsSendEnabled => _isSending;
        public bool IsReceiving => _isReceiving;
        
        public NDIManager()
        {
            lock (_instanceLock)
            {
                _instanceCount++;
            }
            Initialize();
        }
        
        private static bool LoadNDILibrary()
        {
            if (_ndiLibHandle != IntPtr.Zero)
                return true; // Already loaded
            
            // Console.WriteLine("[NDI] Attempting to load NDI library...");
            
            var searchPaths = new List<string>();
            
            // Try simple names first (in case it's in PATH or app directory)
            searchPaths.Add("Processing.NDI.Lib.x64");
            searchPaths.Add("Processing.NDI.Lib.x64.dll");
            
            // Search for all NDI installations
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string ndiBaseDir = Path.Combine(programFiles, "NDI");
            
            if (Directory.Exists(ndiBaseDir))
            {
                // Console.WriteLine($"[NDI] Searching for NDI installations in: {ndiBaseDir}");
                
                // Search pattern: NDI [version] Runtime/Tools -> Runtime -> v[x] -> Processing.NDI.Lib.x64.dll
                // Examples:
                //   NDI 6 Runtime\v6\
                //   NDI 6 Tools\Runtime\
                //   NDI 5 Tools\Runtime\v5\
                //   NDI 7 Runtime\v7\
                
                try
                {
                    // Get all NDI version directories (NDI 5 Tools, NDI 6 Runtime, etc.)
                    var ndiDirs = Directory.GetDirectories(ndiBaseDir, "NDI*");
                    
                    foreach (var ndiDir in ndiDirs.OrderByDescending(d => d)) // Try newer versions first
                    {
                        string dirName = Path.GetFileName(ndiDir);
                        // Console.WriteLine($"[NDI] Found NDI installation: {dirName}");
                        
                        // Check common subdirectories
                        var possiblePaths = new[]
                        {
                            Path.Combine(ndiDir, "Runtime", "Processing.NDI.Lib.x64.dll"),           // NDI 6 Tools\Runtime\
                            Path.Combine(ndiDir, "v6", "Processing.NDI.Lib.x64.dll"),                // NDI 6 Runtime\v6\
                            Path.Combine(ndiDir, "v7", "Processing.NDI.Lib.x64.dll"),                // Future: NDI 7 Runtime\v7\
                            Path.Combine(ndiDir, "v8", "Processing.NDI.Lib.x64.dll"),                // Future: NDI 8 Runtime\v8\
                            Path.Combine(ndiDir, "Runtime", "v5", "Processing.NDI.Lib.x64.dll"),     // NDI 5 Tools\Runtime\v5\
                            Path.Combine(ndiDir, "Runtime", "v6", "Processing.NDI.Lib.x64.dll"),     // NDI 6 Tools\Runtime\v6\
                            Path.Combine(ndiDir, "Runtime", "v7", "Processing.NDI.Lib.x64.dll"),     // Future: NDI 7 Tools\Runtime\v7\
                        };
                        
                        foreach (var path in possiblePaths)
                        {
                            if (File.Exists(path) && !searchPaths.Contains(path))
                            {
                                searchPaths.Add(path);
                                // Console.WriteLine($"[NDI] Found DLL: {path}");
                            }
                        }
                        
                        // Also search recursively for any Processing.NDI.Lib.x64.dll in this NDI installation
                        try
                        {
                            var foundDlls = Directory.GetFiles(ndiDir, "Processing.NDI.Lib.x64.dll", SearchOption.AllDirectories);
                            foreach (var dll in foundDlls)
                            {
                                if (!searchPaths.Contains(dll))
                                {
                                    searchPaths.Add(dll);
                                    // Console.WriteLine($"[NDI] Found DLL (recursive search): {dll}");
                                }
                            }
                        }
                        catch
                        {
                            // Ignore access errors during recursive search
                        }
                    }
                }
                catch
                {
                    // Console.WriteLine($"[NDI] Error searching NDI directory: {ex.Message}");
                }
            }
            
            // Try loading each path
            foreach (var libPath in searchPaths)
            {
                // Console.WriteLine($"[NDI] Trying to load: {libPath}");
                _ndiLibHandle = LoadLibrary(libPath);
                
                if (_ndiLibHandle != IntPtr.Zero)
                {
                    _loadedLibraryVersion = libPath;
                    // Console.WriteLine($"[NDI] ✓ Successfully loaded library: {libPath}");
                    
                    // Load all function pointers
                    try
                    {
                        // Console.WriteLine("[NDI] Loading function pointers...");
                        NDIlib_initialize = GetDelegate<NDIlib_initialize_delegate>(_ndiLibHandle, "NDIlib_initialize");
                        NDIlib_destroy = GetDelegate<NDIlib_destroy_delegate>(_ndiLibHandle, "NDIlib_destroy");
                        NDIlib_send_create = GetDelegate<NDIlib_send_create_delegate>(_ndiLibHandle, "NDIlib_send_create");
                        NDIlib_send_destroy = GetDelegate<NDIlib_send_destroy_delegate>(_ndiLibHandle, "NDIlib_send_destroy");
                        NDIlib_send_send_audio_v2 = GetDelegate<NDIlib_send_send_audio_v2_delegate>(_ndiLibHandle, "NDIlib_send_send_audio_v2");
                        NDIlib_find_create_v2 = GetDelegate<NDIlib_find_create_v2_delegate>(_ndiLibHandle, "NDIlib_find_create_v2");
                        NDIlib_find_destroy = GetDelegate<NDIlib_find_destroy_delegate>(_ndiLibHandle, "NDIlib_find_destroy");
                        NDIlib_find_wait_for_sources = GetDelegate<NDIlib_find_wait_for_sources_delegate>(_ndiLibHandle, "NDIlib_find_wait_for_sources");
                        NDIlib_find_get_current_sources = GetDelegate<NDIlib_find_get_current_sources_delegate>(_ndiLibHandle, "NDIlib_find_get_current_sources");
                        NDIlib_recv_create_v3 = GetDelegate<NDIlib_recv_create_v3_delegate>(_ndiLibHandle, "NDIlib_recv_create_v3");
                        NDIlib_recv_destroy = GetDelegate<NDIlib_recv_destroy_delegate>(_ndiLibHandle, "NDIlib_recv_destroy");
                        NDIlib_recv_capture_v2 = GetDelegate<NDIlib_recv_capture_v2_delegate>(_ndiLibHandle, "NDIlib_recv_capture_v2");
                        NDIlib_recv_free_audio_v2 = GetDelegate<NDIlib_recv_free_audio_v2_delegate>(_ndiLibHandle, "NDIlib_recv_free_audio_v2");
                        
                        // Console.WriteLine("[NDI] ✓ All function pointers loaded successfully");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[NDI] Failed to load function pointers from {libPath}: {ex.Message}");
                        FreeLibrary(_ndiLibHandle);
                        _ndiLibHandle = IntPtr.Zero;
                    }
                }
                else
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != 0)
                    {
                        // Console.WriteLine($"[NDI] ✗ Failed (error {errorCode})");
                    }
                }
            }
            
            // Library not found - provide helpful diagnostic info
            // Console.WriteLine("[NDI] ========================================");
            // Console.WriteLine("[NDI] ✗ NDI DLL not found");
            // Console.WriteLine("[NDI] ========================================");
            // Console.WriteLine($"[NDI] Searched {searchPaths.Count} location(s)");
            // Console.WriteLine("[NDI] ========================================");
            // Console.WriteLine("[NDI] QUICK FIX:");
            // Console.WriteLine("[NDI] 1. Find Processing.NDI.Lib.x64.dll in your NDI installation");
            // Console.WriteLine("[NDI] 2. Copy it to your StudioLog app directory");
            // Console.WriteLine("[NDI] 3. Restart the app");
            // Console.WriteLine("[NDI] ========================================");
            
            return false;
        }
        
        private static T GetDelegate<T>(IntPtr library, string functionName) where T : Delegate
        {
            IntPtr funcPtr = GetProcAddress(library, functionName);
            if (funcPtr == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException($"Function '{functionName}' not found in NDI library");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
        }
        
        private void Initialize()
        {
            try
            {
                // Try to load NDI library first
                if (!LoadNDILibrary())
                {
                    // Console.WriteLine("[NDI] Failed to load NDI library - NDI Runtime may not be installed");
                    // Console.WriteLine("[NDI] Please install NDI Tools v5 or later from https://ndi.tv/tools/");
                    IsNDIAvailable = false;
                    return;
                }
                
                if (NDIlib_initialize == null)
                {
                    // Console.WriteLine("[NDI] NDI functions not loaded");
                    IsNDIAvailable = false;
                    return;
                }
                
                _isInitialized = NDIlib_initialize();
                IsNDIAvailable = _isInitialized;
                
                if (_isInitialized)
                {
                    // Console.WriteLine($"[NDI] Initialized successfully (using {_loadedLibraryVersion})");
                    // Console.WriteLine("[NDI] Compatible with NDI v5 and later");
                    StartSourceDiscovery();
                }
                else
                {
                    // Console.WriteLine("[NDI] Failed to initialize - NDI Runtime may not be installed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NDI] Initialization error: {ex.Message}");
                IsNDIAvailable = false;
            }
        }
        
        #region NDI Send
        
        public bool EnableSend()
        {
            if (!_isInitialized || NDIlib_send_create == null)
                return false;
            
            // Already sending - that's success
            if (_isSending)
            {
                // Console.WriteLine("[NDI] Send already enabled");
                return true;
            }
            
            try
            {
                var namePtr = Marshal.StringToHGlobalAnsi(NDI_SOURCE_NAME);
                
                var createSettings = new NDIlib_send_create_t
                {
                    p_ndi_name = namePtr,
                    p_groups = IntPtr.Zero,
                    clock_video = false,
                    clock_audio = true
                };
                
                _sendInstance = NDIlib_send_create(ref createSettings);
                Marshal.FreeHGlobal(namePtr);
                
                if (_sendInstance != IntPtr.Zero)
                {
                    _isSending = true;
                    // Console.WriteLine($"[NDI] Send enabled - Source name: '{NDI_SOURCE_NAME}'");
                    return true;
                }
                else
                {
                    // Console.WriteLine("[NDI] Failed to create send instance");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NDI] Enable send error: {ex.Message}");
                return false;
            }
        }
        
        public void DisableSend()
        {
            if (_sendInstance != IntPtr.Zero && NDIlib_send_destroy != null)
            {
                NDIlib_send_destroy(_sendInstance);
                _sendInstance = IntPtr.Zero;
                _isSending = false;
                // Console.WriteLine("[NDI] Send disabled");
            }
        }
        
        /// <summary>
        /// Send audio samples over NDI (called from audio callback)
        /// </summary>
        public void SendAudio(float[] samples, int sampleCount)
        {
            if (!_isSending || _sendInstance == IntPtr.Zero || NDIlib_send_send_audio_v2 == null)
                return;
            
            IntPtr dataPtr = IntPtr.Zero;
            try
            {
                // Allocate unmanaged memory for audio data
                int dataSize = sampleCount * sizeof(float);
                dataPtr = Marshal.AllocHGlobal(dataSize);
                Marshal.Copy(samples, 0, dataPtr, sampleCount);
                
                var audioFrame = new NDIlib_audio_frame_v2_t
                {
                    sample_rate = SAMPLE_RATE,
                    no_channels = NUM_CHANNELS,
                    no_samples = sampleCount,
                    timecode = -1, // Use auto timecode
                    p_data = dataPtr,
                    channel_stride_in_bytes = sampleCount * sizeof(float),
                    p_metadata = IntPtr.Zero,
                    timestamp = 0
                };
                
                NDIlib_send_send_audio_v2(_sendInstance, ref audioFrame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NDI] Send audio error: {ex.Message}");
            }
            finally
            {
                if (dataPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(dataPtr);
            }
        }
        
        #endregion
        
        #region NDI Find/Receive
        
        private void StartSourceDiscovery()
        {
            if (!_isInitialized || NDIlib_find_create_v2 == null)
                return;
            
            _discoveryCts = new CancellationTokenSource();
            var ct = _discoveryCts.Token;
            
            _discoveryTask = Task.Run(() =>
            {
                try
                {
                    var createSettings = new NDIlib_find_create_t
                    {
                        show_local_sources = true,
                        p_groups = IntPtr.Zero,
                        p_extra_ips = IntPtr.Zero
                    };
                    
                    _findInstance = NDIlib_find_create_v2(ref createSettings);
                    
                    if (_findInstance == IntPtr.Zero)
                    {
                        return;
                    }
                    
                    // Continuously discover sources until cancelled
                    while (!ct.IsCancellationRequested && _findInstance != IntPtr.Zero && NDIlib_find_wait_for_sources != null)
                    {
                        if (NDIlib_find_wait_for_sources(_findInstance, 1000))
                        {
                            UpdateSourceList();
                        }
                        
                        // Use cancellation-aware wait instead of Thread.Sleep
                        try { Task.Delay(500, ct).Wait(ct); }
                        catch (OperationCanceledException) { break; }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NDI] Source discovery error: {ex.Message}");
                }
            });
        }
        
        private void UpdateSourceList()
        {
            if (_findInstance == IntPtr.Zero || NDIlib_find_get_current_sources == null)
                return;
            
            try
            {
                var sourcesPtr = NDIlib_find_get_current_sources(_findInstance, out uint numSources);
                
                var newSources = new List<string>();
                
                if (sourcesPtr != IntPtr.Zero && numSources > 0)
                {
                    int sourceSize = Marshal.SizeOf<NDIlib_source_t>();
                    
                    for (uint i = 0; i < numSources; i++)
                    {
                        var sourcePtr = IntPtr.Add(sourcesPtr, (int)(i * sourceSize));
                        var source = Marshal.PtrToStructure<NDIlib_source_t>(sourcePtr);
                        
                        if (source.p_ndi_name != IntPtr.Zero)
                        {
                            string name = Marshal.PtrToStringAnsi(source.p_ndi_name) ?? "";
                            if (!string.IsNullOrEmpty(name))
                            {
                                newSources.Add(name);
                            }
                        }
                    }
                }
                
                // Update if changed
                if (!ListsEqual(DiscoveredSources, newSources))
                {
                    DiscoveredSources = newSources;
                    // Console.WriteLine($"[NDI] Found {newSources.Count} sources: {string.Join(", ", newSources)}");
                    SourcesUpdated?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NDI] Update source list error: {ex.Message}");
            }
        }
        
        private bool ListsEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
        
        public void StartReceiving(string sourceName)
        {
            if (!_isInitialized || string.IsNullOrEmpty(sourceName))
                return;
            
            StopReceiving();
            
            _currentReceiveSource = sourceName;
            _receiveCts = new CancellationTokenSource();
            
            _receiveTask = Task.Run(() => ReceiveLoop(sourceName, _receiveCts.Token));
            
            // Console.WriteLine($"[NDI] Started receiving from: {sourceName}");
        }
        
        public void StopReceiving()
        {
            _receiveCts?.Cancel();
            _receiveTask?.Wait(1000);
            
            if (_recvInstance != IntPtr.Zero && NDIlib_recv_destroy != null)
            {
                NDIlib_recv_destroy(_recvInstance);
                _recvInstance = IntPtr.Zero;
            }
            
            _isReceiving = false;
            _currentReceiveSource = null;
            
            // Console.WriteLine("[NDI] Stopped receiving");
        }
        
        private void ReceiveLoop(string sourceName, CancellationToken ct)
        {
            if (NDIlib_recv_create_v3 == null || NDIlib_recv_capture_v2 == null || NDIlib_recv_free_audio_v2 == null)
            {
                // Console.WriteLine("[NDI] Receive functions not available");
                return;
            }
            
            try
            {
                var namePtr = Marshal.StringToHGlobalAnsi(sourceName);
                
                var source = new NDIlib_source_t
                {
                    p_ndi_name = namePtr,
                    p_url_address = IntPtr.Zero
                };
                
                var recvNamePtr = Marshal.StringToHGlobalAnsi("StudioLog Receiver");
                
                var createSettings = new NDIlib_recv_create_v3_t
                {
                    source_to_connect_to = source,
                    color_format = 0,
                    bandwidth = NDIlib_recv_bandwidth_audio_only,
                    allow_video_fields = false,
                    p_ndi_recv_name = recvNamePtr
                };
                
                _recvInstance = NDIlib_recv_create_v3(ref createSettings);
                
                Marshal.FreeHGlobal(namePtr);
                Marshal.FreeHGlobal(recvNamePtr);
                
                if (_recvInstance == IntPtr.Zero)
                {
                    // Console.WriteLine("[NDI] Failed to create receive instance");
                    return;
                }
                
                _isReceiving = true;
                
                var audioFrame = new NDIlib_audio_frame_v2_t();
                
                while (!ct.IsCancellationRequested)
                {
                    int frameType = NDIlib_recv_capture_v2(_recvInstance, IntPtr.Zero, ref audioFrame, IntPtr.Zero, 100);
                    
                    if (frameType == NDIlib_frame_type_audio)
                    {
                        ProcessReceivedAudio(ref audioFrame);
                        NDIlib_recv_free_audio_v2(_recvInstance, ref audioFrame);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NDI] Receive loop error: {ex.Message}");
            }
            finally
            {
                _isReceiving = false;
            }
        }
        
        private void ProcessReceivedAudio(ref NDIlib_audio_frame_v2_t audioFrame)
        {
            if (audioFrame.p_data == IntPtr.Zero || audioFrame.no_samples == 0)
                return;
            
            try
            {
                // Extract first channel (mono LTC)
                float[] samples = new float[audioFrame.no_samples];
                Marshal.Copy(audioFrame.p_data, samples, 0, audioFrame.no_samples);
                
                AudioReceived?.Invoke(samples, audioFrame.sample_rate);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NDI] Process audio error: {ex.Message}");
            }
        }
        
        #endregion
        
        public void Dispose()
        {
            // Cancel discovery thread first
            _discoveryCts?.Cancel();
            try { _discoveryTask?.Wait(2000); } catch { }
            _discoveryCts?.Dispose();
            _discoveryCts = null;
            
            StopReceiving();
            DisableSend();
            
            if (_findInstance != IntPtr.Zero && NDIlib_find_destroy != null)
            {
                NDIlib_find_destroy(_findInstance);
                _findInstance = IntPtr.Zero;
            }
            
            if (_isInitialized && NDIlib_destroy != null)
            {
                NDIlib_destroy();
                _isInitialized = false;
            }
            
            lock (_instanceLock)
            {
                _instanceCount--;
                if (_instanceCount <= 0 && _ndiLibHandle != IntPtr.Zero)
                {
                    FreeLibrary(_ndiLibHandle);
                    _ndiLibHandle = IntPtr.Zero;
                    _loadedLibraryVersion = null;
                    
                    // Null all static delegates to prevent use-after-free
                    NDIlib_initialize = null;
                    NDIlib_destroy = null;
                    NDIlib_send_create = null;
                    NDIlib_send_destroy = null;
                    NDIlib_send_send_audio_v2 = null;
                    NDIlib_find_create_v2 = null;
                    NDIlib_find_destroy = null;
                    NDIlib_find_wait_for_sources = null;
                    NDIlib_find_get_current_sources = null;
                    NDIlib_recv_create_v3 = null;
                    NDIlib_recv_destroy = null;
                    NDIlib_recv_capture_v2 = null;
                    NDIlib_recv_free_audio_v2 = null;
                    
                    _instanceCount = 0;
                }
            }
        }
    }
}
