# StudioLog - NDI Setup Instructions

## Quick Fix for "NDI Library Not Found"

If you see this error in the console:
```
[NDI] Failed to load NDI library - NDI Runtime may not be installed
```

**Even though you have NDI Tools v6 installed**, follow these steps:

### Step 1: Find the NDI DLL

Open File Explorer and search for:
```
Processing.NDI.Lib.x64.dll
```

Common locations for NDI v6:
- `C:\Program Files\NDI\NDI 6 Tools\Runtime\`
- `C:\Program Files\NDI\NDI 6 Runtime\`
- `C:\Program Files\NDI\NDI 6 Tools\Runtime\v6\`

### Step 2: Copy DLL to App Directory

1. Copy `Processing.NDI.Lib.x64.dll`
2. Paste it in **the same folder as StudioLog.exe**
3. Restart StudioLog

### Step 3: Verify

When you restart, the console should now show:
```
[NDI] Attempting to load NDI library...
[NDI] Trying to load: Processing.NDI.Lib.x64
[NDI] Successfully loaded library: Processing.NDI.Lib.x64
[NDI] Loading function pointers...
[NDI] All function pointers loaded successfully
[NDI] Initialized successfully (using Processing.NDI.Lib.x64)
[MainViewModel] IsNDIAvailable queried: True
```

## Alternative: Add NDI to System PATH

### Option 1: Environment Variables (Permanent)

1. Right-click "This PC" → Properties
2. Click "Advanced system settings"
3. Click "Environment Variables"
4. Under "System variables", select "Path"
5. Click "Edit"
6. Click "New"
7. Add the path where `Processing.NDI.Lib.x64.dll` is located
   Example: `C:\Program Files\NDI\NDI 6 Tools\Runtime\`
8. Click OK on all dialogs
9. **Restart your computer** (or at least restart StudioLog)

### Option 2: Copy to Windows System32 (Not Recommended)

1. Copy `Processing.NDI.Lib.x64.dll`
2. Paste to `C:\Windows\System32\`
3. Restart StudioLog

**Note:** This is not recommended as it can cause version conflicts.

## Verifying NDI Works

After fixing the DLL location:

1. Launch StudioLog
2. Check console shows: `[NDI] Initialized successfully`
3. Go to SETTINGS → AUDIO → NDI Output
4. The menu item should be **enabled** (not greyed out)
5. Click to enable (checkmark appears)
6. Console shows: `[NDI] Send enabled - Source name: 'StudioLog LTC'`

## Testing NDI Output

1. Enable NDI Output in menu
2. Click GENERATE to start LTC
3. Open **NDI Studio Monitor** (from NDI Tools)
4. Look for source: **"StudioLog LTC"**
5. Connect to see the LTC waveform

## Troubleshooting

### Console Shows Error Code

```
[NDI] Failed to load Processing.NDI.Lib.x64.dll, error code: 126
```

**Error 126** = DLL or dependencies not found

**Solution:**
- Make sure you copied the right DLL (x64 version)
- Check if NDI Tools is fully installed
- Try reinstalling NDI Tools v6

### Console Shows Function Pointer Error

```
[NDI] Failed to load function pointers: Function 'NDIlib_initialize' not found
```

**This means:**
- Wrong DLL version (not NDI SDK)
- Corrupted installation

**Solution:**
- Reinstall NDI Tools
- Make sure you have the full NDI Tools, not just NDI Runtime

### Still Greyed Out After Copying DLL

1. Verify DLL is in the **exact same folder** as StudioLog.exe
2. Restart the app completely
3. Check console output carefully
4. Make sure it's the **64-bit DLL** (Processing.NDI.Lib.x64.dll)

## Files Needed

Only one file is needed:
- `Processing.NDI.Lib.x64.dll` (approximately 2-4 MB)

**Do NOT copy:**
- Processing.NDI.Lib.ARM64.dll (wrong architecture)
- Processing.NDI.Lib.x86.dll (32-bit, won't work)

## Console Output Reference

### ✅ Success
```
[NDI] Attempting to load NDI library...
[NDI] Trying to load: Processing.NDI.Lib.x64
[NDI] Successfully loaded library: Processing.NDI.Lib.x64
[NDI] Loading function pointers...
[NDI] All function pointers loaded successfully
[NDI] Initialized successfully (using Processing.NDI.Lib.x64)
[NDI] Compatible with NDI v5 and later
[NDI] Source discovery started
[MainViewModel] IsNDIAvailable queried: True
```

### ❌ DLL Not Found
```
[NDI] Attempting to load NDI library...
[NDI] Trying to load: Processing.NDI.Lib.x64
[NDI] Failed to load Processing.NDI.Lib.x64, error code: 126
[NDI] Trying to load: Processing.NDI.Lib.x64.dll
[NDI] Failed to load Processing.NDI.Lib.x64.dll, error code: 126
[NDI] ========================================
[NDI] NDI DLL not found in system PATH
[NDI] ========================================
```

## Questions?

If this doesn't work, provide:
1. Full console output from app startup
2. NDI Tools version (from NDI Studio Monitor → Help → About)
3. Result of searching for Processing.NDI.Lib.x64.dll on your computer
4. Location where you copied the DLL
